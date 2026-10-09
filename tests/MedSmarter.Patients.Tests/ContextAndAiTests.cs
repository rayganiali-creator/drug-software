using System.Net;
using System.Text;
using System.Text.Json;
using MedSmarter.Modules.AI;
using MedSmarter.Modules.AI.Contracts;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Consent.Contracts;
using MedSmarter.Modules.Identity.Contracts;
using MedSmarter.Modules.Medications.Contracts;
using MedSmarter.Modules.Patients.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedSmarter.Patients.Tests;

internal sealed class CapturingHandler : HttpMessageHandler
{
    public int Calls { get; private set; }
    public string? Body { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"text\":\"fake gateway answer\",\"model\":\"m\"}", Encoding.UTF8, "application/json") };
    }
}

public class PatientContextTests
{
    private static async Task<(PEnv Env, Guid Id)> Rich()
    {
        var env = new PEnv();
        var id = await env.Patient("demo-patient");
        await env.Patients.UpdateProfileAsync(id, id, new UpdateProfileCommand(1990, SexGroup.Female, 62, 165, "Asia/Tehran", null), "t", null);
        await env.TakeMedication(id);
        await env.Patients.AddAllergyAsync(id, id, new AllergyInput(AllergenKind.Other, null, "pollen", AllergySeverity.Mild, "sneezing"), "t", null);
        await env.Patients.AddConditionAsync(id, id, new ConditionInput("Seasonal rhinitis", null, ConditionStatus.Active, "private note"), "t", null);
        await env.Patients.AddSymptomAsync(id, id, new SymptomInput("cough", SymptomSeverity.Mild, env.Clock.UtcNow.AddDays(-1), null, null, "private symptom note"), "t", null);
        return (env, id);
    }

    [Fact]
    public async Task Without_consent_nothing_is_included_and_the_gaps_are_listed()
    {
        var (env, id) = await Rich();
        using var _ = env;
        var ctx = (await env.Context.BuildAsync(id, PatientContextPurpose.AiAssistant)).Value!;
        Assert.Empty(ctx.Medications);
        Assert.Empty(ctx.Allergies);
        Assert.Empty(ctx.Conditions);
        Assert.Empty(ctx.RecentSymptoms);
        Assert.Equal("unknown", ctx.AgeGroup);
        Assert.Equal("unknown", ctx.SexGroup);
        Assert.False(ctx.ExternalProcessingConsented);
        Assert.Equal(5, ctx.Excluded.Count);
        Assert.All(ctx.Excluded, e => Assert.Equal("consent_required", e.Reason));
    }

