using System.Net;
using System.Text;
using System.Text.Json;
using MedSmarter.Api.Adr;
using MedSmarter.Modules.ADR.Contracts;
using MedSmarter.Modules.ADR.Persistence;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Consent.Contracts;
using MedSmarter.Modules.Identity;
using MedSmarter.Modules.Identity.Contracts;
using MedSmarter.Modules.Patients.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Patients.Tests;

/// <summary>
/// Phase 18: the reviewer's queue decides which patients the reviewer may read BEFORE it reads, counts or cuts anything, then pages the records of those patients only.
/// Every test builds its own data (direct rows with synthetic ids) and removes it again; expected results come from the database itself, never from a number written in the test.
/// </summary>
public class Phase18ApiTests(PatientsApiFactory factory) : IClassFixture<PatientsApiFactory>
{
    private static readonly ReviewStatus[] Open = [ReviewStatus.Submitted, ReviewStatus.InReview, ReviewStatus.InformationRequested];
    private const int OpenMask = 7; // bits of Submitted, InReview, InformationRequested (see QueueCursor.MaskOf)

    private T Svc<T>() where T : notnull => factory.Services.GetRequiredService<T>();
    private AccessCatalog Catalog => Svc<AccessCatalog>();
    private Guid Id(string account) => Catalog.File.DemoAccounts.Single(a => a.Id == account).UserId;
    private Task<ClinicalApiTests.Session> As(string account) => ClinicalApiTests.Login(factory, account);

    private CurrentUser User(string account)
    {
        var a = Catalog.File.DemoAccounts.Single(x => x.Id == account);
        var roles = a.Roles.Select(r => r.Role).ToHashSet();
        return new CurrentUser(a.UserId, Guid.NewGuid(), a.DisplayName["en"], roles, Catalog.PermissionsOf(roles), a.Organizations.ToHashSet());
    }

    private async Task<IDbContextFactory<AdrDbContext>> Db() => await Task.FromResult(Svc<IDbContextFactory<AdrDbContext>>());

    /// <summary>Rows inserted directly: newest first, <paramref name="tie"/> consecutive rows share one instant.</summary>
    private async Task<List<Guid>> Seed(Guid subject, int count, DateTimeOffset newest, int tie = 1, ReviewStatus status = ReviewStatus.Submitted)
    {
        var ids = new List<Guid>();
        await using var db = await (await Db()).CreateDbContextAsync();
        for (var i = 0; i < count; i++)
        {
            var at = newest.AddSeconds(-(i / tie));
            var id = Guid.NewGuid();
            ids.Add(id);
            db.Events.Add(new AdverseEventRow
            {
                Id = id, SubjectId = subject, ReporterRole = ReporterRole.Patient, ReporterUserId = subject, Origin = ReportOrigin.PatientReported,
                ReportedAt = at, EventDescription = "Synthetic record " + i, Status = status, LastActivityAt = at,
            });
        }

        await db.SaveChangesAsync();
        return ids;
    }

    private async Task Remove(IEnumerable<Guid> ids)
    {
        var set = ids.ToArray();
        await using var db = await (await Db()).CreateDbContextAsync();
        db.Events.RemoveRange(await db.Events.Where(x => set.Contains(x.Id)).ToListAsync());
        await db.SaveChangesAsync();
    }

    private static DateTimeOffset Base() => new(DateTimeOffset.UtcNow.AddHours(-1).UtcTicks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, TimeSpan.Zero);

    /// <summary>Physician B related to patient 2 (active relationship + consent for the adr scope), undone on dispose. Nobody else is related to patient 2 here.</summary>
    private sealed class Grant : IAsyncDisposable
    {
        private readonly Phase18ApiTests _t;
        private readonly Guid _physician;
        private readonly Guid _relationship;
        private readonly Guid _consent;
        private bool _ended;
        private bool _revoked;

        public Grant(Phase18ApiTests t, Guid patient, Guid physician, Guid relationship, Guid consent)
        {
            _t = t;
            Patient = patient;
            _physician = physician;
            _relationship = relationship;
            _consent = consent;
        }

        public Guid Patient { get; }

        public async Task EndRelationship()
        {
            if (!_ended)
            {
                _ended = true;
                Assert.True((await _t.Svc<ICareRelationshipService>().EndAsync(_physician, _relationship, null, "t", null)).Succeeded);
            }
        }

