using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Medications.Contracts;
using Npgsql;

namespace MedSmarter.Knowledge.Tests;

/// <summary>Phase 5 prerequisites A and B: the medication repository and the audit log are durable. These run only with MEDSMARTER_PG_TEST.</summary>
public class PersistenceTests
{
    private static async Task<int> Exec(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return await cmd.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task Medications_survive_an_application_restart()
    {
        using var first = new KEnv();
        var before = await first.Names("nocturin");
        Assert.NotEmpty(before);
        using var second = first.Restart(); // no seeding: everything comes from the database
        Assert.Equal(before, await second.Names("nocturin"));
        var detail = await second.Meds.GetAsync((await second.One("nocturin")).Id);
        Assert.True(detail.Succeeded);
        Assert.True(detail.Value!.IsDemo);
    }

    [PostgresFact]
    public async Task Audit_entries_survive_a_restart_and_the_chain_still_verifies()
    {
        using var first = new KEnv();
        var actor = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
        {
            await first.Get<IAuditWriter>().WriteAsync(new AuditEvent(AuditActions.AdminAction, AuditResult.Success, actor, "test", i.ToString(), Metadata: new Dictionary<string, string> { ["n"] = i.ToString() }));
        }

        using var second = first.Restart();
        Assert.True(await second.Audit.VerifyChainAsync());
        var mine = await second.Audit.QueryAsync(new AuditQuery(ActorUserId: actor));
        Assert.Equal(5, mine.Count);
        await second.Get<IAuditWriter>().WriteAsync(new AuditEvent(AuditActions.AdminAction, AuditResult.Success, actor, "test", "after-restart")); // the chain continues from the stored head
        Assert.True(await second.Audit.VerifyChainAsync());
    }

    [PostgresFact]
    public async Task Audit_rows_cannot_be_updated_deleted_or_truncated()
    {
        using var env = new KEnv();
        await env.Get<IAuditWriter>().WriteAsync(new AuditEvent(AuditActions.AdminAction, AuditResult.Success, Guid.NewGuid()));
        foreach (var sql in new[] { "UPDATE audit.audit_entry SET action = 'X'", "DELETE FROM audit.audit_entry", "TRUNCATE audit.audit_entry" })
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => Exec(env.ConnectionString!, sql));
            Assert.Equal("23001", ex.SqlState); // restrict_violation raised by the append-only trigger
        }

        Assert.True(await env.Audit.VerifyChainAsync());
    }

    [PostgresFact]
    public async Task A_row_changed_behind_the_triggers_is_detected_by_the_chain_check()
    {
        using var env = new KEnv();
        var writer = env.Get<IAuditWriter>();
        for (var i = 0; i < 3; i++)
        {
            await writer.WriteAsync(new AuditEvent(AuditActions.AdminAction, AuditResult.Success, Guid.NewGuid(), "test", i.ToString()));
        }

        Assert.True(await env.Audit.VerifyChainAsync());
        // Simulates an attacker with owner rights who drops the protection first: the hash chain still exposes the change.
        await Exec(env.ConnectionString!, "ALTER TABLE audit.audit_entry DISABLE TRIGGER ALL; UPDATE audit.audit_entry SET reason_code = 'edited' WHERE sequence = 2; ALTER TABLE audit.audit_entry ENABLE TRIGGER ALL;");
        Assert.False(await env.Audit.VerifyChainAsync());
    }

    [PostgresFact]
    public async Task Two_application_instances_writing_at_once_keep_one_gap_free_chain()
    {
        using var a = new KEnv(false);
        using var b = a.Restart(); // second "API instance" on the same database
        var tasks = Enumerable.Range(0, 40).Select(i => Task.Run(() =>
            (i % 2 == 0 ? a : b).Get<IAuditWriter>().WriteAsync(new AuditEvent(AuditActions.AdminAction, AuditResult.Success, Guid.NewGuid(), "load", i.ToString()))));
        await Task.WhenAll(tasks);
        Assert.True(await a.Audit.VerifyChainAsync());
        var all = await a.Audit.QueryAsync(new AuditQuery(Take: 500));
        Assert.Equal(Enumerable.Range(1, 40).Select(x => (long)x), all.Select(e => e.Sequence).OrderBy(x => x));
    }

    [PostgresFact]
    public async Task Substring_search_can_use_the_trigram_index()
    {
        using var env = new KEnv();
        await using var conn = new NpgsqlConnection(env.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var off = new NpgsqlCommand("SET LOCAL enable_seqscan = off", conn, tx))
        {
            await off.ExecuteNonQueryAsync();
        }

        var plan = new System.Text.StringBuilder();
        await using (var explain = new NpgsqlCommand("EXPLAIN SELECT medication_id FROM medications.medication_search_term WHERE normalized LIKE '%noct%'", conn, tx))
        await using (var reader = await explain.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                plan.AppendLine(reader.GetString(0));
            }
        }

        Assert.Contains("ix_medication_search_term_trgm", plan.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_edits_with_the_same_version_have_exactly_one_winner()
    {
        using var env = new KEnv();
        var (_, rev) = await env.RealSource();
        var ing = await env.Ingredient("racetestium");
        var med = (await env.Admin.CreateAsync(KEnv.Actor, env.Draft(ing, rev.Id), "test", null)).Value!;
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() =>
            env.Admin.UpdateAsync(KEnv.Actor, med.Id, med.Version, env.Draft(ing, rev.Id, name: "Racer " + i), "race " + i, "test", null))));
        Assert.Equal(1, results.Count(r => r.Succeeded));
        Assert.All(results.Where(r => !r.Succeeded), r => Assert.Equal(MedicationError.Conflict, r.Error));
        Assert.Equal(med.Version + 1, (await env.Meds.GetAsync(med.Id, true)).Value!.Version);
    }

    [Fact]
    public async Task Duplicate_sources_revisions_and_codes_are_conflicts_not_server_errors()
    {
        using var env = new KEnv();
        var src = new NewKnowledgeSource("Dup source (fictional)", "t", SourceType.Publication, null, "v1", "licence", true, null);
        Assert.True((await env.Sources.RegisterSourceAsync(KEnv.Actor, src, "t", null)).Succeeded);
        Assert.Equal(MedicationError.Conflict, (await env.Sources.RegisterSourceAsync(KEnv.Actor, src, "t", null)).Error);
        var s = (await env.Sources.ListSourcesAsync()).First(x => x.Name == "Dup source (fictional)");
        Assert.True((await env.Sources.AddRevisionAsync(KEnv.Actor, new NewRevision(s.Id, "r1", null), "t", null)).Succeeded);
        Assert.Equal(MedicationError.Conflict, (await env.Sources.AddRevisionAsync(KEnv.Actor, new NewRevision(s.Id, "r1", null), "t", null)).Error);
        var m = new NewManufacturer(new LocalizedText("Dup maker (fictional)", null), "IR", "DEMO-CODE-1");
        Assert.True((await env.Admin.CreateManufacturerAsync(KEnv.Actor, m)).Succeeded);
        Assert.Equal(MedicationError.Conflict, (await env.Admin.CreateManufacturerAsync(KEnv.Actor, m)).Error);
    }
}
