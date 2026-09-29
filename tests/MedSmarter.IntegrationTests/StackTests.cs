using System.Text.Json;
using MedSmarter.BuildingBlocks;
using MedSmarter.BuildingBlocks.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StackExchange.Redis;

namespace MedSmarter.IntegrationTests;

public class StackTests
{
    private static string Env(string name) =>
        Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"{name} is not set");

    [IntegrationFact]
    public async Task Migrations_create_platform_schema_and_outbox_roundtrips()
    {
        // Scratch database so the developer's data is never touched.
        var admin = new NpgsqlConnectionStringBuilder(Env("ConnectionStrings__Postgres"));
        var scratch = "it_" + Guid.NewGuid().ToString("N")[..12];
        await using (var conn = new NpgsqlConnection(admin.ConnectionString))
        {
            await conn.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {scratch}", conn);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            admin.Database = scratch;
            var options = new DbContextOptionsBuilder<PlatformDbContext>()
                .UseNpgsql(admin.ConnectionString, o => o.MigrationsHistoryTable("__ef_migrations_history", PlatformDbContext.Schema))
                .Options;

            await using (var db = new PlatformDbContext(options))
            {
                await db.Database.MigrateAsync();
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
                db.OutboxMessages.Add(new OutboxMessage { Id = Guid.NewGuid(), Type = "test.event", Payload = "{\"a\":1}", OccurredAt = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync();
            }

            await using (var db = new PlatformDbContext(options))
            {
                var row = await db.OutboxMessages.SingleAsync();
                Assert.Equal("test.event", row.Type);
                Assert.Null(row.ProcessedAt);
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var conn = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(Env("ConnectionStrings__Postgres")).ConnectionString);
            await conn.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {scratch} WITH (FORCE)", conn);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [IntegrationFact]
    public async Task Redis_roundtrip()
    {
        await using var mux = await ConnectionMultiplexer.ConnectAsync(Env("Redis__ConnectionString"));
        var db = mux.GetDatabase();
        var key = "it:" + Guid.NewGuid().ToString("N");
        await db.StringSetAsync(key, "v", TimeSpan.FromSeconds(30));
        Assert.Equal("v", (string?)await db.StringGetAsync(key));
    }

    [IntegrationFact]
    public async Task Api_is_ready_when_the_whole_stack_is_up()
    {
        // Requires migrations to have been applied to the configured database (make migrate).
        await using var factory = new WebApplicationFactory<Program>();
        var res = await factory.CreateClient().GetAsync("/health/ready");
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, body);
        var checks = JsonDocument.Parse(body).RootElement.GetProperty("checks");
        foreach (var c in checks.EnumerateObject())
        {
            Assert.Equal("Healthy", c.Value.GetString());
        }
    }
}
