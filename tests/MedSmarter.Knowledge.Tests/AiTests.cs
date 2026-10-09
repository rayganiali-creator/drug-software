using System.Net;
using System.Text;
using MedSmarter.Modules.AI;
using MedSmarter.Modules.AI.Contracts;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Medications.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedSmarter.Knowledge.Tests;

/// <summary>An in-memory HTTP handler: no test in this file can reach the network.</summary>
internal sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public int Calls { get; private set; }
    public HttpRequestMessage? Last { get; private set; }
    public string? LastBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        Last = request;
        LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return await respond(request, cancellationToken);
    }

    public static HttpResponseMessage Json(HttpStatusCode code, string body) => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

public class ProviderTests
{
    private static AiRequest Request(params MedicationKnowledgeDocument[] docs) => new("medication-information", "what is this?", "en", docs, 256);

    private static async Task<MedicationKnowledgeDocument> Doc(string name = "nocturin")
    {
        using var env = new KEnv();
        return (await env.Meds.GetKnowledgeDocumentAsync((await env.One(name)).Id)).Value!;
    }

    private static ExternalAIProvider External(AiOptions o, FakeHandler h) =>
        new(new HttpClient(h), Options.Create(o), NullLogger<ExternalAIProvider>.Instance);

    private static AiOptions Configured() => new() { Provider = "External", BaseUrl = "https://gateway.example.invalid/v1/", ApiKey = "test-key-not-real-0001", Model = "test-model", AllowExternalDataTransfer = true, TimeoutSeconds = 5 };

    [Fact]
    public async Task Mock_provider_runs_without_any_key_and_is_clearly_labelled()
    {
        var r = await new MockAIProvider().CompleteAsync(Request(await Doc(), await Doc("duodemo")));
        Assert.True(r.Succeeded);
        Assert.True(r.Completion!.IsMock);
        Assert.StartsWith(MockAIProvider.Label, r.Completion.Text, StringComparison.Ordinal);
        Assert.Contains("Nocturin", r.Completion.Text, StringComparison.Ordinal);
        Assert.Contains("information not available", r.Completion.Text, StringComparison.Ordinal); // contraindications etc. are not invented
    }

    [Fact]
    public async Task Mock_provider_is_deterministic()
    {
        var doc = await Doc();
        Assert.Equal((await new MockAIProvider().CompleteAsync(Request(doc))).Completion!.Text, (await new MockAIProvider().CompleteAsync(Request(doc))).Completion!.Text);
    }

    [Fact]
    public async Task Disabled_provider_returns_a_controlled_error()
    {
        var r = await new DisabledAIProvider().CompleteAsync(Request());
        Assert.False(r.Succeeded);
        Assert.Equal(AiErrorCode.Disabled, r.Error);
    }

    [Fact]
    public async Task External_provider_without_approval_or_settings_makes_no_http_call()
    {
        var h = new FakeHandler((_, _) => Task.FromResult(FakeHandler.Json(HttpStatusCode.OK, "{}")));
        Assert.Equal(AiErrorCode.NotConfigured, (await External(new AiOptions { Provider = "External" }, h).CompleteAsync(Request())).Error);
        var notApproved = Configured();
        notApproved.AllowExternalDataTransfer = false;
        Assert.Equal(AiErrorCode.NotConfigured, (await External(notApproved, h).CompleteAsync(Request())).Error);
        var noKey = Configured();
        noKey.ApiKey = null;
        Assert.Equal(AiErrorCode.NotConfigured, (await External(noKey, h).CompleteAsync(Request())).Error);
        Assert.Equal(0, h.Calls);
    }

    [Fact]
    public async Task External_provider_speaks_the_neutral_contract_and_never_puts_the_key_in_the_body()
    {
        var h = new FakeHandler((_, _) => Task.FromResult(FakeHandler.Json(HttpStatusCode.OK, "{\"text\":\"hello\",\"model\":\"m1\"}")));
        var r = await External(Configured(), h).CompleteAsync(Request(await Doc()));
        Assert.True(r.Succeeded);
        Assert.Equal("hello", r.Completion!.Text);
        Assert.False(r.Completion.IsMock);
        Assert.Equal("https://gateway.example.invalid/v1/complete", h.Last!.RequestUri!.ToString());
        Assert.Equal("Bearer", h.Last.Headers.Authorization!.Scheme);
        Assert.DoesNotContain("test-key-not-real-0001", h.LastBody!, StringComparison.Ordinal);
        Assert.Contains("\"question\"", h.LastBody!, StringComparison.Ordinal);
        Assert.DoesNotContain("patient", h.LastBody!, StringComparison.OrdinalIgnoreCase); // the request type has no patient data
    }

