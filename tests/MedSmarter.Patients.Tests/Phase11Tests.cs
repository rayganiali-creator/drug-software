using System.Reflection;
using System.Text.Json;
using MedSmarter.Modules.AI.Contracts;
using MedSmarter.Modules.Integrations;
using MedSmarter.Modules.Integrations.Contracts;
using MedSmarter.Modules.ProductTrace.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace MedSmarter.Patients.Tests;

/// <summary>Phase 11: every integration is disabled or a labelled local mock; "real" is fail-closed; mock mode makes no network request.</summary>
public class Phase11Tests
{
    private static IConfiguration Config(params (string, string)[] kv) => new ConfigurationBuilder().AddInMemoryCollection(kv.ToDictionary(x => x.Item1, x => (string?)x.Item2)).Build();

    private static ServiceProvider Adapters(IConfiguration c)
    {
        var s = new ServiceCollection();
        s.AddLogging();
        new IntegrationsModule().Register(s, c);
        return s.BuildServiceProvider();
    }

    [Fact]
    public async Task By_default_every_adapter_is_disabled_and_does_nothing()
    {
        using var sp = Adapters(Config());
        Assert.IsType<DisabledMedicationReferenceAdapter>(sp.GetRequiredService<IMedicationReferenceAdapter>());
        Assert.IsType<DisabledProviderVerificationAdapter>(sp.GetRequiredService<IProviderVerificationAdapter>());
        Assert.IsType<DisabledElectronicPrescriptionAdapter>(sp.GetRequiredService<IElectronicPrescriptionAdapter>());
        Assert.IsType<DisabledNationalReportingAdapter>(sp.GetRequiredService<INationalReportingAdapter>());
        var r = await sp.GetRequiredService<INationalReportingAdapter>().SubmitAsync(new NationalReportEnvelope("x", "ref", true, true), "k");
        Assert.Equal(AdapterOutcome.NotConfigured, r.Outcome);
        Assert.Null(r.Value);
        Assert.False(r.IsSimulated);
        Assert.All(sp.GetRequiredService<IIntegrationRegistry>().Describe().Where(d => d.Id is "medication-reference" or "provider-verification" or "e-prescription" or "national-pv-reporting"), d =>
        {
            Assert.Equal(IntegrationState.Inactive, d.State);
            Assert.False(d.UsesNetwork);
            Assert.NotEmpty(d.RequiredBeforeReal);
        });
    }

    [Fact]
    public async Task Mock_mode_returns_labelled_simulated_results_and_the_national_mock_refuses_without_review_and_legal_basis()
    {
        using var sp = Adapters(Config(("Integrations:MedicationReference:Mode", "Mock"), ("Integrations:ProviderVerification:Mode", "Mock"), ("Integrations:ElectronicPrescription:Mode", "Mock"), ("Integrations:NationalReporting:Mode", "Mock")));
        var med = await sp.GetRequiredService<IMedicationReferenceAdapter>().LookupAsync(new("X-1", "demo"));
        Assert.True(med.IsSimulated);
        Assert.True(med.Value!.IsFictional);
        Assert.Equal("MOCK", med.Value.Source);
        Assert.Equal(AdapterOutcome.Rejected, (await sp.GetRequiredService<IMedicationReferenceAdapter>().LookupAsync(new(" ", "demo"))).Outcome);
        var ver = sp.GetRequiredService<IProviderVerificationAdapter>();
        Assert.Equal(ProviderVerificationStatus.SimulatedVerified, (await ver.VerifyAsync(new("DEMO-1", "Pharmacist"))).Value!.Status);
        Assert.Equal(ProviderVerificationStatus.Unverified, (await ver.VerifyAsync(new("123456", "Pharmacist"))).Value!.Status); // a real-looking number is never "verified"
        Assert.True((await sp.GetRequiredService<IElectronicPrescriptionAdapter>().FetchAsync(new("RX-1"))).Value!.IsFictional);

        var national = sp.GetRequiredService<INationalReportingAdapter>();
        Assert.Equal("human_review_required", (await national.SubmitAsync(new("v", "ref", false, true), "k1")).ErrorCode);
        Assert.Equal("legal_basis_required", (await national.SubmitAsync(new("v", "ref", true, false), "k1")).ErrorCode);
        var a = await national.SubmitAsync(new("v", "ref", true, true), "k1");
        var b = await national.SubmitAsync(new("v", "ref", true, true), "k1");
        Assert.True(a.IsSimulated);
        Assert.StartsWith("MOCK-NATIONAL-", a.Value!.Reference, StringComparison.Ordinal);
        Assert.Equal(a.Value.Reference, b.Value!.Reference); // same key, same receipt
        Assert.Equal(1, ((MockNationalReportingAdapter)national).ReceiptCount);
        Assert.NotEqual(a.Value.Reference, (await national.SubmitAsync(new("v", "ref", true, true), "k2")).Value!.Reference);
    }

