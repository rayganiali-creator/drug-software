using System.Net;
using System.Net.Http.Json;
using MedSmarter.Api.Adr;
using MedSmarter.Modules.ADR.Contracts;

namespace MedSmarter.Patients.Tests;

/// <summary>
/// Phase 17 introduced the completeness header on the reviewer's queue. Phase 18 replaced the capped global read behind it by a read limited to the patients the reviewer may read
/// (see Phase18ApiTests); the test that pinned the old cap ("an authorized older record may be missing") was replaced there by its opposite.
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
}