    [Fact]
    public async Task Timeout_is_reported_as_a_controlled_error()
    {
        var o = Configured();
        o.TimeoutSeconds = 1;
        var h = new FakeHandler(async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(5), ct); return FakeHandler.Json(HttpStatusCode.OK, "{}"); });
        var r = await External(o, h).CompleteAsync(Request());
        Assert.Equal(AiErrorCode.Timeout, r.Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, AiErrorCode.Unavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, AiErrorCode.Unavailable)]
    [InlineData(HttpStatusCode.BadRequest, AiErrorCode.Rejected)]
    [InlineData(HttpStatusCode.Unauthorized, AiErrorCode.Rejected)]
    public async Task Provider_http_errors_are_mapped_without_leaking_details(HttpStatusCode status, AiErrorCode expected)
    {
        var o = Configured();
        var h = new FakeHandler((_, _) => Task.FromResult(FakeHandler.Json(status, "{\"error\":\"secret internal detail https://internal.example key=abc\"}")));
        var r = await External(o, h).CompleteAsync(Request());
        Assert.Equal(expected, r.Error);
        Assert.DoesNotContain("internal", r.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(o.ApiKey!, r.SafeMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"text\":\"   \"}")]
    public async Task Unusable_provider_answers_are_rejected(string body)
    {
        var h = new FakeHandler((_, _) => Task.FromResult(FakeHandler.Json(HttpStatusCode.OK, body)));
        Assert.Equal(AiErrorCode.InvalidResponse, (await External(Configured(), h).CompleteAsync(Request())).Error);
    }

    [Fact]
    public async Task Network_failure_is_a_controlled_error()
    {
        var h = new FakeHandler((_, _) => throw new HttpRequestException("connection refused to https://secret.example"));
        var r = await External(Configured(), h).CompleteAsync(Request());
        Assert.Equal(AiErrorCode.Unavailable, r.Error);
        Assert.DoesNotContain("secret.example", r.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Local_provider_contract_is_ready_but_reports_missing_runtime_honestly()
    {
        Assert.Equal(AiErrorCode.NotConfigured, (await new LocalAIProvider().CompleteAsync(Request())).Error);
        var withRuntime = new LocalAIProvider(new FakeRuntime("local answer"));
        Assert.Equal("local answer", (await withRuntime.CompleteAsync(Request())).Completion!.Text);
        Assert.Equal(AiErrorCode.InvalidResponse, (await new LocalAIProvider(new FakeRuntime(null)).CompleteAsync(Request())).Error);
    }

    private sealed class FakeRuntime(string? text) : ILocalModelRuntime
    {
        public Task<string?> GenerateAsync(AiRequest request, CancellationToken ct) => Task.FromResult(text);
    }

    [Fact]
    public void Mock_is_refused_outside_development_unless_explicitly_allowed()
    {
        var mock = new AiOptions { Provider = "Mock" };
        Assert.Throws<InvalidOperationException>(() => AiGuard.EnsureSafe("Production", mock));
        Assert.Throws<InvalidOperationException>(() => AiGuard.EnsureSafe("Staging", mock));
        AiGuard.EnsureSafe("Development", mock);
        AiGuard.EnsureSafe("Testing", mock);
        AiGuard.EnsureSafe("Production", new AiOptions { Provider = "Mock", AllowMockInProduction = true });
        AiGuard.EnsureSafe("Production", new AiOptions()); // default = Disabled
        Assert.Throws<InvalidOperationException>(() => AiGuard.EnsureSafe("Development", new AiOptions { Provider = "Banana" }));
    }

    [Fact]
    public void The_default_provider_is_disabled_so_nothing_runs_by_accident() => Assert.Equal(AiProviderKind.Disabled, new AiOptions().Kind);
}

public class AssistantTests
{
    [Fact]
    public async Task Answers_only_from_retrieved_sources_and_cites_them()
    {
        using var env = new KEnv();
        var svc = env.Get<IAIAssistantService>();
        var a = await svc.AskAsync(KEnv.Actor, new AssistantQuestion("Tell me about Nocturin", "en", null), "test", null);
        Assert.True(a.Answered);
        Assert.True(a.IsMock);
        Assert.Equal("mock", a.Provider);
        Assert.NotEmpty(a.Sources);
        Assert.Contains("DEMO DATA", a.Notice, StringComparison.Ordinal);
        Assert.StartsWith(MockAIProvider.Label, a.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_source_the_provider_is_not_called()
    {
        var calls = 0;
        using var env = new KEnv(settings: new() { ["Ai:Provider"] = "External", ["Ai:AllowExternalDataTransfer"] = "true", ["Ai:BaseUrl"] = "https://gateway.example.invalid/", ["Ai:ApiKey"] = "k", ["Ai:Model"] = "m" });
        _ = calls;
        var a = await env.Get<IAIAssistantService>().AskAsync(KEnv.Actor, new AssistantQuestion("xyzzyqq nothing like this", "en", null), "test", null);
        Assert.False(a.Answered);
        Assert.Equal("none", a.Provider);
        Assert.Contains("No information", a.Text, StringComparison.Ordinal);
        Assert.Empty(await env.Audit.QueryAsync(new AuditQuery(Action: AuditActions.AiProviderCalled)));
    }

    [Fact]
    public async Task Inactive_medications_are_not_used_as_context()
    {
        using var env = new KEnv();
        var oldmed = (await env.Meds.SearchAsync(new MedicationSearchQuery("oldmed", 5, 0, true))).Items.Single();
        var a = await env.Get<IAIAssistantService>().AskAsync(KEnv.Actor, new AssistantQuestion("what is it", "en", [oldmed.Id]), "test", null);
        Assert.False(a.Answered);
    }

    [Fact]
    public async Task A_disabled_provider_gives_a_controlled_non_answer_and_is_audited_without_the_question()
    {
        using var env = new KEnv(settings: new() { ["Ai:Provider"] = "Disabled" });
        var a = await env.Get<IAIAssistantService>().AskAsync(KEnv.Actor, new AssistantQuestion("Nocturin secret-question-text", "en", null), "test", null);
        Assert.False(a.Answered);
        Assert.Equal(AiErrorCode.Disabled, a.Error);
        var entry = Assert.Single(await env.Audit.QueryAsync(new AuditQuery(Action: AuditActions.AiProviderCalled)));
        Assert.Equal(AuditResult.Failure, entry.Result);
        Assert.DoesNotContain("secret-question-text", string.Join(' ', entry.Metadata.Values) + entry.ReasonCode, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\u0000control")]
    public async Task Invalid_questions_are_rejected(string q)
    {
        using var env = new KEnv();
        await Assert.ThrowsAsync<AssistantInputException>(() => env.Get<IAIAssistantService>().AskAsync(KEnv.Actor, new AssistantQuestion(q, "en", null), "t", null));
    }

    [Fact]
    public async Task Too_long_questions_and_unknown_locales_are_rejected()
    {
        using var env = new KEnv();
        var svc = env.Get<IAIAssistantService>();
        await Assert.ThrowsAsync<AssistantInputException>(() => svc.AskAsync(KEnv.Actor, new AssistantQuestion(new string('a', 501), "en", null), "t", null));
        await Assert.ThrowsAsync<AssistantInputException>(() => svc.AskAsync(KEnv.Actor, new AssistantQuestion("nocturin", "xx", null), "t", null));
    }

    [Fact]
    public void Status_reports_configuration_but_never_the_key()
    {
        using var env = new KEnv(settings: new() { ["Ai:Provider"] = "External", ["Ai:ApiKey"] = "super-secret-value", ["Ai:Model"] = "m" });
        var s = env.Get<IAIAssistantService>().Status();
        Assert.True(s.ApiKeyPresent);
        Assert.False(s.Configured); // no approval, no URL
        Assert.DoesNotContain("super-secret-value", System.Text.Json.JsonSerializer.Serialize(s), StringComparison.Ordinal);
    }

    [Fact]
    public void Provider_is_chosen_by_settings_only()
    {
        foreach (var (name, kind) in new[] { ("Mock", AiProviderKind.Mock), ("External", AiProviderKind.External), ("Local", AiProviderKind.Local), ("Disabled", AiProviderKind.Disabled) })
        {
            using var env = new KEnv(settings: new() { ["Ai:Provider"] = name });
            Assert.Equal(kind, env.Get<IAIProvider>().Kind);
        }
    }
}