    [Fact]
    public async Task Asking_for_a_real_integration_is_fail_closed_the_disabled_adapter_stays_and_the_registry_says_so()
    {
        var c = Config(("Integrations:NationalReporting:Mode", "Real"), ("Integrations:MedicationReference:Mode", "real"), ("ManufacturerReports:Provider", "SomeRealVendor"), ("Integrations:NationalReporting:Endpoint", "https://example.invalid"), ("Integrations:NationalReporting:ApiKey", "secret"));
        using var sp = Adapters(c);
        Assert.IsType<DisabledNationalReportingAdapter>(sp.GetRequiredService<INationalReportingAdapter>());
        Assert.IsType<DisabledMedicationReferenceAdapter>(sp.GetRequiredService<IMedicationReferenceAdapter>());
        Assert.Equal(AdapterOutcome.NotConfigured, (await sp.GetRequiredService<INationalReportingAdapter>().SubmitAsync(new("v", "r", true, true), "k")).Outcome);
        var d = sp.GetRequiredService<IIntegrationRegistry>().Describe();
        Assert.Equal(IntegrationState.RealBlocked, d.Single(x => x.Id == "national-pv-reporting").State);
        Assert.Equal(IntegrationState.RealBlocked, d.Single(x => x.Id == "medication-reference").State);
        Assert.Equal(IntegrationState.RealBlocked, d.Single(x => x.Id == "manufacturer-reporting").State);
        Assert.All(d.Where(x => x.State == IntegrationState.RealBlocked), x => Assert.Contains("fail-closed", x.Blocker, StringComparison.Ordinal));
        // the registry never echoes configuration values, endpoints or keys
        var json = JsonSerializer.Serialize(d);
        Assert.DoesNotContain("example.invalid", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Insurance_stays_administrative_and_the_registry_says_pharmacovigilance_records_are_never_part_of_it()
    {
        using var sp = Adapters(Config(("Integrations:Insurance:EnableMock", "true")));
        var ins = sp.GetRequiredService<IIntegrationRegistry>().Describe().Single(x => x.Kind == IntegrationKind.InsuranceCoverage);
        Assert.Equal(IntegrationState.MockActive, ins.State);
        Assert.True(ins.ResultsAreSimulated);
        Assert.Contains(ins.RequiredBeforeReal, x => x.Contains("adverse-event and pharmacy-safety", StringComparison.Ordinal));
    }

    [Fact]
    public void The_integration_assemblies_declare_and_use_no_http_client_or_socket_type()
    {
        // MockInsuranceProvider throws HttpRequestException only to SIMULATE an outage; that is an exception type, not a client. No member may hold or build a client or handler.
        var banned = new[] { typeof(HttpClient), typeof(HttpMessageHandler), typeof(System.Net.Sockets.Socket), typeof(IHttpClientFactory) };
        foreach (var asm in new[] { typeof(IntegrationsModule).Assembly, typeof(IIntegrationRegistry).Assembly })
        {
            Assert.DoesNotContain(asm.GetReferencedAssemblies().Select(a => a.Name!), n => n.Contains("System.Net.Sockets", StringComparison.Ordinal));
            foreach (var t in asm.GetTypes())
            {
                var used = t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Select(f => f.FieldType)
                    .Concat(t.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Select(p => p.PropertyType))
                    .Concat(t.GetConstructors().SelectMany(c => c.GetParameters().Select(p => p.ParameterType)))
                    .Concat(t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)));
                Assert.DoesNotContain(used, u => banned.Any(b => b.IsAssignableFrom(u) || u.IsAssignableFrom(b) && u != typeof(object)));
            }
        }
    }