        public async Task RevokeConsent()
        {
            if (!_revoked)
            {
                _revoked = true;
                Assert.True((await _t.Svc<IConsentService>().RevokeAsync(Patient, _consent, "t", null)).Succeeded);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await EndRelationship();
            await RevokeConsent();
        }
    }

    private async Task<Grant> GrantB()
    {
        var patient = Id("demo-patient-2");
        var physician = Id("demo-physician-b");
        Assert.True((await Svc<IPatientService>().EnsureOwnAsync(patient, "test", null)).Succeeded);
        var rel = (await Svc<ICareRelationshipService>().RequestAsync(patient, new RequestCareRelationshipCommand(physician, "Treating"), "t", null)).Value!;
        Assert.True((await Svc<ICareRelationshipService>().AcceptAsync(physician, rel.Id, "t", null)).Succeeded);
        var consent = await Svc<IConsentService>().GrantAsync(patient, new GrantConsentCommand(patient, physician, null, ConsentPurposes.Treatment, [DataScopes.Adr], DateTimeOffset.UtcNow.AddDays(30), "v1"), "t", null);
        Assert.True(consent.Succeeded, consent.Error.ToString());
        return new Grant(this, patient, physician, rel.Id, consent.Consent!.Id);
    }

    private static Task<HttpResponseMessage> Get(ClinicalApiTests.Session s, string query = "") => s.Client.GetAsync("/adverse-events/queue" + query);

    private static async Task<List<Guid>> Ids(HttpResponseMessage res)
    {
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return [.. (await ClinicalApiTests.Json(res)).EnumerateArray().Select(x => x.GetProperty("id").GetGuid())];
    }

    private static string? Header(HttpResponseMessage res, string name) => res.Headers.TryGetValues(name, out var v) ? Assert.Single(v) : null;

    private sealed record PageInfo(List<Guid> Ids, string? Truncated, string? Cursor, string? Total);

    private static async Task<PageInfo> Read(HttpResponseMessage res) =>
        new(await Ids(res), Header(res, AdrEndpoints.QueueTruncatedHeader), Header(res, AdrEndpoints.QueueNextCursorHeader), Header(res, AdrEndpoints.QueueTotalCountHeader));

    private static async Task<List<PageInfo>> Walk(ClinicalApiTests.Session s, int limit, string status = "")
    {
        var pages = new List<PageInfo>();
        string? cursor = null;
        do
        {
            var q = $"?limit={limit}" + (status.Length > 0 ? "&status=" + status : "") + (cursor is null ? "" : "&cursor=" + cursor);
            var page = await Read(await Get(s, q));
            pages.Add(page);
            cursor = page.Cursor;
            Assert.True(pages.Count < 100, "runaway pagination");
        }
        while (cursor is not null);
        return pages;
    }

    private async Task<List<Guid>> Oracle(Guid subject, params ReviewStatus[] statuses)
    {
        await using var db = await (await Db()).CreateDbContextAsync();
        return await db.Events.AsNoTracking().Where(x => x.SubjectId == subject && statuses.Contains(x.Status)).OrderByDescending(x => x.LastActivityAt).ThenByDescending(x => x.Id).Select(x => x.Id).ToListAsync();
    }

    // ---------- the former 200 boundary ----------

    [Fact]
    public async Task An_authorized_record_older_than_250_newer_records_of_other_patients_is_on_the_first_page()
    {
        await using var grant = await GrantB();
        var mine = await Seed(grant.Patient, 1, Base().AddHours(-5));
        var sara = Id("demo-patient"); // exists, not related to physician B
        var others = new List<Guid>();
        others.AddRange(await Seed(sara, 130, Base()));
        others.AddRange(await Seed(Guid.NewGuid(), 130, Base())); // a patient that does not even exist
        try
        {
            var res = await Get(await As("demo-physician-b"));
            var page = await Read(res);
            Assert.Equal(mine, page.Ids.Intersect(mine).ToList());
            Assert.Empty(page.Ids.Intersect(others));
            Assert.Equal("false", page.Truncated);
            Assert.Null(page.Cursor);
            Assert.Equal((await Oracle(grant.Patient, Open)).Count.ToString(), page.Total);
        }
        finally
        {
            await Remove(mine.Concat(others));
        }
    }

