using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MedSmarter.Api.Adr;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.ADR.Contracts;
using MedSmarter.Modules.ADR.Persistence;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Consent;
using MedSmarter.Modules.Consent.Contracts;
using MedSmarter.Modules.Identity;
using MedSmarter.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Patients.Tests;

/// <summary>Switches shared between a test and the decorators it puts in front of the real services (relationship source, consent evaluator and store, clock, audit writer, authorizer).</summary>
internal sealed class Faults
{
    public bool FailRelationships { get; set; }
    public bool FailConsent { get; set; }
    public int ConsentCountDelta { get; set; }
    public int AuthorizerCountDelta { get; set; }
    public bool CancelRelationships { get; set; }
    public CancellationTokenSource? CancelOnConsent { get; set; }
    public CancellationTokenSource? CancelOnAudit { get; set; }
    /// <summary>The audit write of this action fails once <see cref="AuditOkBeforeFailure"/> of them have been written.</summary>
    public string? FailAuditAction { get; set; }
    public int AuditOkBeforeFailure { get; set; }
    public int AuditSeen { get; set; }
    public bool Trace { get; set; }
    public List<string> Order { get; } = [];
}

internal static class FaultWiring
{
    /// <summary>Puts a wrapper in front of the registered service of type T, keeping its lifetime and its own construction.</summary>
    public static void Decorate<T>(IServiceCollection services, Func<T, IServiceProvider, T> wrap) where T : class
    {
        var original = services.Last(d => d.ServiceType == typeof(T));
        services.Remove(original);
        services.Add(ServiceDescriptor.Describe(typeof(T), sp =>
        {
            var inner = original.ImplementationInstance is not null ? (T)original.ImplementationInstance
                : original.ImplementationFactory is not null ? (T)original.ImplementationFactory(sp)
                : (T)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!);
            return wrap(inner, sp);
        }, original.Lifetime));
    }

    public static void Wire(IServiceCollection s, Faults f, bool authorizer = false)
    {
        Decorate<ICareRelationshipSource>(s, (x, _) => new FaultyCare(x, f));
        Decorate<IConsentEvaluator>(s, (x, _) => new FaultyConsent(x, f));
        Decorate<IConsentStore>(s, (x, _) => new TracingStore(x, f));
        Decorate<IClock>(s, (x, _) => new TracingClock(x, f));
        Decorate<IAuditWriter>(s, (x, _) => new FaultyAudit(x, f));
        if (authorizer) { Decorate<IAccessAuthorizer>(s, (x, _) => new FaultyAuthorizer(x, f)); }
    }
}

internal sealed class FaultyCare(ICareRelationshipSource inner, Faults f) : ICareRelationshipSource
{
    public Task<IReadOnlyList<ActiveCareLink>> ActiveForPatientAsync(Guid patientUserId, CancellationToken ct = default) => inner.ActiveForPatientAsync(patientUserId, ct);

    public Task<IReadOnlyList<ActiveCareLink>> ActiveForProviderAsync(Guid providerUserId, IReadOnlyCollection<Guid> organizationIds, CancellationToken ct = default) => inner.ActiveForProviderAsync(providerUserId, organizationIds, ct);

    public async Task<IReadOnlyList<ActiveCareLink>> ActiveForPatientsAsync(IReadOnlyCollection<Guid> patientUserIds, CancellationToken ct = default)
    {
        if (f.CancelRelationships) { throw new OperationCanceledException(ct); }
        ct.ThrowIfCancellationRequested();
        if (f.FailRelationships) { throw new InvalidOperationException("synthetic relationship read failure"); }
        return await inner.ActiveForPatientsAsync(patientUserIds, ct);
    }
}

internal sealed class FaultyConsent(IConsentEvaluator inner, Faults f) : IConsentEvaluator
{
    public Task<ConsentDecision> EvaluateAsync(ConsentCheck check, CancellationToken ct = default) => inner.EvaluateAsync(check, ct);