    [Fact]
    public async Task Only_the_consented_categories_are_included_and_nothing_identifies_the_person()
    {
        var (env, id) = await Rich();
        using var _ = env;
        await env.Consent("demo-patient", ConsentPurposes.AiProcessing, [DataScopes.Medications, DataScopes.Allergies, DataScopes.Profile]);
        var ctx = (await env.Context.BuildAsync(id, PatientContextPurpose.AiAssistant)).Value!;
        Assert.Equal("Demopril", Assert.Single(ctx.Medications).Name);
        Assert.Equal("5 mg", ctx.Medications[0].Dose);
        Assert.Equal("pollen", Assert.Single(ctx.Allergies).Substance);
        Assert.Empty(ctx.Conditions); // no consent for conditions
        Assert.Empty(ctx.RecentSymptoms);
        Assert.Equal("18-39", ctx.AgeGroup);
        Assert.Equal(["Conditions", "Symptoms"], ctx.Excluded.Select(e => e.Category.ToString()).Order());
        Assert.Contains(ConsentPurposes.AiProcessing, ctx.ConsentedPurposes);

        var json = JsonSerializer.Serialize(ctx);
        // Short numbers (weight, height) could appear by chance inside a random GUID, so they are looked for with GUIDs removed.
        var withoutGuids = System.Text.RegularExpressions.Regex.Replace(json, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", "");
        foreach (var leak in new[] { id.ToString(), "Sara", "1990", "example.invalid", "private note", "private symptom note", "Asia/Tehran", "Seasonal rhinitis" })
        {
            Assert.DoesNotContain(leak, json, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var leak in new[] { "62", "165" })
        {
            Assert.DoesNotContain(leak, withoutGuids, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Consent_for_monitoring_does_not_open_the_ai_context_and_the_reverse()
    {
        var (env, id) = await Rich();
        using var _ = env;
        await env.Consent("demo-patient", ConsentPurposes.Monitoring, [DataScopes.Medications]);
        Assert.Empty((await env.Context.BuildAsync(id, PatientContextPurpose.AiAssistant)).Value!.Medications);
        Assert.Single((await env.Context.BuildAsync(id, PatientContextPurpose.Monitoring)).Value!.Medications);
    }

    [Fact]
    public async Task Revoking_the_consent_empties_the_context_on_the_next_build()
    {
        var (env, id) = await Rich();
        using var _ = env;
        var c = await env.Consent("demo-patient", ConsentPurposes.AiProcessing, [DataScopes.Medications]);
        Assert.Single((await env.Context.BuildAsync(id, PatientContextPurpose.AiAssistant)).Value!.Medications);
        await env.Consents.RevokeAsync(id, c.Id, "t", null);
        Assert.Empty((await env.Context.BuildAsync(id, PatientContextPurpose.AiAssistant)).Value!.Medications);
    }

    [Fact]
    public async Task External_processing_consent_is_a_separate_decision()
    {
        var (env, id) = await Rich();
        using var _ = env;
        await env.Consent("demo-patient", ConsentPurposes.AiProcessing, [DataScopes.Medications]);
        Assert.False((await env.Context.BuildAsync(id, PatientContextPurpose.AiAssistant)).Value!.ExternalProcessingConsented);
        await env.Consent("demo-patient", ConsentPurposes.AiExternalProcessing, [DataScopes.Medications]);
        Assert.True((await env.Context.BuildAsync(id, PatientContextPurpose.AiAssistant)).Value!.ExternalProcessingConsented);
    }

    [Fact]
    public async Task Stopped_and_removed_medicines_are_not_part_of_the_context()
    {
        var (env, id) = await Rich();
        using var _ = env;
        await env.Consent("demo-patient", ConsentPurposes.AiProcessing, [DataScopes.Medications]);
        var med = (await env.Meds.ListAsync(id, false)).Value![0];
        await env.Meds.StopAsync(id, id, med.Id, new StopMedicationCommand(null, null, med.Version), "t", null);
        Assert.Empty((await env.Context.BuildAsync(id, PatientContextPurpose.AiAssistant)).Value!.Medications);
    }

    [Fact]
    public async Task A_deactivated_or_missing_patient_has_no_context()
    {
        using var env = new PEnv();
        Assert.Equal(PatientError.NotFound, (await env.Context.BuildAsync(Guid.NewGuid(), PatientContextPurpose.AiAssistant)).Error);
        var id = await env.Patient("demo-patient");
        await env.Patients.SetStatusAsync(id, id, PatientStatus.Inactive, "t", null);
        Assert.Equal(PatientError.Forbidden, (await env.Context.BuildAsync(id, PatientContextPurpose.AiAssistant)).Error);
    }
}

public class AiPatientDataGateTests
{
    private sealed record Rig(PEnv Env, Guid Id, AssistantService Assistant, CapturingHandler Handler);

    private static async Task<Rig> Arrange(AiOptions options)
    {
        var env = new PEnv();
        var id = await env.Patient("demo-patient");
        await env.TakeMedication(id);
        var handler = new CapturingHandler();
        var opts = Options.Create(options);
        var provider = new ConfiguredAIProvider(opts, new MockAIProvider(), new ExternalAIProvider(new HttpClient(handler), opts, NullLogger<ExternalAIProvider>.Instance), new LocalAIProvider(), new DisabledAIProvider());
        var assistant = new AssistantService(provider, env.Reference, env.Get<IAuditWriter>(), opts, env.Context);
        return new Rig(env, id, assistant, handler);
    }

    private static AiOptions External(bool transfer) => new() { Provider = "External", BaseUrl = "https://gateway.example.invalid/v1/", ApiKey = "test-key-not-real-0001", Model = "test-model", AllowExternalDataTransfer = transfer, TimeoutSeconds = 5 };

    private static AssistantQuestion Ask(bool includeContext) => new("tell me about demopril", "en", null, includeContext);

    [Fact]
    public async Task The_mock_receives_the_consented_context_and_the_use_is_audited()
    {
        var r = await Arrange(new AiOptions { Provider = "Mock" });
        using var _ = r.Env;
        await r.Env.Consent("demo-patient", ConsentPurposes.AiProcessing, [DataScopes.Medications]);
        var answer = await r.Assistant.AskAsync(r.Id, Ask(true), "t", null);
        Assert.True(answer.PatientContextUsed);
        Assert.Equal("patient_context.used", answer.PatientContextNote);
        Assert.Contains("Patient context received: 1 medicines", answer.Text, StringComparison.Ordinal);
        Assert.Contains("No clinical analysis", answer.Text, StringComparison.Ordinal);
        Assert.Contains(await r.Env.Audit.QueryAsync(new AuditQuery(Action: AuditActions.AiPatientContextUsed)), e => e.SubjectUserId == r.Id);
    }

    [Fact]
    public async Task Without_consent_the_question_is_answered_without_any_patient_data()
    {
        var r = await Arrange(new AiOptions { Provider = "Mock" });
        using var _ = r.Env;
        var answer = await r.Assistant.AskAsync(r.Id, Ask(true), "t", null);
        Assert.False(answer.PatientContextUsed);
        Assert.Equal("patient_context.no_consented_data", answer.PatientContextNote);
        Assert.DoesNotContain("Patient context received", answer.Text, StringComparison.Ordinal);
        Assert.True(answer.Answered);
        Assert.Empty(await r.Env.Audit.QueryAsync(new AuditQuery(Action: AuditActions.AiPatientContextUsed)));
    }

    [Fact]
    public async Task Not_asking_for_context_never_loads_it()
    {
        var r = await Arrange(new AiOptions { Provider = "Mock" });
        using var _ = r.Env;
        await r.Env.Consent("demo-patient", ConsentPurposes.AiProcessing, [DataScopes.Medications]);
        var answer = await r.Assistant.AskAsync(r.Id, Ask(false), "t", null);
        Assert.False(answer.PatientContextUsed);
        Assert.Null(answer.PatientContextNote);
    }

    [Fact]
    public async Task The_global_flag_alone_never_sends_patient_data_to_an_external_provider()
    {
        var r = await Arrange(External(transfer: true));
        using var _ = r.Env;
        await r.Env.Consent("demo-patient", ConsentPurposes.AiProcessing, [DataScopes.Medications]); // in-house consent only
        var answer = await r.Assistant.AskAsync(r.Id, Ask(true), "t", null);
        Assert.True(answer.Answered);
        Assert.False(answer.PatientContextUsed);
        Assert.Equal("patient_context.external_processing_consent_required", answer.PatientContextNote);
        Assert.Equal(1, r.Handler.Calls);
        Assert.DoesNotContain("\"patient\"", r.Handler.Body!, StringComparison.Ordinal);
        Assert.DoesNotContain("Demopril 5", r.Handler.Body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Patient_data_goes_out_only_with_the_flag_AND_the_patients_external_consent()
    {
        var r = await Arrange(External(transfer: true));
        using var _ = r.Env;
        await r.Env.Consent("demo-patient", ConsentPurposes.AiProcessing, [DataScopes.Medications]);
        await r.Env.Consent("demo-patient", ConsentPurposes.AiExternalProcessing, [DataScopes.Medications]);
        var answer = await r.Assistant.AskAsync(r.Id, Ask(true), "t", null);
        Assert.True(answer.PatientContextUsed);
        Assert.Contains("\"patient\"", r.Handler.Body!, StringComparison.Ordinal);
        foreach (var leak in new[] { r.Id.ToString(), "Sara", "example.invalid" })
        {
            Assert.DoesNotContain(leak, r.Handler.Body!, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task The_patients_consent_without_the_global_approval_sends_nothing_at_all()
    {
        var r = await Arrange(External(transfer: false));
        using var _ = r.Env;
        await r.Env.Consent("demo-patient", ConsentPurposes.AiProcessing, [DataScopes.Medications]);
        await r.Env.Consent("demo-patient", ConsentPurposes.AiExternalProcessing, [DataScopes.Medications]);
        var answer = await r.Assistant.AskAsync(r.Id, Ask(true), "t", null);
        Assert.False(answer.Answered); // the provider itself refuses: transfer is not approved
        Assert.Equal(0, r.Handler.Calls);
        Assert.False(answer.PatientContextUsed);
    }
}
