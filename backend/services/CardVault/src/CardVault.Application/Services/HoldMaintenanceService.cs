using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Application.Services;

public sealed class HoldMaintenanceService
{
    private readonly CardVaultDbContext _db;
    private readonly AuditService _audit;

    public HoldMaintenanceService(CardVaultDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <summary>
    /// Expires Active and PartiallyCaptured holds whose ExpiresOn is at or before <paramref name="now"/>.
    /// The pending amount is released through the hold status; the ledger only receives a shadow
    /// <see cref="LedgerEntryType.AuthorizationHold"/> entry that nets the original hold entry to zero,
    /// so the posted balance is never affected. Returns the number of holds expired.
    /// </summary>
    public async Task<int> ExpireDueHoldsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var due = await _db.AuthorizationHolds
            .Where(x => (x.Status == HoldStatus.Active || x.Status == HoldStatus.PartiallyCaptured)
                        && x.ExpiresOn <= now)
            .Take(500)
            .ToListAsync(ct);

        if (due.Count == 0) return 0;

        foreach (var h in due)
        {
            _db.LedgerEntries.Add(new LedgerEntryEntity
            {
                Id = Guid.NewGuid(),
                AccountId = h.AccountId,
                Type = LedgerEntryType.AuthorizationHold,
                Amount = -Math.Abs(h.Amount - h.CapturedAmount),
                Description = $"HOLD EXPIRED {h.Network} STAN:{h.Stan} RRN:{h.Rrn}",
                PostedOn = now,
                StatementId = null
            });

            h.Status = HoldStatus.Expired;
            h.ReleasedOn = now;
        }

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("holds.expired",
            new { count = due.Count },
            correlationId: null,
            traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(),
            ct: ct);

        return due.Count;
    }
}