    public async Task<IReadOnlyList<ConsentDecision>> EvaluateManyAsync(IReadOnlyList<ConsentCheck> checks, CancellationToken ct = default)
    {
        if (f.CancelOnConsent is { } cts) { await cts.CancelAsync(); }
        ct.ThrowIfCancellationRequested();
        if (f.FailConsent) { throw new InvalidOperationException("synthetic consent read failure"); }
        var result = await inner.EvaluateManyAsync(checks, ct);
        return f.ConsentCountDelta switch
        {
            < 0 => [.. result.Take(Math.Max(0, result.Count + f.ConsentCountDelta))],
            > 0 => [.. result, .. Enumerable.Repeat(new ConsentDecision(true, null, "allowed"), f.ConsentCountDelta)], // extra "allowed" answers must never be used
            _ => result,
        };
    }
}

internal sealed class TracingStore(IConsentStore inner, Faults f) : IConsentStore
{
    private void Note() { if (f.Trace) { f.Order.Add("read"); } }
    public Task AddAsync(ConsentRecord record, ConsentEventRecord granted, CancellationToken ct) => inner.AddAsync(record, granted, ct);
    public Task<bool> ExistsAsync(Guid id, CancellationToken ct) => inner.ExistsAsync(id, ct);
    public Task<ConsentRecord?> FindAsync(Guid id, CancellationToken ct) => inner.FindAsync(id, ct);
    public Task<IReadOnlyList<ConsentRecord>> ForSubjectAsync(Guid subjectUserId, CancellationToken ct) { Note(); return inner.ForSubjectAsync(subjectUserId, ct); }
    public Task<IReadOnlyList<ConsentRecord>> ForSubjectsAsync(IReadOnlyCollection<Guid> subjectUserIds, CancellationToken ct) { Note(); return inner.ForSubjectsAsync(subjectUserIds, ct); }
    public Task<ConsentRecord?> RevokeAsync(Guid id, DateTimeOffset at, ConsentEventRecord revoked, CancellationToken ct) => inner.RevokeAsync(id, at, revoked, ct);
    public Task<IReadOnlyList<ConsentEventRecord>> EventsAsync(Guid subjectUserId, CancellationToken ct) => inner.EventsAsync(subjectUserId, ct);
}

internal sealed class TracingClock(IClock inner, Faults f) : IClock
{
    public DateTimeOffset UtcNow { get { if (f.Trace) { f.Order.Add("clock"); } return inner.UtcNow; } }
}

internal sealed class FaultyAudit(IAuditWriter inner, Faults f) : IAuditWriter
{
    public async Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        if (f.CancelOnAudit is { } cts && auditEvent.Action == AuditActions.AccessDenied) { await cts.CancelAsync(); }
        cancellationToken.ThrowIfCancellationRequested();
        if (f.FailAuditAction is { } action && auditEvent.Action == action && f.AuditSeen++ >= f.AuditOkBeforeFailure) { throw new InvalidOperationException("synthetic audit failure"); }
        await inner.WriteAsync(auditEvent, cancellationToken);
    }
}

internal sealed class FaultyAuthorizer(IAccessAuthorizer inner, Faults f) : IAccessAuthorizer
{
    public Task<AccessDecision> AuthorizeAsync(CurrentUser actor, string permission, AccessResource resource, RequestContext context, CancellationToken ct = default) => inner.AuthorizeAsync(actor, permission, resource, context, ct);

    public async Task<IReadOnlyList<AccessDecision>> AuthorizeManyAsync(CurrentUser actor, string permission, IReadOnlyList<AccessResource> resources, RequestContext context, CancellationToken ct = default)
    {
        var result = await inner.AuthorizeManyAsync(actor, permission, resources, context, ct);
        return f.AuthorizerCountDelta switch
        {
            < 0 => [.. result.Take(Math.Max(0, result.Count + f.AuthorizerCountDelta))],
            > 0 => [.. result, .. Enumerable.Repeat(AccessDecision.Allow(AccessLayer.Consent), f.AuthorizerCountDelta)],
            _ => result,
        };
    }
}

/// <summary>
/// What the batch does when something goes wrong: a failure, a cancellation or an answer of the wrong size is never turned into a decision, nothing is returned, and the audit trail contains
/// exactly the denial events that were actually written (events of a batch that fails before its decisions are complete are NOT written; a failing audit write stops the rest).
/// </summary>
public class AuthorizationBatchFaultTests
{
    private static readonly RequestContext Rc = new("test", "corr-fault-1234");

    private static PEnv Env(Faults f) => new(seedPatients: true, configureServices: s => FaultWiring.Wire(s, f));

