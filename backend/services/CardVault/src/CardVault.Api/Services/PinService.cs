using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Issuer;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace CardVault.Api.Services;

public sealed class PinService
{
    private readonly CardVaultDbContext _db;
    private readonly AuditService _audit;
    private const int MaxPinRetries = 3;
    private const int PinBlockMinutes = 60;

    public PinService(CardVaultDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<bool> VerifyPinAsync(Guid cardId, string pin, CancellationToken ct)
    {
        var card = await _db.Cards.FirstOrDefaultAsync(x => x.Id == cardId, ct);
        if (card is null || card.PinHash is null) return false;

        // Check if blocked by retries
        if (card.PinBlockedUntil.HasValue && card.PinBlockedUntil > DateTimeOffset.UtcNow)
        {
            await _audit.WriteAsync("security.pin.blocked_attempt", new { cardId }, null, null, ct);
            return false;
        }

        var incomingHash = HashPin(pin);
        if (card.PinHash == incomingHash)
        {
            // Reset retries if valid
            if (card.PinRetryCount > 0)
            {
                card.PinRetryCount = 0;
                card.PinBlockedUntil = null;
                await _db.SaveChangesAsync(ct);
            }
            return true;
        }

        // Invalid PIN: Increment retries
        card.PinRetryCount++;
        if (card.PinRetryCount >= MaxPinRetries)
        {
            card.PinBlockedUntil = DateTimeOffset.UtcNow.AddMinutes(PinBlockMinutes);
            await _audit.WriteAsync("security.pin.permanently_blocked", new { cardId, retries = card.PinRetryCount }, null, null, ct);
        }

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("security.pin.invalid_attempt", new { cardId, retry = card.PinRetryCount }, null, null, ct);
        return false;
    }

    public async Task SetPinAsync(Guid cardId, string pin, CancellationToken ct)
    {
        var card = await _db.Cards.FirstOrDefaultAsync(x => x.Id == cardId, ct)
            ?? throw new InvalidOperationException("Card not found");

        card.PinHash = HashPin(pin);
        card.PinRetryCount = 0;
        card.PinBlockedUntil = null;

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("security.pin.set", new { cardId }, null, null, ct);
    }

    private static string HashPin(string pin)
    {
        // Simple hash for demo (in prod use Salt + Arg2 or similar)
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(pin));
        return Convert.ToBase64String(bytes);
    }
}
