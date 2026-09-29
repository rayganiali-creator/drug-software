using MedSmarter.BuildingBlocks.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedSmarter.Api.Tests;

public class ConfigurationTests
{
    [Fact]
    public void Missing_postgres_connection_string_fails_fast()
    {
        var config = new ConfigurationBuilder().Build();
        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddPlatformInfrastructure(config));
        Assert.Contains("ConnectionStrings__Postgres", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_appsettings_contain_no_credentials()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MedSmarter.sln"))) { dir = dir.Parent; }
        var text = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Host", "MedSmarter.Api", "appsettings.json"));
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Host=", text, StringComparison.Ordinal);
    }
}
