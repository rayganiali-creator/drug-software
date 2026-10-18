using System.Net;
using System.Net.Http.Json;
using MedSmarter.Api.Adr;
using MedSmarter.Modules.ADR.Contracts;
using MedSmarter.Modules.ADR.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Patients.Tests;

/// <summary>
/// Phase 17: the reviewer's queue reads a bounded number of records of ALL patients before it filters them to the ones the reviewer may read. When the read hit the bound the list
/// can be incomplete, so the response says so (header), and nothing may present a count made from it as exact.
/// </summary>
public class Phase17ApiTests(PatientsApiFactory factory) : IClassFixture<PatientsApiFactory>
{
    private Task<ClinicalApiTests.Session> As(string account) => ClinicalApiTests.Login(factory, account);

    private static async Task<Guid> Create(ClinicalApiTests.Session s, Guid subject, string text)
    {
        var res = await s.Client.PostAsJsonAsync($"/patients/{subject}/adverse-events", new { eventDescription = text, suspectedMedications = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await ClinicalApiTests.Json(res)).GetProperty("event").GetProperty("id").GetGuid();
    }

    private static string Truncated(HttpResponseMessage res) => Assert.Single(res.Headers.GetValues(AdrEndpoints.QueueTruncatedHeader));

    [Fact]
    public async Task A_queue_read_under_the_cap_says_it_is_complete_and_over_HTTP_an_unauthorized_caller_gets_no_header_value_to_learn_from()
    {
        var sara = await As("demo-patient");
        await Create(sara, sara.UserId, "Phase 17 small queue");
        var res = await (await As("demo-physician")).Client.GetAsync("/adverse-events/queue");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("false", Truncated(res));

        var refused = await sara.Client.GetAsync("/adverse-events/queue");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.False(refused.Headers.Contains(AdrEndpoints.QueueTruncatedHeader)); // the flag is not set for a caller who is refused
    }

    [Fact]
    public async Task When_the_read_hits_the_cap_the_response_says_so_and_an_authorized_older_record_may_be_missing_from_the_list()
    {
        var sara = await As("demo-patient");
        var saraRecord = await Create(sara, sara.UserId, "Phase 17 old record"); // the reviewer may read this one
        await using (var db = await factory.Services.GetRequiredService<IDbContextFactory<AdrDbContext>>().CreateDbContextAsync())
        {
            var later = DateTimeOffset.UtcNow.AddHours(1);
            for (var i = 0; i < AdrEndpoints.QueueReadCap; i++)
            {
                // newer open records of other, unrelated patients: they fill the read, then are filtered out
                db.Events.Add(new AdverseEventRow
                {
                    Id = Guid.NewGuid(), SubjectId = Guid.NewGuid(), ReporterRole = ReporterRole.Patient, ReporterUserId = Guid.NewGuid(), Origin = ReportOrigin.PatientReported,
                    ReportedAt = later, EventDescription = "Synthetic filler " + i, Status = ReviewStatus.Submitted, LastActivityAt = later.AddSeconds(i),
                });
            }

            await db.SaveChangesAsync();
        }

        var doctor = await As("demo-physician");
        var res = await doctor.Client.GetAsync("/adverse-events/queue");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("true", Truncated(res)); // the client is told the list may be incomplete
        var ids = (await ClinicalApiTests.Json(res)).EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.DoesNotContain(saraRecord, ids); // this is exactly why an exact-looking count would have been wrong: a record the reviewer may read is not in the list
        // and the filler records of unrelated patients never appear
        Assert.Empty(ids);
    }
}
