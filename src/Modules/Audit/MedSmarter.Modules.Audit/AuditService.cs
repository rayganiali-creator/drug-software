using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Audit.Contracts;
using Microsoft.Extensions.Logging;

namespace MedSmarter.Modules.Audit;

/// <summary>Append-only storage. A durable implementation (PostgreSQL, append-only role) replaces the in-memory one later.</summary>
public interface IAuditStore
{
    /// <summary>Appends atomically; <paramref name="build"/> receives the previous hash and the next sequence number.</summary>
    Task<AuditEntry> AppendAsync(Func<long, string, AuditEntry> build, CancellationToken ct);
    Task<IReadOnlyList<AuditEntry>> SnapshotAsync(CancellationToken ct);

    /// <summary>Newest first. The store applies the filters so a durable store never loads the whole log.</summary>
    Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken ct);

    /// <summary>Every entry in sequence order, streamed (used to verify the hash chain).</summary>
    IAsyncEnumerable<AuditEntry> StreamAsync(CancellationToken ct);
}

public sealed class InMemoryAuditStore : IAuditStore
{
    private readonly List<AuditEntry> _entries = [];
    private readonly Lock _gate = new();

    public Task<AuditEntry> AppendAsync(Func<long, string, AuditEntry> build, CancellationToken ct)
    {
        lock (_gate)
        {
            var prev = _entries.Count == 0 ? AuditService.GenesisHash : _entries[^1].Hash;
            var entry = build(_entries.Count + 1, prev);
            _entries.Add(entry);
            return Task.FromResult(entry);
        }
    }

    public Task<IReadOnlyList<AuditEntry>> SnapshotAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<AuditEntry>>([.. _entries]);
        }
    }

    public Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<AuditEntry>>([.. _entries
                .Where(x => (query.ActorUserId is null || x.ActorUserId == query.ActorUserId)
                         && (query.SubjectUserId is null || x.SubjectUserId == query.SubjectUserId)
                         && (query.Action is null || x.Action == query.Action))
                .OrderByDescending(x => x.Sequence)
                .Take(Math.Clamp(query.Take, 1, 500))]);
        }
    }

    public async IAsyncEnumerable<AuditEntry> StreamAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var e in await SnapshotAsync(ct))
        {
            yield return e;
        }
    }

    /// <summary>Test hook: simulates tampering with stored data.</summary>
    internal void TamperForTest(int index, Func<AuditEntry, AuditEntry> mutate)
    {
        lock (_gate)
        {
            _entries[index] = mutate(_entries[index]);
        }
    }
}

/// <summary>
/// Central audit writer/reader. Sanitises metadata (credentials and token-like values never reach the log) and chains
/// entries with SHA-256 so that alteration or removal is detectable.
/// </summary>
public sealed partial class AuditService(IAuditStore store, IClock clock, ILogger<AuditService> logger) : IAuditWriter, IAuditReader
{
    public const string GenesisHash = "GENESIS";
    private const int MaxValueLength = 200;
    private const int MaxMetadataItems = 12;

    [GeneratedRegex("(pass(word)?|pwd|secret|token|authorization|bearer|cookie|credential|apikey|api_key|otp|pin)", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveKey();

    // JWT-like (three base64url segments) or long opaque secrets
    [GeneratedRegex(@"^[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}$|^[A-Za-z0-9_\-+/=]{40,}$")]
    private static partial Regex SecretLikeValue();

    [LoggerMessage(Level = LogLevel.Information, Message = "audit {Action} {Result} actor={Actor} resourceType={ResourceType} seq={Seq}")]
    private static partial void LogAudit(ILogger logger, string action, AuditResult result, Guid? actor, string? resourceType, long seq);

    public async Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        var meta = Sanitize(auditEvent.Metadata);
        var now = Micro(clock.UtcNow);
        var entry = await store.AppendAsync((seq, prev) =>
        {
            var id = Guid.NewGuid();
            var partial = new AuditEntry(id, seq, now, auditEvent.ActorUserId, auditEvent.Action, Trim(auditEvent.ResourceType), Trim(auditEvent.ResourceId), auditEvent.SubjectUserId, auditEvent.Result,
                Trim(auditEvent.Source) ?? "api", Trim(auditEvent.CorrelationId), Trim(auditEvent.ReasonCode), meta, string.Empty);
            return partial with { Hash = ComputeHash(partial, prev) };
        }, cancellationToken);

        // Structured application log: identifiers and outcome only, never metadata values.
        LogAudit(logger, entry.Action, entry.Result, entry.ActorUserId, entry.ResourceType, entry.Sequence);
    }

    public async Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        return await store.QueryAsync(query, cancellationToken);
    }

    public async Task<bool> VerifyChainAsync(CancellationToken cancellationToken = default)
    {
        var prev = GenesisHash;
        long expected = 1;
        await foreach (var e in store.StreamAsync(cancellationToken))
        {
            if (e.Sequence != expected || e.Hash != ComputeHash(e, prev))
            {
                return false;
            }

            prev = e.Hash;
            expected++;
        }

        return true;
    }

    /// <summary>PostgreSQL keeps microseconds; the hash covers the stored precision so a durable round trip verifies.</summary>
    private static DateTimeOffset Micro(DateTimeOffset t) => new(t.UtcTicks - (t.UtcTicks % 10), TimeSpan.Zero);

    private static string? Trim(string? s) => s is null ? null : (s.Length <= MaxValueLength ? s : s[..MaxValueLength]);

    internal static IReadOnlyDictionary<string, string> Sanitize(IReadOnlyDictionary<string, string>? metadata)
    {
        var result = new Dictionary<string, string>();
        if (metadata is null)
        {
            return result;
        }

        foreach (var (k, v) in metadata.Take(MaxMetadataItems))
        {
            if (SensitiveKey().IsMatch(k))
            {
                continue; // drop the key entirely: even its presence is not useful
            }

            var value = v.Length > MaxValueLength ? v[..MaxValueLength] : v;
            result[k.Length > 50 ? k[..50] : k] = SecretLikeValue().IsMatch(value) ? "[redacted]" : value;
        }

        return result;
    }

    internal static string ComputeHash(AuditEntry e, string previousHash)
    {
        var sb = new StringBuilder();
        sb.Append(previousHash).Append('|').Append(e.Id).Append('|').Append(e.Sequence).Append('|').Append(e.Timestamp.UtcTicks).Append('|')
          .Append(e.ActorUserId).Append('|').Append(e.Action).Append('|').Append(e.ResourceType).Append('|').Append(e.ResourceId).Append('|')
          .Append(e.SubjectUserId).Append('|').Append((int)e.Result).Append('|').Append(e.Source).Append('|').Append(e.CorrelationId).Append('|').Append(e.ReasonCode);
        foreach (var (k, v) in e.Metadata.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            sb.Append('|').Append(k).Append('=').Append(v);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
