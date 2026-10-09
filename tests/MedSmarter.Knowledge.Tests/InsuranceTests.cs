using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Identity.Contracts;
using MedSmarter.Modules.Integrations;
using MedSmarter.Modules.Integrations.Contracts;

namespace MedSmarter.Knowledge.Tests;

public class InsuranceTests
{
    private static readonly Guid PatientA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid PatientB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    private static CurrentUser User(params string[] permissions) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Test integration (fictional)", new HashSet<string>(), new HashSet<string>(permissions), new HashSet<Guid>());

    private static readonly CurrentUser Importer = User(Permissions.InsuranceImport, Permissions.InsuranceRead);

    private static IInsuranceIntegrationService Svc(KEnv env) => env.Get<IInsuranceIntegrationService>();

    private static InsuranceMember Member(string id = "DEMO-INS-0001", string plan = "DEMO-BASIC", CoverageStatus status = CoverageStatus.Active) =>
        new("DEMO-INS", id, new InsuranceCoverage(plan, status, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));

    private static InsuranceBatch Batch(string id = "DEMO-BATCH-001", DateTimeOffset? at = null, params InsuranceMember[] members) =>
        new("mock-insurance", id, at ?? new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), members.Length == 0 ? [Member()] : members);

    [Fact]
    public async Task Without_the_permission_nothing_is_imported_and_the_denial_is_audited()
    {
        using var env = new KEnv();
        var r = await Svc(env).ImportAsync(User(), Batch(), "test", null);
        Assert.Equal(InsuranceError.Forbidden, r.Error);
        Assert.Empty((await Svc(env).ListImportsAsync(Importer)).Value!);
        Assert.NotEmpty(await env.Audit.QueryAsync(new AuditQuery(Action: AuditActions.AccessDenied)));
        Assert.Equal(InsuranceError.Forbidden, (await Svc(env).GetCoverageAsync(User(), PatientA, "DEMO-INS")).Error);
        Assert.Equal(InsuranceError.Forbidden, (await Svc(env).LinkPatientAsync(User(), new ExternalPatientIdentifier("DEMO-INS", "DEMO-INS-0001"), PatientA, "t", null)).Error);
    }

    [Fact]
    public async Task Unmapped_members_are_reported_and_never_create_patients()
    {
        using var env = new KEnv();
        var r = (await Svc(env).ImportAsync(Importer, Batch(), "test", null)).Value!;
        Assert.Equal(1, r.Unmapped);
        Assert.Equal(0, r.Applied);
        Assert.Equal(ImportStatus.CompletedWithIssues, r.Status);
        Assert.Equal(InsuranceError.NotFound, (await Svc(env).GetCoverageAsync(Importer, PatientA, "DEMO-INS")).Error);
    }

    [Fact]
    public async Task A_linked_member_gets_coverage_and_a_retry_after_linking_applies_it()
    {
        using var env = new KEnv();
        var first = (await Svc(env).ImportAsync(Importer, Batch(), "test", null)).Value!;
        Assert.Equal(1, first.Unmapped);
        await Svc(env).LinkPatientAsync(Importer, new ExternalPatientIdentifier("DEMO-INS", "DEMO-INS-0001"), PatientA, "t", null);
        var retried = (await Svc(env).RetryAsync(Importer, first.Id, "test", null)).Value!;
        Assert.Equal(1, retried.Applied);
        var cov = (await Svc(env).GetCoverageAsync(Importer, PatientA, "DEMO-INS")).Value!;
        Assert.Equal("DEMO-BASIC", cov.PlanCode);
        Assert.Equal(CoverageStatus.Active, cov.Status);
    }

    [Fact]
    public async Task Reimporting_the_same_batch_is_idempotent_and_a_reused_batch_id_with_other_content_is_a_conflict()
    {
        using var env = new KEnv();
        await Svc(env).LinkPatientAsync(Importer, new ExternalPatientIdentifier("DEMO-INS", "DEMO-INS-0001"), PatientA, "t", null);
        var one = (await Svc(env).ImportAsync(Importer, Batch(), "test", null)).Value!;
        Assert.Equal(1, one.Applied);
        var again = (await Svc(env).ImportAsync(Importer, Batch(), "test", null)).Value!;
        Assert.Equal(ImportStatus.Duplicate, again.Status);
        Assert.Equal(one.Id, again.Id);
        Assert.Single((await Svc(env).ListImportsAsync(Importer)).Value!);
        var changed = await Svc(env).ImportAsync(Importer, Batch(members: Member(plan: "DEMO-PLUS")), "test", null);
        Assert.Equal(InsuranceError.Conflict, changed.Error);
    }

    [Fact]
    public async Task Newer_data_updates_older_or_simultaneous_conflicting_data_is_flagged()
    {
        using var env = new KEnv();
        await Svc(env).LinkPatientAsync(Importer, new ExternalPatientIdentifier("DEMO-INS", "DEMO-INS-0001"), PatientA, "t", null);
        var t1 = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        await Svc(env).ImportAsync(Importer, Batch("BATCH-1", t1), "test", null);
        var newer = (await Svc(env).ImportAsync(Importer, Batch("BATCH-2", t1.AddDays(1), Member(plan: "DEMO-PLUS")), "test", null)).Value!;
        Assert.Equal(1, newer.Applied);
        var older = (await Svc(env).ImportAsync(Importer, Batch("BATCH-3", t1.AddDays(-1), Member(plan: "DEMO-OLD")), "test", null)).Value!;
        Assert.Equal("older_than_stored", older.Records[0].ErrorCode);
        var same = (await Svc(env).ImportAsync(Importer, Batch("BATCH-4", t1.AddDays(1), Member(plan: "DEMO-OTHER")), "test", null)).Value!;
        Assert.Equal("same_time_different_data", same.Records[0].ErrorCode);
        Assert.Equal("DEMO-PLUS", (await Svc(env).GetCoverageAsync(Importer, PatientA, "DEMO-INS")).Value!.PlanCode);
    }

    [Fact]
    public async Task Invalid_records_are_rejected_individually_without_stopping_the_batch()
    {
        using var env = new KEnv();
        await Svc(env).LinkPatientAsync(Importer, new ExternalPatientIdentifier("DEMO-INS", "DEMO-INS-0001"), PatientA, "t", null);
        var bad = new[]
        {
            Member(),
            Member("x"),                                                                     // id too short
            Member("DEMO INS 3"),                                                            // space
            new InsuranceMember("bad insurer!", "DEMO-INS-0004", Member().Coverage),          // insurer format
            new InsuranceMember("DEMO-INS", "DEMO-INS-0005", new InsuranceCoverage("p l", CoverageStatus.Active, new DateOnly(2026, 1, 1), null)),
            new InsuranceMember("DEMO-INS", "DEMO-INS-0006", new InsuranceCoverage("DEMO-BASIC", CoverageStatus.Active, new DateOnly(2026, 6, 1), new DateOnly(2026, 1, 1))), // dates reversed
            new InsuranceMember("DEMO-INS", "DEMO-INS-0007", new InsuranceCoverage("DEMO-BASIC", (CoverageStatus)99, new DateOnly(2026, 1, 1), null)),
            new InsuranceMember("DEMO-INS", "DEMO-INS-0008", new InsuranceCoverage("DEMO-BASIC", CoverageStatus.Active, new DateOnly(1900, 1, 1), null)),
        };
        var r = (await Svc(env).ImportAsync(Importer, Batch(members: bad), "test", null)).Value!;
        Assert.Equal(1, r.Applied);
        Assert.Equal(7, r.Rejected);
        Assert.Equal(["member_id.invalid", "member_id.invalid", "insurer.invalid", "plan.invalid", "dates.order", "status.invalid", "dates.range"], r.Records.Skip(1).Select(x => x.ErrorCode));
    }

    [Fact]
    public async Task Duplicates_inside_a_batch_are_deduplicated_or_flagged()
    {
        using var env = new KEnv();
        await Svc(env).LinkPatientAsync(Importer, new ExternalPatientIdentifier("DEMO-INS", "DEMO-INS-0001"), PatientA, "t", null);
        var r = (await Svc(env).ImportAsync(Importer, Batch(members: [Member(), Member(), Member(plan: "DEMO-PLUS")]), "test", null)).Value!;
        Assert.Equal(1, r.Applied);
        Assert.Equal(1, r.Unchanged);
        Assert.Equal(1, r.Conflicts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5001)]
    public async Task Empty_or_oversized_batches_are_invalid(int count)
    {
        using var env = new KEnv();
        var members = Enumerable.Range(0, count).Select(i => Member($"DEMO-INS-{i:D5}")).ToArray();
        var r = await Svc(env).ImportAsync(Importer, new InsuranceBatch("mock-insurance", "B-LARGE", DateTimeOffset.UtcNow, members), "test", null);
        Assert.Equal(InsuranceError.InvalidBatch, r.Error);
    }

    [Fact]
    public async Task Identifier_mapping_prevents_duplicate_patients_and_double_links()
    {
        using var env = new KEnv();
        var ext = new ExternalPatientIdentifier("DEMO-INS", "DEMO-INS-0001");
        Assert.True((await Svc(env).LinkPatientAsync(Importer, ext, PatientA, "t", null)).Succeeded);
        Assert.True((await Svc(env).LinkPatientAsync(Importer, ext, PatientA, "t", null)).Succeeded); // idempotent
        Assert.Equal("identifier.linked_to_other_patient", (await Svc(env).LinkPatientAsync(Importer, ext, PatientB, "t", null)).Detail);
        Assert.Equal("patient.already_linked_in_system", (await Svc(env).LinkPatientAsync(Importer, new ExternalPatientIdentifier("DEMO-INS", "DEMO-INS-0002"), PatientA, "t", null)).Detail);
        Assert.True((await Svc(env).LinkPatientAsync(Importer, new ExternalPatientIdentifier("OTHER-INS", "OTH-0001"), PatientA, "t", null)).Succeeded); // other system is fine
        Assert.Equal(InsuranceError.InvalidBatch, (await Svc(env).LinkPatientAsync(Importer, new ExternalPatientIdentifier("DEMO-INS", "??"), PatientA, "t", null)).Error);
        Assert.Equal(InsuranceError.InvalidBatch, (await Svc(env).LinkPatientAsync(Importer, ext with { Value = "DEMO-INS-0009" }, Guid.Empty, "t", null)).Error);
    }

    [Fact]
    public async Task Mock_provider_import_works_and_a_provider_outage_is_recorded_as_a_failed_import()
    {
        using var env = new KEnv(settings: new() { ["Integrations:Insurance:EnableMock"] = "true" });
        var ok = await Svc(env).ImportFromProviderAsync(Importer, MockInsuranceProvider.ProviderId, null, "test", null);
        Assert.True(ok.Succeeded);
        Assert.Equal(3, ok.Value!.Total);
        Assert.Equal(3, ok.Value.Unmapped);

        var failing = new InsuranceIntegrationService(new InsuranceStore(), [new MockInsuranceProvider(new MockInsuranceOptions { SimulateFailure = true })], env.Get<IAuditWriter>(), env.Clock);
        var r = await failing.ImportFromProviderAsync(Importer, MockInsuranceProvider.ProviderId, null, "test", null);
        Assert.Equal(InsuranceError.ProviderUnavailable, r.Error);
        var list = (await failing.ListImportsAsync(Importer)).Value!;
        Assert.Equal(ImportStatus.Failed, Assert.Single(list).Status);
        Assert.Equal(InsuranceError.NotFound, (await failing.ImportFromProviderAsync(Importer, "no-such-provider", null, "t", null)).Error);
    }

    [Fact]
    public async Task Audit_records_counts_but_no_member_identifiers()
    {
        using var env = new KEnv();
        await Svc(env).ImportAsync(Importer, Batch(), "test", "corr-12345678");
        var e = Assert.Single(await env.Audit.QueryAsync(new AuditQuery(Action: AuditActions.InsuranceImport)));
        var dump = string.Join(' ', e.Metadata.Values) + e.ResourceId + e.ReasonCode;
        Assert.DoesNotContain("DEMO-INS-0001", dump, StringComparison.Ordinal);
        Assert.Equal("1", e.Metadata["total"]);
    }

    [Fact]
    public async Task Coverage_reads_need_their_own_permission_and_retry_of_an_unknown_import_is_not_found()
    {
        using var env = new KEnv();
        var importOnly = User(Permissions.InsuranceImport);
        Assert.Equal(InsuranceError.Forbidden, (await Svc(env).GetCoverageAsync(importOnly, PatientA, "DEMO-INS")).Error);
        Assert.Equal(InsuranceError.NotFound, (await Svc(env).RetryAsync(Importer, Guid.NewGuid(), "t", null)).Error);
    }
}
