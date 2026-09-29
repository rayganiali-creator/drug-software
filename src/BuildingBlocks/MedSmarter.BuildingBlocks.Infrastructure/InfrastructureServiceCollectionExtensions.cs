using Confluent.Kafka;
using MedSmarter.BuildingBlocks.Infrastructure.Health;
using MedSmarter.BuildingBlocks.Infrastructure.Options;
using MedSmarter.BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Net.Http.Headers;
using System.Text;

namespace MedSmarter.BuildingBlocks.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public const string PostgresConnectionName = "Postgres";
    public const string ReadyTag = "ready";

    /// <summary>
    /// Registers PostgreSQL, Redis, OpenSearch and Kafka connections plus their readiness checks.
    /// Every connection setting must come from configuration (env vars / secret store); startup fails
    /// fast when one is missing.
    /// </summary>
    public static IServiceCollection AddPlatformInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IClock, SystemClock>();

        services.AddOptions<HealthOptions>().Bind(configuration.GetSection(HealthOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<RedisOptions>().Bind(configuration.GetSection(RedisOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<OpenSearchOptions>().Bind(configuration.GetSection(OpenSearchOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<KafkaOptions>().Bind(configuration.GetSection(KafkaOptions.Section)).ValidateDataAnnotations().ValidateOnStart();

        // PostgreSQL
        var pg = configuration.GetConnectionString(PostgresConnectionName);
        if (string.IsNullOrWhiteSpace(pg))
        {
            throw new InvalidOperationException("ConnectionStrings:Postgres is not configured (env var ConnectionStrings__Postgres).");
        }

        services.AddDbContext<PlatformDbContext>(o => o.UseNpgsql(pg, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", PlatformDbContext.Schema)));

        // Redis: AbortOnConnectFail=false so the API can boot (and report "not ready") while Redis is down.
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var opts = ConfigurationOptions.Parse(sp.GetRequiredService<IOptions<RedisOptions>>().Value.ConnectionString);
            opts.AbortOnConnectFail = false;
            opts.ConnectTimeout = 3000;
            return ConnectionMultiplexer.Connect(opts);
        });

        // OpenSearch (plain REST; a typed client is added when search features arrive).
        services.AddHttpClient(OpenSearchHealthCheck.ClientName, (sp, http) =>
        {
            var o = sp.GetRequiredService<IOptions<OpenSearchOptions>>().Value;
            http.BaseAddress = new Uri(o.Uri.EndsWith('/') ? o.Uri : o.Uri + "/");
            if (!string.IsNullOrEmpty(o.Username))
            {
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{o.Username}:{o.Password}"));
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            }
        });

        // Kafka admin client (metadata only; producer/consumer registered with the outbox publisher later).
        services.AddSingleton<IAdminClient>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<KafkaOptions>>().Value;
            return new AdminClientBuilder(new AdminClientConfig
            {
                BootstrapServers = o.BootstrapServers,
                ClientId = o.ClientId,
                SocketTimeoutMs = 3000,
            }).Build();
        });

        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgres", HealthStatus.Unhealthy, [ReadyTag])
            .AddCheck<RedisHealthCheck>("redis", HealthStatus.Unhealthy, [ReadyTag])
            .AddCheck<OpenSearchHealthCheck>("opensearch", HealthStatus.Unhealthy, [ReadyTag])
            .AddCheck<KafkaHealthCheck>("kafka", HealthStatus.Unhealthy, [ReadyTag]);

        return services;
    }
}