    private static List<AccessResource> Patients(PEnv env, params string[] accounts) => [.. accounts.Select(a => AccessResource.Patient(env.UserId(a), "adr"))];

    private static readonly string[] Several = ["demo-patient", "demo-patient-2", "subject-pt-2", "subject-pt-3", "subject-pt-4", "subject-pt-5", "subject-pt-6", "subject-pt-7"];

    private static async Task<int> Denied(PEnv env, Guid actor) => (await env.Audit.QueryAsync(new AuditQuery(ActorUserId: actor, Action: AuditActions.AccessDenied, Take: 500))).Count;

    [Fact]
    public async Task A_failed_relationship_read_throws_decides_nothing_and_writes_no_audit_events()
    {
        var f = new Faults { FailRelationships = true };
        using var env = Env(f);
        var actor = env.As("demo-physician-b");
        var before = await Denied(env, actor.UserId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Authz.AuthorizeManyAsync(actor, Permissions.AdrReview, Patients(env, Several), Rc));
        Assert.Equal(before, await Denied(env, actor.UserId)); // actual behaviour: a batch that fails before its decisions are complete records no denial events

        f.FailRelationships = false;
        var again = await env.Authz.AuthorizeManyAsync(actor, Permissions.AdrReview, Patients(env, Several), Rc); // nothing sticky: the next call decides normally
        Assert.Equal(Several.Length, again.Count);
        Assert.Equal(before + Several.Length, await Denied(env, actor.UserId));
    }

    [Fact]
    public async Task A_failed_consent_read_throws_and_no_resource_is_allowed_or_audited()
    {
        var f = new Faults { FailConsent = true };
        using var env = Env(f);
        var actor = env.As("demo-physician"); // related to eight patients: the consent step is reached
        var before = await Denied(env, actor.UserId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Authz.AuthorizeManyAsync(actor, Permissions.AdrReview, Patients(env, Several), Rc));
        Assert.Equal(before, await Denied(env, actor.UserId));
    }

    [Fact]
    public async Task Cancellation_before_the_relationship_read_or_before_the_consent_read_throws_and_audits_nothing()
    {
        var actor = new Faults();
        using var env = Env(actor);
        var doctor = env.As("demo-physician");
        var before = await Denied(env, doctor.UserId);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        actor.CancelRelationships = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => env.Authz.AuthorizeManyAsync(doctor, Permissions.AdrReview, Patients(env, Several), Rc, cancelled.Token));
        actor.CancelRelationships = false;

        using var duringConsent = new CancellationTokenSource();
        actor.CancelOnConsent = duringConsent; // the token is cancelled while the consents are being read
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => env.Authz.AuthorizeManyAsync(doctor, Permissions.AdrReview, Patients(env, Several), Rc, duringConsent.Token));
        Assert.Equal(before, await Denied(env, doctor.UserId));
    }

    [Fact]
    public async Task A_result_of_the_wrong_size_from_the_consent_evaluator_is_an_error_never_a_decision()
    {
        foreach (var delta in new[] { -1, 1 })
        {
            var f = new Faults { ConsentCountDelta = delta };
            using var env = Env(f);
            var actor = env.As("demo-physician");
            var before = await Denied(env, actor.UserId);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => env.Authz.AuthorizeManyAsync(actor, Permissions.AdrReview, Patients(env, Several), Rc));
            Assert.Contains("decisions for", ex.Message);
            Assert.Equal(before, await Denied(env, actor.UserId));
        }
    }

    [Fact]
    public async Task A_failing_audit_write_stops_the_batch_with_an_error_and_only_the_events_actually_written_exist()
    {
        var f = new Faults { FailAuditAction = AuditActions.AccessDenied, AuditOkBeforeFailure = 2 };
        using var env = Env(f);
        var actor = env.As("demo-physician-b"); // related to nobody: every resource is denied (no_care_relationship), so one event per resource is due
        var before = await Denied(env, actor.UserId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Authz.AuthorizeManyAsync(actor, Permissions.AdrReview, Patients(env, Several), Rc));
        var events = (await env.Audit.QueryAsync(new AuditQuery(ActorUserId: actor.UserId, Action: AuditActions.AccessDenied, Take: 500))).OrderBy(e => e.Sequence).ToList();
        Assert.Equal(before + 2, events.Count);                                              // events 1 and 2 were written, in order ...
        Assert.Equal(env.UserId(Several[0]), events[^2].SubjectUserId);
        Assert.Equal(env.UserId(Several[1]), events[^1].SubjectUserId);
        Assert.DoesNotContain(events, e => e.SubjectUserId == env.UserId(Several[2]));       // ... the failing one and the rest were not: nothing claims they were recorded
    }

    [Fact]
    public async Task Cancellation_during_the_audit_writes_throws_before_that_event_is_written()
    {
        var f = new Faults();
        using var env = Env(f);
        var actor = env.As("demo-physician-b");
        var before = await Denied(env, actor.UserId);
        using var cts = new CancellationTokenSource();
        f.CancelOnAudit = cts;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => env.Authz.AuthorizeManyAsync(actor, Permissions.AdrReview, Patients(env, Several), Rc, cts.Token));
        Assert.Equal(before, await Denied(env, actor.UserId)); // cancelled at the first denial: that and the later events were not written
    }

    [Fact]
    public async Task The_single_and_the_batch_consent_paths_both_read_the_clock_before_they_read_the_consents()
    {
        var f = new Faults();
        using var env = Env(f);
        var evaluator = env.Get<IConsentEvaluator>();
        var physician = env.As("demo-physician");
        var check = new ConsentCheck(env.UserId("demo-patient"), physician.UserId, physician.OrganizationIds.ToArray(), DataScopes.Adr, [ConsentPurposes.Treatment]);

        f.Trace = true;
        await evaluator.EvaluateAsync(check);
        var single = f.Order.ToList();
        f.Order.Clear();
        await evaluator.EvaluateManyAsync([check]);
        var batch = f.Order.ToList();
        f.Trace = false;

        Assert.Equal(["clock", "read"], single);
        Assert.Equal(["clock", "read"], batch);
    }
}

