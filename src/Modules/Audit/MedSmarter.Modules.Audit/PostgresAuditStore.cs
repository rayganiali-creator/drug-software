using System.Runtime.CompilerServices;
using System.Text.Json;
using MedSmarter.Modules.Audit.Contracts;
using MedSmarter.Modules.Audit.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedSmarter.Modules.Audit;

/// <summary>
/// Durable, append-only audit store. Appends are serialised by a transaction-scoped advisory lock so the sequence number and the
/// previous hash can never fork, even with several API instances writing at the same time.
/// </summary>
public sealed class PostgresAuditStore(IDbContextFactory<AuditDbContext> factory) : IAuditStore
{
    private const long AppendLockKey = 0x4D53_4155_4449_54L; // "MSAUDIT"

    public async Task<AuditEntry> AppendAsync(Func<long, string, AuditEntry> build, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_xact_lock({AppendLockKey})", ct);
        var last = await db.Entries.AsNoTracking().OrderByDescending(x => x.Sequence).Select(x => new { x.Sequence, x.Hash }).FirstOrDefaultAsync(ct);
        var previous = last?.Hash ?? AuditService.GenesisHash;
        var entry = build((last?.Sequence ?? 0) + 1, previous);
        db.Entries.Add(new AuditRow
        {
            Sequence = entry.Sequence, Id = entry.Id, Timestamp = entry.Timestamp, ActorUserId = entry.ActorUserId, Action = entry.Action, ResourceType = entry.ResourceType, ResourceId = entry.ResourceId,
            SubjectUserId = entry.SubjectUserId, Result = (int)entry.Result, Source = entry.Source, CorrelationId = entry.CorrelationId, ReasonCode = entry.ReasonCode,
            MetadataJson = JsonSerializer.Serialize(entry.Metadata), PreviousHash = previous, Hash = entry.Hash,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return entry;
    }

    public async Task<IReadOnlyList<AuditEntry>> SnapshotAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return [.. (await db.Entries.AsNoTracking().OrderBy(x => x.Sequence).ToListAsync(ct)).Select(x => x.ToEntry())];
    }

    public async Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var q = db.Entries.AsNoTracking().AsQueryable();
        if (query.ActorUserId is { } actor) { q = q.Where(x => x.ActorUserId == actor); }
        if (query.SubjectUserId is { } subject) { q = q.Where(x => x.SubjectUserId == subject); }
        if (query.Action is { } action) { q = q.Where(x => x.Action == action); }
        return [.. (await q.OrderByDescending(x => x.Sequence).Take(Math.Clamp(query.Take, 1, 500)).ToListAsync(ct)).Select(x => x.ToEntry())];
    }

    public async IAsyncEnumerable<AuditEntry> StreamAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await foreach (var row in db.Entries.AsNoTracking().OrderBy(x => x.Sequence).AsAsyncEnumerable().WithCancellation(ct))
        {
            yield return row.ToEntry();
        }
    }
}
