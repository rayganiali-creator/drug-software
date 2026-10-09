using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MedSmarter.Api.Tests;

/// <summary>
/// Boots the real Host with every dependency pointing at a closed local port. Proves the API starts,
/// stays live and reports "not ready" instead of crashing when infrastructure is down.
/// </summary>
public sealed class UnreachableDependenciesFactory : WebApplicationFactory<Program>
{
    private static readonly Dictionary<string, string> Settings = new()
    {
        ["ConnectionStrings__Postgres"] = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=not-a-real-secret;Timeout=1;Command Timeout=1",
        ["Redis__ConnectionString"] = "127.0.0.1:1",
        ["OpenSearch__Uri"] = "http://127.0.0.1:1",
        ["Kafka__BootstrapServers"] = "127.0.0.1:1",
        ["Health__TimeoutSeconds"] = "1",
        ["Cors__AllowedOrigins__0"] = "http://localhost:3000",
    };

    public UnreachableDependenciesFactory()
    {
        foreach (var (k, v) in Settings)
        {
            Environment.SetEnvironmentVariable(k, v);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseSetting("Persistence:Provider", "InMemory"); // tests never need a database
}
