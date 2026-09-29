using System.ComponentModel.DataAnnotations;

namespace MedSmarter.BuildingBlocks.Infrastructure.Options;

// Bound from environment variables / user-secrets / secret manager. No defaults carry credentials.

public sealed class RedisOptions
{
    public const string Section = "Redis";

    /// <summary>StackExchange.Redis configuration string, e.g. "localhost:6379,password=...".</summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;
}

public sealed class OpenSearchOptions
{
    public const string Section = "OpenSearch";

    [Required, Url]
    public string Uri { get; set; } = string.Empty;

    /// <summary>Optional basic-auth credentials. Leave empty only for local dev (security plugin disabled).</summary>
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public sealed class KafkaOptions
{
    public const string Section = "Kafka";

    [Required]
    public string BootstrapServers { get; set; } = string.Empty;

    public string ClientId { get; set; } = "medsmarter-core";
}

public sealed class HealthOptions
{
    public const string Section = "Health";

    /// <summary>Per-dependency timeout used by readiness probes.</summary>
    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 3;
}
