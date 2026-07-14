using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Issuer;
using Konscious.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CardVault.Application.Services;

/// <summary>
/// SEC-02: salted, cost-parameterized PIN hashing (Argon2id interim control).
/// New PINs are always hashed with Argon2id. Legacy unsalted-SHA-256 records are
/// transparently upgraded to Argon2id on the next successful verify
/// (verify-then-upgrade — see design.md SEC-02).
/// </summary>
public sealed class PinService
{
    private readonly CardVaultDbContext _db;
    private readonly AuditService _audit;
    private const int MaxPinRetries = 3;
    private const int PinBlockMinutes = 60;

    private const string Argon2idAlgorithm = "argon2id";
    private const int Argon2MemoryKib = 19456;
    private const int Argon2Iterations = 2;
    private const int Argon2Parallelism = 1;
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;

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

        var isValid = card.PinHashAlgorithm == Argon2idAlgorithm
            ? VerifyArgon2id(pin, card.PinHash, card.PinSalt)
            : VerifyLegacySha256(pin, card.PinHash);

        if (isValid)
        {
            // Verify-then-upgrade: a successful legacy verify transparently re-hashes the
            // same PIN with Argon2id and overwrites the old unsalted hash in this same
            // SaveChangesAsync, so no card remains verifiable only by unsalted SHA-256.
            if (card.PinHashAlgorithm != Argon2idAlgorithm)
            {
                WriteArgon2idHash(card, pin);
            }

            // Reset retries if valid
            if (card.PinRetryCount > 0)
            {
                card.PinRetryCount = 0;
                card.PinBlockedUntil = null;
            }

            await _db.SaveChangesAsync(ct);
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

        WriteArgon2idHash(card, pin);
        card.PinRetryCount = 0;
        card.PinBlockedUntil = null;

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("security.pin.set", new { cardId }, null, null, ct);
    }

    /// <summary>
    /// Hashes <paramref name="pin"/> with Argon2id using a fresh random salt and writes
    /// algorithm id + cost params + salt + hash onto <paramref name="card"/>. Never logs
    /// the PIN or any encoding of it.
    /// </summary>
    private static void WriteArgon2idHash(CardEntity card, string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = HashPinArgon2id(pin, salt);

        card.PinHashAlgorithm = Argon2idAlgorithm;
        card.PinSalt = Convert.ToBase64String(salt);
        card.PinHashParams = JsonSerializer.Serialize(new { m = Argon2MemoryKib, t = Argon2Iterations, p = Argon2Parallelism });
        card.PinHash = Convert.ToBase64String(hash);
    }

    private static byte[] HashPinArgon2id(string pin, byte[] salt)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(pin))
        {
            Salt = salt,
            DegreeOfParallelism = Argon2Parallelism,
            Iterations = Argon2Iterations,
            MemorySize = Argon2MemoryKib
        };

        return argon2.GetBytes(HashSizeBytes);
    }

    private static bool VerifyArgon2id(string pin, string storedHashBase64, string? storedSaltBase64)
    {
        if (storedSaltBase64 is null) return false;

        var salt = Convert.FromBase64String(storedSaltBase64);
        var computedHash = HashPinArgon2id(pin, salt);
        var storedHash = Convert.FromBase64String(storedHashBase64);

        return CryptographicOperations.FixedTimeEquals(computedHash, storedHash);
    }

    /// <summary>
    /// Preserves the exact legacy unsalted-SHA-256 comparison. Used ONLY on the
    /// verify-then-upgrade path — never to write new hashes.
    /// </summary>
    private static bool VerifyLegacySha256(string pin, string storedHash)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(pin));
        var computed = Convert.ToBase64String(bytes);
        return storedHash == computed;
    }
}
