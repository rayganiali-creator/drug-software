using Confluent.Kafka;
using MedSmarter.BuildingBlocks.Infrastructure.Options;
using MedSmarter.BuildingBlocks.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Net.Http.Json;
using System.Text.Json;

namespace MedSmarter.BuildingBlocks.Infrastructure.Health;

// Health results deliberately never include exception text: readiness output is unauthenticated
// and must not leak hostnames, credentials or stack traces. Details go to the (PHI-free) log.

public sealed class PostgresHealthCheck(PlatformDbContext db, IOptions<HealthOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        try
        {
            return await db.Database.CanConnectAsync(cts.Token)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("cannot connect");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("unreachable");
        }
    }
}

public sealed class RedisHealthCheck(IConnectionMultiplexer redis, IOptions<HealthOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var ping = redis.GetDatabase().PingAsync();
            var timeout = Task.Delay(TimeSpan.FromSeconds(options.Value.TimeoutSeconds), cancellationToken);
            if (await Task.WhenAny(ping, timeout) != ping)
            {
                return HealthCheckResult.Unhealthy("timeout");
            }

            await ping;
            return HealthCheckResult.Healthy();
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("unreachable");
        }
    }
}

public sealed class OpenSearchHealthCheck(IHttpClientFactory httpFactory, IOptions<HealthOptions> options) : IHealthCheck
{
    public const string ClientName = "opensearch";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        try
        {
            var client = httpFactory.CreateClient(ClientName);
            using var response = await client.GetAsync("_cluster/health", cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Unhealthy("bad status");
            }

            var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cts.Token);
            var status = doc.TryGetProperty("status", out var s) ? s.GetString() : null;
            // A single-node dev cluster is "yellow" once replicas are configured; only "red" is unusable.
            return status is "green" or "yellow"
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("cluster " + (status ?? "unknown"));
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("unreachable");
        }
    }
}

public sealed class KafkaHealthCheck(IAdminClient admin, IOptions<HealthOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // AdminClient.GetMetadata is blocking; run it off the request thread.
        return Task.Run(() =>
        {
            try
            {
                var md = admin.GetMetadata(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
                return md.Brokers.Count > 0
                    ? HealthCheckResult.Healthy()
                    : HealthCheckResult.Unhealthy("no brokers");
            }
            catch (KafkaException)
            {
                return HealthCheckResult.Unhealthy("unreachable");
            }
        }, cancellationToken);
    }
}
