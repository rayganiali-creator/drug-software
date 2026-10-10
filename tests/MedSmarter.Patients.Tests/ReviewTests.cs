using MedSmarter.BuildingBlocks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Patients.Tests;

/// <summary>Cross-phase checks added by the repository review after Phase 5.</summary>
public class ReviewTests(PatientsApiFactory factory) : IClassFixture<PatientsApiFactory>
{
    // Routes that are public on purpose, or that need only a valid session (no permission): everything else must name a perm:<permission> policy.
    private static readonly HashSet<string> Public = ["/health/live", "/health/ready", "/version", "/auth/login", "/auth/refresh", "/auth/demo-accounts"];
    private static readonly HashSet<string> SessionOnly = ["/auth/logout", "/auth/logout-all", "/auth/me", "/guidance/samples"] /* the handler itself requires guidance.read or guidance.professional.read */;

    [Fact]
    public void Every_route_either_is_on_the_public_list_or_names_an_explicit_permission_policy()
    {
        var endpoints = factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().ToList();
        Assert.True(endpoints.Count > 100, "the route table should be fully populated");
        var offenders = new List<string>();
        foreach (var e in endpoints)
        {
            var path = "/" + (e.RoutePattern.RawText ?? string.Empty).TrimStart('/');
            var anonymous = e.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            var policies = e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).Where(p => p is not null).ToList();
            var explicitPermission = policies.Any(p => p!.StartsWith("perm:", StringComparison.Ordinal));
            if (anonymous)
            {
                if (!Public.Contains(path))
                {
                    offenders.Add($"{path}: anonymous but not on the public list");
                }
            }
            else if (!explicitPermission && !SessionOnly.Contains(path))
            {
                offenders.Add($"{path}: relies on the fallback policy only");
            }
        }

        Assert.True(offenders.Count == 0, string.Join("; ", offenders));
    }

    [Theory]
    [InlineData("sara​@example.com")]                      // zero-width space hides an e-mail address
    [InlineData("call 0912​3456​789")]                // zero-width spaces split a phone number
    [InlineData("ht​tps://example.com")]                    // zero-width space hides a link
    [InlineData("id １２３４５６７８９０")]                       // full-width digits
    [InlineData("id 12345­67890")]                          // soft hyphen inside a number
    [InlineData("mail⁠me⁠@⁠example.com")]         // word joiner around an e-mail address
    public void Free_text_filter_cannot_be_bypassed_with_invisible_or_alternative_characters(string text)
    {
        Assert.NotNull(FreeTextGuard.Problem(text, "t", 200));
    }

    [Theory]
    [InlineData("sara\uFF20example.com")]            // full-width @: invariant globalization does not fold it
    [InlineData("\uFF48\uFF54\uFF54\uFF50\uFF53\uFF1A\uFF0F\uFF0Fexample.com")] // full-width "https://"
    [InlineData("\U0001D7CE\U0001D7D7\U0001D7CF\U0001D7D0\U0001D7D1\U0001D7D2\U0001D7D3\U0001D7D4\U0001D7D5\U0001D7D6\U0001D7D7")] // mathematical bold digits
    public void Free_text_filter_folds_look_alike_characters_first(string text) => Assert.NotNull(FreeTextGuard.Problem(text, "t", 200));

    [Fact]
    public void Free_text_filter_still_accepts_ordinary_text_in_both_scripts()
    {
        Assert.Null(FreeTextGuard.Problem("Mild headache after the evening dose", "t", 200));
        Assert.Null(FreeTextGuard.Problem("سردرد خفیف بعد از دوز شب", "t", 200));
        Assert.Null(FreeTextGuard.Problem("می‌خواهم", "t", 200)); // contains a ZWNJ, which Persian needs
    }
}
