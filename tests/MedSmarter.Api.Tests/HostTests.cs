using System.Net;
using System.Text.Json;

namespace MedSmarter.Api.Tests;

public class HostTests(UnreachableDependenciesFactory factory) : IClassFixture<UnreachableDependenciesFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Live_is_healthy_without_any_dependency()
    {
        var res = await _client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("no-store", res.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Ready_is_503_and_reports_each_dependency_without_leaking_details()
    {
        var res = await _client.GetAsync("/health/ready");
        var body = await res.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        var checks = JsonDocument.Parse(body).RootElement.GetProperty("checks");
        foreach (var name in new[] { "postgres", "redis", "opensearch", "kafka" })
        {
            Assert.Equal("Unhealthy", checks.GetProperty(name).GetString());
        }

        Assert.DoesNotContain("127.0.0.1", body, StringComparison.Ordinal);
        Assert.DoesNotContain("not-a-real-secret", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Version_lists_all_22_modules()
    {
        var doc = JsonDocument.Parse(await _client.GetStringAsync("/version"));
        Assert.Equal("medsmarter-api", doc.RootElement.GetProperty("service").GetString());
        Assert.Equal(22, doc.RootElement.GetProperty("modules").GetArrayLength());
    }

    [Fact]
    public async Task Safe_incoming_correlation_id_is_echoed()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/version");
        req.Headers.Add("X-Correlation-Id", "abc-12345678");
        var res = await _client.SendAsync(req);
        Assert.Equal("abc-12345678", res.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task Unsafe_incoming_correlation_id_is_replaced()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/version");
        req.Headers.TryAddWithoutValidation("X-Correlation-Id", "x y\"{evil}");
        var res = await _client.SendAsync(req);
        var id = res.Headers.GetValues("X-Correlation-Id").Single();
        Assert.NotEqual("x y\"{evil}", id);
        Assert.Matches("^[a-f0-9]{32}$", id);
    }

    [Fact]
    public async Task Cors_allows_only_configured_origin()
    {
        var allowed = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        allowed.Headers.Add("Origin", "http://localhost:3000");
        var ok = await _client.SendAsync(allowed);
        Assert.Equal("http://localhost:3000", ok.Headers.GetValues("Access-Control-Allow-Origin").Single());

        var other = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        other.Headers.Add("Origin", "https://evil.example");
        var denied = await _client.SendAsync(other);
        Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