    [Fact]
    public async Task Pagination_visits_every_authorized_record_exactly_once_in_the_database_order_even_with_equal_timestamps()
    {
        await using var grant = await GrantB();
        var mine = await Seed(grant.Patient, 237, Base(), tie: 5); // groups of 5 records share one instant
        var other = await Seed(Id("demo-patient"), 40, Base().AddSeconds(30)); // newer, not readable by B
        try
        {
            var pages = await Walk(await As("demo-physician-b"), 100);
            Assert.Equal([100, 100, 37 + (await Oracle(grant.Patient, Open)).Count - 237], pages.Select(p => p.Ids.Count));
            var walked = pages.SelectMany(p => p.Ids).ToList();
            Assert.Equal(walked.Count, walked.Distinct().Count());
            Assert.Equal(await Oracle(grant.Patient, Open), walked); // same order as the database's own (LastActivityAt desc, Id desc)
            Assert.Equal(["true", "true", "false"], pages.Select(p => p.Truncated));
            Assert.Equal([true, true, false], pages.Select(p => p.Cursor is not null));
            Assert.Empty(walked.Intersect(other));
        }
        finally
        {
            await Remove(mine.Concat(other));
        }
    }

    [Fact]
    public async Task Total_is_sent_for_the_first_page_only_and_counts_only_readable_records()
    {
        await using var grant = await GrantB();
        var mine = await Seed(grant.Patient, 120, Base());
        var other = await Seed(Id("demo-patient"), 60, Base());
        try
        {
            var b = await As("demo-physician-b");
            var first = await Read(await Get(b, "?limit=50"));
            Assert.Equal((await Oracle(grant.Patient, Open)).Count.ToString(), first.Total);
            Assert.NotNull(first.Cursor);
            var second = await Read(await Get(b, "?limit=50&cursor=" + first.Cursor));
            Assert.Null(second.Total); // never on a page reached by cursor
            Assert.NotEqual((await Oracle(Id("demo-patient"), Open)).Count.ToString(), first.Total);
            // the status filter applies to the total as well
            Assert.Equal("0", Header(await Get(b, "?status=Closed"), AdrEndpoints.QueueTotalCountHeader));
        }
        finally
        {
            await Remove(mine.Concat(other));
        }
    }

    [Fact]
    public async Task Empty_exact_and_final_pages()
    {
        await using var grant = await GrantB();
        var b = await As("demo-physician-b");
        var empty = await Read(await Get(b, "?status=Closed"));
        Assert.Empty(empty.Ids);
        Assert.Equal("false", empty.Truncated);
        Assert.Null(empty.Cursor);
        Assert.Equal("0", empty.Total);

        var mine = await Seed(grant.Patient, 7, Base());
        try
        {
            var count = (await Oracle(grant.Patient, Open)).Count;
            Assert.InRange(count, 7, 100);
            var exact = await Read(await Get(b, $"?limit={count}")); // exactly as many as fit: nothing more exists
            Assert.Equal(count, exact.Ids.Count);
            Assert.Equal("false", exact.Truncated);
            Assert.Null(exact.Cursor);
            var one_short = await Read(await Get(b, $"?limit={count - 1}"));
            Assert.Equal("true", one_short.Truncated);
            Assert.NotNull(one_short.Cursor);
            var last = await Read(await Get(b, $"?limit={count - 1}&cursor={one_short.Cursor}"));
            Assert.Single(last.Ids);
            Assert.Equal("false", last.Truncated);
            Assert.Null(last.Cursor);
        }
        finally
        {
            await Remove(mine);
        }
    }