    private sealed class CountingHandler(Action hit) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            hit();
            throw new InvalidOperationException("An outbound HTTP request was attempted in mock mode.");
        }
    }

    [Fact]
    public async Task Exercising_every_mock_path_makes_no_outbound_http_request()
    {
        var hits = 0;
        using var env = new PEnv(settings: new()
        {
            ["Integrations:MedicationReference:Mode"] = "Mock", ["Integrations:ProviderVerification:Mode"] = "Mock", ["Integrations:ElectronicPrescription:Mode"] = "Mock", ["Integrations:NationalReporting:Mode"] = "Mock",
            ["Integrations:Insurance:EnableMock"] = "true",
        }, configureServices: s => s.ConfigureAll<HttpClientFactoryOptions>(o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = new CountingHandler(() => Interlocked.Increment(ref hits)))));
        // every adapter
        await env.Get<IMedicationReferenceAdapter>().LookupAsync(new("A", "demo"));
        await env.Get<IProviderVerificationAdapter>().VerifyAsync(new("DEMO-1", "Pharmacist"));
        await env.Get<IElectronicPrescriptionAdapter>().FetchAsync(new("RX"));
        await env.Get<INationalReportingAdapter>().SubmitAsync(new("v", "r", true, true), "k");
        // the existing mocks: manufacturer report provider and the AI provider
        var payload = new ManufacturerReportPayload("ref", "P", null, null, "B", null, DateOnly.FromDateTime(DateTime.UtcNow), null, "QualityProblem", "Unknown", DateOnly.FromDateTime(DateTime.UtcNow), null, "Adult", "Unknown", [], null, true, "v1");
        Assert.Equal(SubmissionOutcome.Accepted, (await env.Get<IManufacturerReportProvider>().SubmitAsync(payload, "idem-1")).Outcome);
        var ai = env.Get<IAIProvider>();
        Assert.Equal(AiProviderKind.Mock, ai.Kind);
        await ai.CompleteAsync(new AiRequest("qa", "hello", "en", [], 100));
        Assert.Equal(0, hits);
        // and the registry in this configuration shows no real integration
        Assert.DoesNotContain(env.Get<IIntegrationRegistry>().Describe(), d => d.State is IntegrationState.RealBlocked or IntegrationState.RealGatedElsewhere);
    }
}

public class Phase11ApiTests(PatientsApiFactory factory) : IClassFixture<PatientsApiFactory>
{
    private Task<ClinicalApiTests.Session> As(string account) => ClinicalApiTests.Login(factory, account);

    [Fact]
    public async Task The_registry_is_401_without_a_token_and_visible_only_to_the_system_administrator()
    {
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/integrations/registry")).StatusCode);
        foreach (var who in new[] { "demo-patient", "demo-physician", "demo-pharmacist", "demo-pharmacy-admin", "demo-industry", "demo-researcher", "demo-content-manager" })
        {
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await (await As(who)).Client.GetAsync("/integrations/registry")).StatusCode);
        }

        var res = await (await As("demo-system-admin")).Client.GetAsync("/integrations/registry");
        Assert.Equal(System.Net.HttpStatusCode.OK, res.StatusCode);
        var j = await ClinicalApiTests.Json(res);
        Assert.False(j.GetProperty("anyRealIntegrationActive").GetBoolean());
        Assert.Contains("fail-closed", j.GetProperty("notice").GetString(), StringComparison.Ordinal);
        Assert.Equal(8, j.GetProperty("integrations").GetArrayLength());
        Assert.All(j.GetProperty("integrations").EnumerateArray(), d => Assert.False(d.GetProperty("usesNetwork").GetBoolean()));
        var text = j.ToString();
        Assert.DoesNotContain("http", text.Replace("https://", "", StringComparison.Ordinal).Replace("HTTP", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
    }
}