/// <summary>The same failures through the queue endpoint: the response is a server error, none of the seeded records is in it, and the queue-access event is not written.</summary>
public class AuthorizationBatchFaultApiTests(PatientsApiFactory factory) : IClassFixture<PatientsApiFactory>
{
    private const string Marker = "FAULT-MARKER-RECORD";

    private sealed record Host(WebApplicationFactory<Program> Factory, Faults Faults);

    private Host Start(bool authorizer = false)
    {
        var f = new Faults();
        var derived = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => FaultWiring.Wire(s, f, authorizer)));
        return new Host(derived, f);
    }

    private static async Task<HttpClient> Client(WebApplicationFactory<Program> f, string account)
    {
        var client = f.CreateClient();
        var res = await client.PostAsJsonAsync("/auth/login", new { credentials = new { accountId = account }, device = new { platform = "web", deviceName = "fault test" } });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var auth = await res.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("tokens").GetProperty("accessToken").GetString());
        return client;
    }

    private async Task<List<Guid>> Seed(Host h, string account)
    {
        var subject = h.Factory.Services.GetRequiredService<AccessCatalog>().File.DemoAccounts.Single(a => a.Id == account).UserId;
        var db = await h.Factory.Services.GetRequiredService<IDbContextFactory<AdrDbContext>>().CreateDbContextAsync();
        await using (db)
        {
            var ids = new List<Guid>();
            for (var i = 0; i < 3; i++)
            {
                var at = DateTimeOffset.UtcNow.AddMinutes(-i - 1);
                var id = Guid.NewGuid();
                ids.Add(id);
                db.Events.Add(new AdverseEventRow { Id = id, SubjectId = subject, ReporterRole = ReporterRole.Patient, ReporterUserId = subject, Origin = ReportOrigin.PatientReported, ReportedAt = at, EventDescription = Marker + i, Status = ReviewStatus.Submitted, LastActivityAt = at });
            }

            await db.SaveChangesAsync();
            return ids;
        }
    }

    private static async Task Cleanup(Host h, List<Guid> ids)
    {
        await using var db = await h.Factory.Services.GetRequiredService<IDbContextFactory<AdrDbContext>>().CreateDbContextAsync();
        db.Events.RemoveRange(await db.Events.Where(x => ids.Contains(x.Id)).ToListAsync());
        await db.SaveChangesAsync();
    }

    private static async Task<int> Count(Host h, Guid actor, string action) => (await h.Factory.Services.GetRequiredService<IAuditReader>().QueryAsync(new AuditQuery(ActorUserId: actor, Action: action, Take: 500))).Count;

    private async Task AssertFailsClosed(Host h, List<Guid> ids, Action arm, Action disarm, Func<Task<int>>? watch = null)
    {
        var client = await Client(h.Factory, "demo-physician");
        var actor = h.Factory.Services.GetRequiredService<AccessCatalog>().File.DemoAccounts.Single(a => a.Id == "demo-physician").UserId;

        var watched0 = watch is null ? 0 : await watch();
        var ok = await client.GetAsync("/adverse-events/queue");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var watched1 = watch is null ? 0 : await watch();
        if (watch is not null) { Assert.True(watched1 > watched0); } // the watched events are written by a normal request ...
        Assert.Contains(Marker, await ok.Content.ReadAsStringAsync()); // without the fault the records are returned: the checks below are not vacuous

        var accessBefore = await Count(h, actor, AuditActions.AdrAccessed);
        arm();
        var res = await client.GetAsync("/adverse-events/queue");
        var body = await res.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
        Assert.DoesNotContain(Marker, body);
        foreach (var id in ids) { Assert.DoesNotContain(id.ToString(), body, StringComparison.OrdinalIgnoreCase); }
        Assert.False(res.Headers.Contains(AdrEndpoints.QueueTotalCountHeader) || res.Headers.Contains(AdrEndpoints.QueueNextCursorHeader) || res.Headers.Contains(AdrEndpoints.QueueTruncatedHeader));
        Assert.Equal(accessBefore, await Count(h, actor, AuditActions.AdrAccessed)); // no "queue accessed" event for a request that returned nothing
        if (watch is not null) { Assert.Equal(watched1, await watch()); } // ... and none of them exists for the request that failed
        disarm();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/adverse-events/queue")).StatusCode); // and nothing stays broken
    }

    [Fact]
    public async Task A_relationship_read_failure_gives_a_server_error_and_no_records()
    {
        var h = Start();
        var ids = await Seed(h, "demo-patient");
        try { await AssertFailsClosed(h, ids, () => h.Faults.FailRelationships = true, () => h.Faults.FailRelationships = false); }
        finally { await Cleanup(h, ids); }
    }

    [Fact]
    public async Task A_consent_read_failure_gives_a_server_error_and_no_records()
    {
        var h = Start();
        var ids = await Seed(h, "demo-patient");
        try { await AssertFailsClosed(h, ids, () => h.Faults.FailConsent = true, () => h.Faults.FailConsent = false); }
        finally { await Cleanup(h, ids); }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task A_wrong_number_of_decisions_from_the_authorizer_gives_a_server_error_and_no_records(int delta)
    {
        var h = Start(authorizer: true);
        var ids = await Seed(h, "demo-patient");
        try { await AssertFailsClosed(h, ids, () => h.Faults.AuthorizerCountDelta = delta, () => h.Faults.AuthorizerCountDelta = 0); }
        finally { await Cleanup(h, ids); }
    }

    [Fact]
    public async Task A_wrong_number_of_answers_from_the_consent_evaluator_gives_a_server_error_and_no_records()
    {
        var h = Start();
        var ids = await Seed(h, "demo-patient");
        try { await AssertFailsClosed(h, ids, () => h.Faults.ConsentCountDelta = 1, () => h.Faults.ConsentCountDelta = 0); }
        finally { await Cleanup(h, ids); }
    }

    [Fact]
    public async Task A_failing_denial_audit_write_gives_a_server_error_no_records_and_that_denial_is_not_recorded()
    {
        var h = Start();
        var ids = await Seed(h, "demo-patient");
        try
        {
            // physician A is related to patients who gave no consent, so a normal request writes denial events; with the audit write failing, the request fails and none of them is written
            var actor = h.Factory.Services.GetRequiredService<AccessCatalog>().File.DemoAccounts.Single(a => a.Id == "demo-physician").UserId;
            await AssertFailsClosed(h, ids,
                () => { h.Faults.FailAuditAction = AuditActions.AccessDenied; h.Faults.AuditOkBeforeFailure = 0; h.Faults.AuditSeen = 0; },
                () => h.Faults.FailAuditAction = null,
                watch: () => Count(h, actor, AuditActions.AccessDenied));
        }
        finally { await Cleanup(h, ids); }
    }
}