    [Fact]
    public async Task Status_filter_is_honoured_and_a_cursor_is_bound_to_it()
    {
        await using var grant = await GrantB();
        var open = await Seed(grant.Patient, 60, Base());
        var closed = await Seed(grant.Patient, 60, Base(), status: ReviewStatus.Closed);
        try
        {
            var b = await As("demo-physician-b");
            var onlyClosed = (await Walk(b, 50, "Closed")).SelectMany(p => p.Ids).ToList();
            Assert.Equal((await Oracle(grant.Patient, ReviewStatus.Closed)), onlyClosed);
            Assert.Empty(onlyClosed.Intersect(open));
            var first = await Read(await Get(b, "?limit=10"));
            Assert.Equal(HttpStatusCode.BadRequest, (await Get(b, "?limit=10&status=Closed&cursor=" + first.Cursor)).StatusCode); // issued for another status set
            Assert.Equal(HttpStatusCode.OK, (await Get(b, "?limit=10&status=closed,Submitted&cursor=" + (await Read(await Get(b, "?limit=10&status=Submitted,Closed"))).Cursor)).StatusCode); // same set, other order and case
        }
        finally
        {
            await Remove(open.Concat(closed));
        }
    }

    // ---------- scope: who is never shown ----------

    [Fact]
    public async Task No_page_header_or_total_carries_anything_of_a_patient_the_reviewer_may_not_read()
    {
        await using var grant = await GrantB();
        var mine = await Seed(grant.Patient, 10, Base());
        var sara = Id("demo-patient");
        var noConsent = Id("demo-patient-2"); // physician A is related to patient 2 but has no consent: covered by the parity test; here B has consent
        var hidden = new List<Guid>();
        hidden.AddRange(await Seed(sara, 120, Base().AddSeconds(100)));
        hidden.AddRange(await Seed(Guid.NewGuid(), 120, Base().AddSeconds(100)));
        try
        {
            var b = await As("demo-physician-b");
            var wire = new StringBuilder();
            var all = new List<Guid>();
            string? cursor = null;
            do
            {
                var res = await Get(b, "?limit=3" + (cursor is null ? "" : "&cursor=" + cursor));
                wire.Append(await res.Content.ReadAsStringAsync());
                foreach (var h in res.Headers) { wire.Append(h.Key).Append(':').Append(string.Join(',', h.Value)); }
                var p = await Read(res);
                all.AddRange(p.Ids);
                cursor = p.Cursor;
            }
            while (cursor is not null);
            var oracle = await Oracle(noConsent, Open);
            Assert.Equal(oracle, all);
            var text = wire.ToString();
            foreach (var id in hidden.Take(20).Append(sara)) { Assert.DoesNotContain(id.ToString(), text, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain(id.ToString("N"), text, StringComparison.OrdinalIgnoreCase); }
            Assert.Equal(oracle.Count.ToString(), Header(await Get(b), AdrEndpoints.QueueTotalCountHeader));
        }
        finally
        {
            await Remove(mine.Concat(hidden));
        }
    }

    [Fact]
    public async Task Consent_revoked_between_two_requests_leaves_no_stale_records_and_no_cursor_or_total()
    {
        await using var grant = await GrantB();
        var mine = await Seed(grant.Patient, 120, Base());
        try
        {
            var b = await As("demo-physician-b");
            var first = await Read(await Get(b, "?limit=50"));
            Assert.Equal(50, first.Ids.Count);
            await grant.RevokeConsent();
            var next = await Get(b, "?limit=50&cursor=" + first.Cursor);
            var page = await Read(next);
            Assert.Empty(page.Ids);
            Assert.Equal("false", page.Truncated);
            Assert.Null(page.Cursor);
            Assert.Null(page.Total);
            Assert.Empty((await Read(await Get(b))).Ids);
        }
        finally
        {
            await Remove(mine);
        }
    }

    [Fact]
    public async Task A_relationship_that_ends_between_two_requests_leaves_no_stale_records()
    {
        await using var grant = await GrantB();
        var mine = await Seed(grant.Patient, 120, Base());
        try
        {
            var b = await As("demo-physician-b");
            var first = await Read(await Get(b, "?limit=50"));
            await grant.EndRelationship();
            Assert.Empty((await Read(await Get(b, "?limit=50&cursor=" + first.Cursor))).Ids);
            Assert.Empty((await Read(await Get(b))).Ids);
        }
        finally
        {
            await Remove(mine);
        }
    }

    [Fact]
    public async Task A_patient_whose_account_is_disabled_keeps_their_open_reports_in_the_queue_while_the_care_list_still_leaves_them_out()
    {
        await using var grant = await GrantB();
        var mine = await Seed(grant.Patient, 5, Base());
        var store = Svc<InMemoryIdentityStore>();
        var user = (await store.FindUserAsync(grant.Patient, default))!;
        try
        {
            var b = await As("demo-physician-b");
            var before = (await Read(await Get(b))).Ids;
            Assert.Equal(await Oracle(grant.Patient, Open), before);

            user.Status = UserStatusValue.Disabled; // the ACCOUNT state; the patient record and the care relationship are untouched
            var users = Svc<IUserIdentityService>();
            Assert.DoesNotContain(await users.ListRelatedPatientsAsync(User("demo-physician-b")), p => p.UserId == grant.Patient); // care list: unchanged behaviour
            Assert.Contains(await users.ListRelatedPatientsAsync(User("demo-physician-b"), includeDisabled: true), p => p.UserId == grant.Patient);
            Assert.Equal(before, (await Read(await Get(b))).Ids); // the queue: nothing disappears
        }
        finally
        {
            user.Status = UserStatusValue.Active;
            await Remove(mine);
        }
    }

    [Fact]
    public async Task For_every_professional_account_the_new_queue_lists_exactly_what_the_authorizer_allows_per_patient_without_any_cap()
    {
        var accounts = Catalog.File.DemoAccounts.Where(a => a.Roles.Any(r => r.Role is "Physician" or "Pharmacist")).Select(a => a.Id).ToList();
        var subjects = Catalog.File.DemoAccounts.Where(a => a.Roles.Any(r => r.Role == "Patient")).Select(a => a.UserId).ToList();
        var seeded = new List<Guid>();
        foreach (var s in subjects) { seeded.AddRange(await Seed(s, 3, Base())); }
        seeded.AddRange(await Seed(Guid.NewGuid(), 250, Base().AddMinutes(5))); // fills the former cap
        await using var grant = await GrantB();
        try
        {
            foreach (var account in accounts)
            {
                var actor = User(account);
                List<Guid> expected = [];
                await using (var db = await (await Db()).CreateDbContextAsync())
                {
                    foreach (var row in await db.Events.AsNoTracking().Where(x => Open.Contains(x.Status)).ToListAsync())
                    {
                        // the previous rule, applied to every open record: not the reviewer's own, a professional role, and the per-patient authorization
                        if (row.SubjectId != actor.UserId && (actor.Roles.Contains("Physician") || actor.Roles.Contains("Pharmacist"))
                            && (await Svc<IAccessAuthorizer>().AuthorizeAsync(actor, Permissions.AdrReview, AccessResource.Patient(row.SubjectId, AdrEndpoints.Scope), new RequestContext("test", "corr-12345678"))).Allowed)
                        {
                            expected.Add(row.Id);
                        }
                    }
                }

                var got = (await Walk(await As(account), 100)).SelectMany(p => p.Ids).ToList();
                Assert.Equal(expected.Order().ToList(), got.Order().ToList());
                Assert.Equal(got.Count, got.Distinct().Count());
            }
        }
        finally
        {
            await Remove(seeded);
        }
    }

    // ---------- input validation, cursor protection ----------

    [Theory]
    [InlineData("?limit=0")]
    [InlineData("?limit=101")]
    [InlineData("?limit=-1")]
    [InlineData("?limit=abc")]
    [InlineData("?limit=")]
    [InlineData("?limit=1.5")]
    [InlineData("?limit=%2B5")]
    [InlineData("?limit=%205")]
    [InlineData("?limit=50&limit=60")]
    [InlineData("?limit=99999999999999999999")]
    public async Task An_invalid_limit_is_a_400_with_a_stable_code_and_no_data_headers(string query)
    {
        var res = await Get(await As("demo-physician"), query);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("limit.invalid", (await ClinicalApiTests.Json(res)).GetProperty("code").GetString());
        Assert.Equal("no-store", res.Headers.CacheControl?.ToString());
        Assert.Null(Header(res, AdrEndpoints.QueueTruncatedHeader));
        Assert.Null(Header(res, AdrEndpoints.QueueTotalCountHeader));
        Assert.Null(Header(res, AdrEndpoints.QueueNextCursorHeader));
    }

    [Theory]
    [InlineData("?limit=1")]
    [InlineData("?limit=100")]
    public async Task The_limits_themselves_are_accepted(string query)
    {
        Assert.Equal(HttpStatusCode.OK, (await Get(await As("demo-physician"), query)).StatusCode);
    }

    private static string Forge(long ticks, Guid id, int mask, Guid actor) =>
        "v1." + Convert.ToBase64String(Encoding.ASCII.GetBytes($"{ticks}|{id:N}|{mask}|{actor:N}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public async Task A_bad_tampered_oversized_or_foreign_cursor_is_refused_with_one_code_and_nothing_else_leaves()
    {
        await using var grant = await GrantB();
        var mine = await Seed(grant.Patient, 120, Base());
        var sara = await Seed(Id("demo-patient"), 120, Base()); // readable by physician A, not by B
        try
        {
            var a = await As("demo-physician");
            var b = await As("demo-physician-b");
            var cursorOfB = (await Read(await Get(b, "?limit=10"))).Cursor!;
            var cursorOfA = (await Read(await Get(a, "?limit=10"))).Cursor!;

            var bad = new List<string>
            {
                "garbage", "v1.", "v1.!!!!", "v2." + cursorOfB[3..], new string('A', 201), "v1." + new string('A', 300),
                "v1." + Convert.ToBase64String(Encoding.ASCII.GetBytes("1|2|3")).TrimEnd('='),
                Forge(long.MaxValue, Guid.NewGuid(), OpenMask, b.UserId),          // beyond DateTime.MaxValue
                cursorOfA,                                                          // issued to another user
                Forge(1, Guid.NewGuid(), 3, b.UserId),                              // other status set
                Forge(1, Guid.NewGuid(), OpenMask, a.UserId),                       // another user's id inside
                cursorOfB + "A", cursorOfB[..^2],
            };
            foreach (var c in bad)
            {
                var res = await Get(b, "?cursor=" + Uri.EscapeDataString(c));
                Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
                Assert.Equal("cursor.invalid", (await ClinicalApiTests.Json(res)).GetProperty("code").GetString());
                Assert.Null(Header(res, AdrEndpoints.QueueTruncatedHeader));
            }

            Assert.Equal(HttpStatusCode.BadRequest, (await Get(b, $"?cursor={cursorOfB}&cursor={cursorOfB}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Get(b, "?cursor=")).StatusCode);
        }
        finally
        {
            await Remove(mine.Concat(sara));
        }
    }

    [Fact]
    public async Task A_forged_but_well_formed_position_can_only_move_the_callers_own_position_inside_what_they_may_already_read()
    {
        await using var grant = await GrantB();
        var mine = await Seed(grant.Patient, 30, Base());
        var sara = await Seed(Id("demo-patient"), 30, Base());
        try
        {
            var b = await As("demo-physician-b");
            var readable = (await Oracle(grant.Patient, Open)).ToHashSet();
            foreach (var (ticks, id) in new[] { (0L, Guid.Empty), (DateTime.MaxValue.Ticks, Guid.Empty), (DateTime.MaxValue.Ticks, Guid.NewGuid()), (Base().UtcTicks, sara[0]), (Base().UtcTicks, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")) })
            {
                var page = await Read(await Get(b, "?limit=100&cursor=" + Forge(ticks, id, OpenMask, b.UserId)));
                Assert.All(page.Ids, x => Assert.Contains(x, readable)); // never anything outside the readable set, whatever position was claimed
                Assert.Null(page.Total);
            }

            // a forged position names no record that is ever looked up: an id of an unreadable record behaves exactly like a random id
            var withSara = await Read(await Get(b, "?limit=100&cursor=" + Forge(Base().UtcTicks, sara[0], OpenMask, b.UserId)));
            var withRandom = await Read(await Get(b, "?limit=100&cursor=" + Forge(Base().UtcTicks, Guid.NewGuid(), OpenMask, b.UserId)));
            Assert.Equal(withSara.Ids.Count > 0, withRandom.Ids.Count > 0);
        }
        finally
        {
            await Remove(mine.Concat(sara));
        }
    }

    [Fact]
    public async Task Replaying_a_cursor_gives_the_same_page_and_a_refused_caller_gets_no_data_headers()
    {
        await using var grant = await GrantB();
        var mine = await Seed(grant.Patient, 60, Base());
        try
        {
            var b = await As("demo-physician-b");
            var first = await Read(await Get(b, "?limit=20"));
            var one = await Read(await Get(b, "?limit=20&cursor=" + first.Cursor));
            var two = await Read(await Get(b, "?limit=20&cursor=" + first.Cursor));
            Assert.Equal(one.Ids, two.Ids);
            Assert.Equal(one.Cursor, two.Cursor);

            var patient = await As("demo-patient");
            var refused = await Get(patient, "?cursor=" + first.Cursor);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Null(Header(refused, AdrEndpoints.QueueTruncatedHeader));
            Assert.Null(Header(refused, AdrEndpoints.QueueNextCursorHeader));
            Assert.Null(Header(refused, AdrEndpoints.QueueTotalCountHeader));
        }
        finally
        {
            await Remove(mine);
        }
    }

    // ---------- changes while someone pages (documented behaviour) ----------

    [Fact]
    public async Task Changes_during_a_walk_follow_the_documented_rules_and_nothing_is_lost_for_good()
    {
        await using var grant = await GrantB();
        var t0 = Base();
        var s = await Seed(grant.Patient, 120, t0); // s[0] is the newest
        var added = new List<Guid>();
        try
        {
            var b = await As("demo-physician-b");
            var page1 = await Read(await Get(b, "?limit=50"));
            Assert.Equal(s.Take(50), page1.Ids.Where(s.Contains));

            // while the reviewer is on page 1: a new report arrives; a record already shown gets activity; one not yet reached gets activity; one is closed
            var now = DateTimeOffset.UtcNow;
            added = await Seed(grant.Patient, 1, now);
            await using (var db = await (await Db()).CreateDbContextAsync())
            {
                (await db.Events.SingleAsync(x => x.Id == s[10])).LastActivityAt = now;
                (await db.Events.SingleAsync(x => x.Id == s[100])).LastActivityAt = now;
                (await db.Events.SingleAsync(x => x.Id == s[60])).Status = ReviewStatus.Closed;
                await db.SaveChangesAsync();
            }

            var tail = new List<Guid>();
            string? cursor = page1.Cursor;
            while (cursor is not null)
            {
                var p = await Read(await Get(b, "?limit=50&cursor=" + cursor));
                tail.AddRange(p.Ids);
                cursor = p.Cursor;
            }

            var seenInWalk = page1.Ids.Concat(tail).ToList();
            Assert.Equal(seenInWalk.Count, seenInWalk.Distinct().Count());                       // no duplicates, even for the record that moved forward
            Assert.DoesNotContain(added[0], seenInWalk);                                          // a new report is not part of this walk
            Assert.Contains(s[10], page1.Ids);                                                    // shown once, where it was
            Assert.DoesNotContain(s[10], tail);
            Assert.DoesNotContain(s[100], seenInWalk);                                            // updated after the cursor passed: left out of THIS walk
            Assert.DoesNotContain(s[60], tail);                                                   // closed: gone from the open list
            var fresh = (await Walk(b, 100)).SelectMany(x => x.Ids).ToList();
            Assert.Equal(await Oracle(grant.Patient, Open), fresh);                               // a new first page is the full, current picture
            Assert.Contains(s[100], fresh.Take(3));                                               // the skipped record is at the very top: it is not lost
            Assert.Contains(added[0], fresh.Take(3));
        }
        finally
        {
            await Remove(s.Concat(added));
        }
    }

    // ---------- headers, CORS, audit ----------

    [Fact]
    public async Task Responses_are_not_cacheable_and_the_new_headers_are_exposed_to_the_browser()
    {
        await using var grant = await GrantB();
        var mine = await Seed(grant.Patient, 5, Base());
        try
        {
            var b = await As("demo-physician-b");
            var req = new HttpRequestMessage(HttpMethod.Get, "/adverse-events/queue?limit=2");
            req.Headers.Add("Origin", "http://localhost:3000");
            var res = await b.Client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal("no-store", res.Headers.CacheControl?.ToString());
            var exposed = string.Join(',', res.Headers.GetValues("Access-Control-Expose-Headers")).Split(',').Select(h => h.Trim()).ToList();
            Assert.Contains(AdrEndpoints.QueueTruncatedHeader, exposed, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(AdrEndpoints.QueueNextCursorHeader, exposed, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(AdrEndpoints.QueueTotalCountHeader, exposed, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("true", Header(res, AdrEndpoints.QueueTruncatedHeader));
            Assert.NotNull(Header(res, AdrEndpoints.QueueNextCursorHeader));
        }
        finally
        {
            await Remove(mine);
        }
    }

    [Fact]
    public async Task A_client_that_sends_no_parameters_still_gets_a_bare_array_and_the_incomplete_flag_when_more_exist()
    {
        await using var grant = await GrantB();
        var mine = await Seed(grant.Patient, AdrEndpoints.QueueDefaultLimit + 10, Base());
        try
        {
            var res = await Get(await As("demo-physician-b"));
            Assert.Equal(JsonValueKind.Array, (await ClinicalApiTests.Json(res)).ValueKind);
            var page = await Read(await Get(await As("demo-physician-b")));
            Assert.Equal(AdrEndpoints.QueueDefaultLimit, page.Ids.Count);
            Assert.Equal("true", page.Truncated);
        }
        finally
        {
            await Remove(mine);
        }
    }

    [Fact]
    public async Task Audit_one_access_line_per_request_and_denials_only_for_patients_the_reviewer_is_related_to()
    {
        var audit = Svc<IAuditReader>();
        var physicianA = Id("demo-physician");
        var withoutConsent = Id("demo-patient-2"); // A is related to this patient but has no consent
        var unrelated = Id("subject-pt-7");
        var rows = (await Seed(withoutConsent, 2, Base())).Concat(await Seed(unrelated, 2, Base())).ToList();
        try
        {
            var a = await As("demo-physician");
            async Task<(int Access, int DeniedRelated, int DeniedUnrelated)> Counts() => (
                (await audit.QueryAsync(new AuditQuery(ActorUserId: physicianA, Action: AuditActions.AdrAccessed, Take: 500))).Count,
                (await audit.QueryAsync(new AuditQuery(ActorUserId: physicianA, SubjectUserId: withoutConsent, Action: AuditActions.AccessDenied, Take: 500))).Count,
                (await audit.QueryAsync(new AuditQuery(ActorUserId: physicianA, SubjectUserId: unrelated, Action: AuditActions.AccessDenied, Take: 500))).Count);

            var before = await Counts();
            await Get(a);
            var one = await Counts();
            await Get(a, "?limit=1");
            var two = await Counts();
            Assert.Equal(1, one.Access - before.Access);
            Assert.Equal(1, two.Access - one.Access);
            Assert.Equal(1, one.DeniedRelated - before.DeniedRelated);   // the relationship exists, the consent does not: a real, audited refusal, once per request
            Assert.Equal(1, two.DeniedRelated - one.DeniedRelated);
            Assert.Equal(before.DeniedUnrelated, two.DeniedUnrelated);   // a patient the reviewer has no relationship with is never evaluated, so no refusal is invented for them
        }
        finally
        {
            await Remove(rows);
        }
    }

    // ---------- the service itself has no "all patients" mode ----------

    [Fact]
    public async Task The_service_answers_an_empty_page_for_no_subjects_and_refuses_a_malformed_request()
    {
        var svc = Svc<IAdverseEventService>();
        var rows = await Seed(Guid.NewGuid(), 3, Base());
        try
        {
            var empty = await svc.ListQueueAsync(new AdrQueueRequest(Open, [], 50, null, null, true));
            Assert.True(empty.Succeeded);
            Assert.Empty(empty.Value!.Items);
            Assert.False(empty.Value.HasMore);
            Assert.Equal(0, empty.Value.Total);

            Assert.Equal(AdrError.Validation, (await svc.ListQueueAsync(new AdrQueueRequest(Open, [Guid.NewGuid()], 0, null, null, false))).Error);
            Assert.Equal(AdrError.Validation, (await svc.ListQueueAsync(new AdrQueueRequest(Open, [Guid.NewGuid()], 101, null, null, false))).Error);
            Assert.Equal(AdrError.Validation, (await svc.ListQueueAsync(new AdrQueueRequest(Open, [Guid.NewGuid()], 10, DateTimeOffset.UtcNow, null, false))).Error);
        }
        finally
        {
            await Remove(rows);
        }
    }
}
