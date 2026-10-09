using MedSmarter.Modules.Consent.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedSmarter.Modules.Consent;

public sealed class PostgresConsentStore(IDbContextFactory<ConsentDbContext> factory) : IConsentStore
{
    public async Task AddAsync(ConsentRecord record, ConsentEventRecord granted, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Consents.Add(record.Copy());
        db.Events.Add(granted);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Consents.AnyAsync(x => x.Id == id, ct);
    }

    public async Task<ConsentRecord?> FindAsync(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Consents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<IReadOnlyList<ConsentRecord>> ForSubjectAsync(Guid subjectUserId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Consents.AsNoTracking().Where(x => x.SubjectUserId == subjectUserId).ToListAsync(ct);
    }

    public async Task<ConsentRecord?> RevokeAsync(Guid id, DateTimeOffset at, ConsentEventRecord revoked, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rows = await db.Consents.Where(x => x.Id == id && x.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, at), ct);
        if (rows == 1)
        {
            db.Events.Add(revoked);
            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);
        return await db.Consents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<IReadOnlyList<ConsentEventRecord>> EventsAsync(Guid subjectUserId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Events.AsNoTracking().Where(x => x.SubjectUserId == subjectUserId).ToListAsync(ct);
    }
}
