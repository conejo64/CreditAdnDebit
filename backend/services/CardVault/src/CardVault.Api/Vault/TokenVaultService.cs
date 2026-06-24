using BuildingBlocks.Kafka;
using BuildingBlocks.Outbox;
using CardVault.Api.Pci;
using CardVault.Application.Ports;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Outbox;
using CardVault.Infrastructure.Persistence.Vault;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;

namespace CardVault.Api.Vault;

public sealed class TokenVaultService : IPanVault
{
    private readonly VaultSettingsStore _settings;
    private readonly VaultOptions _vaultOptions;
    private readonly PciOptions _pci;
    private readonly PciAuditPublisher _pciAudit;
    private readonly CardVaultDbContext _db;
    private readonly VaultCrypto _crypto;
    private readonly IEventBus _bus;

    public TokenVaultService(CardVaultDbContext db, VaultCrypto crypto, IEventBus bus, VaultSettingsStore settings, VaultOptions vaultOptions, PciOptions pci, PciAuditPublisher pciAudit)
    {
        _db = db;
        _crypto = crypto;
        _bus = bus;
        _settings = settings;
        _vaultOptions = vaultOptions;
        _pci = pci;
        _pciAudit = pciAudit;
    }

    public sealed record SensitiveCardPayload(string Pan, string? ExpiryYyMm);

    public async Task<TokenizeResponse> TokenizeAsync(TokenizeRequest req, string actor, string? traceId, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = Observability.ActivitySource.StartActivity("vault.tokenize");
        activity?.SetTag("traceId", traceId);
        var outcome = "ok";
        try
        {
            var token = "tok_" + Base64Url(RandomNumberGenerator.GetBytes(18));
            var bin = req.Pan.Length >= 6 ? req.Pan[..6] : null;
            var masked = PciMasker.MaskPan(req.Pan, _pci);

            var activeKeyId = await _settings.GetActiveKeyIdAsync(ct);
            _crypto.SetActiveKeyId(activeKeyId);

            var parts = _crypto.EncryptToParts(new SensitiveCardPayload(req.Pan, req.ExpiryYyMm));

            var e = new TokenVaultEntryEntity
            {
                Id = Guid.NewGuid(),
                Token = token,
                KeyId = parts.keyId,
                NonceB64 = parts.nonceB64,
                CiphertextB64 = parts.cipherB64,
                TagB64 = parts.tagB64,
                MaskedPan = masked,
                Bin = bin,
                CreatedOn = DateTimeOffset.UtcNow
            };

            _db.TokenVault.Add(e);
            await _db.SaveChangesAsync(ct);

            await _bus.PublishAsync("sw.cardvault.audit", token, JsonSerializer.Serialize(new
            {
                type = "cardvault.tokenize",
                token,
                maskedPan = masked,
                bin,
                keyId = e.KeyId,
                actor,
                at = DateTimeOffset.UtcNow
            }), ct);

            await _pciAudit.PublishAsync("pci.tokenize", token, new { token, maskedPan = masked, bin, keyId = e.KeyId, actor, traceId }, ct);

            return new TokenizeResponse(token, masked, bin, e.KeyId, e.CreatedOn);
        }
        catch
        {
            outcome = "error";
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            sw.Stop();
            Observability.VaultOperationsTotal.Add(1, new KeyValuePair<string, object?>("op", "tokenize"), new KeyValuePair<string, object?>("outcome", outcome));
            Observability.VaultOperationDurationMs.Record(sw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("op", "tokenize"), new KeyValuePair<string, object?>("outcome", outcome));
            activity?.SetTag("duration_ms", sw.Elapsed.TotalMilliseconds);
        }
    }

    public async Task<DetokenizeResponse> DetokenizeAsync(string token, string actor, string? traceId, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = Observability.ActivitySource.StartActivity("vault.detokenize");
        activity?.SetTag("traceId", traceId);
        var outcome = "ok";
        try
        {
            var e = await _db.TokenVault.FirstOrDefaultAsync(x => x.Token == token, ct)
                ?? throw new InvalidOperationException("Token not found");

            var payload = _crypto.DecryptFromParts<SensitiveCardPayload>(e.KeyId, e.NonceB64, e.CiphertextB64, e.TagB64);

            e.LastAccessedOn = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            await _bus.PublishAsync("sw.cardvault.audit", token, JsonSerializer.Serialize(new
            {
                type = "cardvault.detokenize",
                token,
                maskedPan = e.MaskedPan,
                keyId = e.KeyId,
                actor,
                at = DateTimeOffset.UtcNow
            }), ct);

            await _pciAudit.PublishAsync("pci.detokenize", token, new { token, maskedPan = e.MaskedPan, keyId = e.KeyId, actor, traceId }, ct);

            return new DetokenizeResponse(token, payload.Pan, payload.ExpiryYyMm, e.KeyId);
        }
        catch
        {
            outcome = "error";
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            sw.Stop();
            Observability.VaultOperationsTotal.Add(1, new KeyValuePair<string, object?>("op", "detokenize"), new KeyValuePair<string, object?>("outcome", outcome));
            Observability.VaultOperationDurationMs.Record(sw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("op", "detokenize"), new KeyValuePair<string, object?>("outcome", outcome));
            activity?.SetTag("duration_ms", sw.Elapsed.TotalMilliseconds);
        }
    }

    public async Task<TokenMetadataResponse> GetMetadataAsync(string token, CancellationToken ct)
    {
        var e = await _db.TokenVault.AsNoTracking().FirstOrDefaultAsync(x => x.Token == token, ct)
            ?? throw new InvalidOperationException("Token not found");

        return new TokenMetadataResponse(e.Token, e.MaskedPan, e.Bin, e.KeyId, e.CreatedOn, e.LastAccessedOn);
    }

    
    public async Task<RotateKeyResponse> RotateActiveKeyAsync(string newActiveKeyId, string actor, string? traceId, CancellationToken ct)
    {
        var rotatedOn = DateTimeOffset.UtcNow;

        // Resolve (or cold-start-create) the VaultSettings singleton first.
        // On a cold-start path, GetAsync internally calls SaveChangesAsync to create the row.
        // By calling GetAsync BEFORE staging the outbox row we guarantee the outbox row is
        // NOT included in that cold-start save — so both the key mutation and the outbox row
        // can be committed together in a single subsequent SaveChangesAsync, satisfying the
        // spec atomicity requirement regardless of whether settings pre-existed.
        var s = await _settings.GetAsync(ct);

        // Mutate the settings entity (tracked by _db) — no save yet.
        s.ActiveKeyId = newActiveKeyId;
        s.UpdatedOn   = DateTimeOffset.UtcNow;
        s.LastReencryptStatus = "rotated";

        // Stage the audit outbox row in the SAME change tracker as the key mutation.
        _db.OutboxMessages.Add(new OutboxMessageEntity
        {
            Topic       = "sw.cardvault.audit",
            Key         = "vault",
            PayloadJson = JsonSerializer.Serialize(new
            {
                type      = "cardvault.vault.rotate",
                actor,
                keyId     = newActiveKeyId,
                traceId,
                rotatedAt = rotatedOn
            })
        });

        // Single SaveChangesAsync: commits the key change and the outbox row atomically.
        await _db.SaveChangesAsync(ct);

        _crypto.SetActiveKeyId(newActiveKeyId);

        return new RotateKeyResponse(_crypto.ActiveKeyId, rotatedOn, actor);
    }

    /// <summary>
    /// Re-encrypts existing vault entries to the currently active key.
    /// Batch operation for controlled migration after a rotation.
    /// </summary>
    public async Task<ReEncryptBatchResponse> ReEncryptBatchAsync(int take, string actor, string? traceId, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, 500);

        var active = await _settings.GetActiveKeyIdAsync(ct);
        _crypto.SetActiveKeyId(active);
        var candidates = await _db.TokenVault
            .Where(x => x.KeyId != active)
            .OrderBy(x => x.CreatedOn)
            .Take(take)
            .ToListAsync(ct);

        var updated = 0;

        foreach (var e in candidates)
        {
            var payload = _crypto.DecryptFromParts<SensitiveCardPayload>(e.KeyId, e.NonceB64, e.CiphertextB64, e.TagB64);

            var parts = _crypto.EncryptToParts(payload); // uses active key
            e.KeyId = parts.keyId;
            e.NonceB64 = parts.nonceB64;
            e.CiphertextB64 = parts.cipherB64;
            e.TagB64 = parts.tagB64;

            updated++;
        }

        if (updated > 0)
        {
            // Add the audit outbox row inside the same SaveChangesAsync as the re-encrypted entries
            // so audit persistence is atomic with the state change (spec: outbox durability).
            _db.OutboxMessages.Add(new OutboxMessageEntity
            {
                Topic       = "sw.cardvault.audit",
                Key         = "vault",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    type            = "cardvault.reencrypt.batch",
                    actor,
                    traceId,
                    recordsAffected = updated,
                    completedAt     = DateTimeOffset.UtcNow
                })
            });
            await _db.SaveChangesAsync(ct);
        }

        await _settings.UpdateReencryptStateAsync(updated > 0 ? "completed" : "noop", updated, ct);

        return new ReEncryptBatchResponse(active, updated, DateTimeOffset.UtcNow);
    }

// IPanVault port implementation — maps internal records to Application port types
    async Task<TokenizeResult> IPanVault.TokenizeAsync(string pan, string? expiryYyMm, string actor, string? traceId, CancellationToken ct)
    {
        var r = await TokenizeAsync(new TokenizeRequest(pan, expiryYyMm), actor, traceId, ct);
        return new TokenizeResult(r.Token, r.MaskedPan, r.Bin, r.KeyId, r.CreatedOn);
    }

    async Task<DetokenizeResult> IPanVault.DetokenizeAsync(string token, string actor, string? traceId, CancellationToken ct)
    {
        var r = await DetokenizeAsync(token, actor, traceId, ct);
        return new DetokenizeResult(r.Token, r.Pan, r.ExpiryYyMm, r.KeyId);
    }

    async Task<TokenMetadataResult> IPanVault.GetMetadataAsync(string token, CancellationToken ct)
    {
        var r = await GetMetadataAsync(token, ct);
        return new TokenMetadataResult(r.Token, r.MaskedPan, r.Bin, r.KeyId, r.CreatedOn, r.LastAccessedOn);
    }

    async Task<RotateKeyResult> IPanVault.RotateActiveKeyAsync(string newActiveKeyId, string actor, string? traceId, CancellationToken ct)
    {
        var r = await RotateActiveKeyAsync(newActiveKeyId, actor, traceId, ct);
        return new RotateKeyResult(r.ActiveKeyId, r.RotatedOn, r.Actor);
    }

    async Task<ReEncryptBatchResult> IPanVault.ReEncryptBatchAsync(int take, string actor, string? traceId, CancellationToken ct)
    {
        var r = await ReEncryptBatchAsync(take, actor, traceId, ct);
        return new ReEncryptBatchResult(r.ActiveKeyId, r.UpdatedCount, r.RotatedOn);
    }

    async Task<(string ActiveKeyId, IReadOnlyList<string> AvailableKeyIds)> IPanVault.GetActiveKeyInfoAsync(CancellationToken ct)
    {
        var active = await _settings.GetActiveKeyIdAsync(ct);
        var available = _vaultOptions.Keys.Keys.OrderBy(k => k).ToList();
        return (active, available);
    }

private static string Mask(string pan)
    {
        if (pan.Length < 10) return "****";
        var first6 = pan[..6];
        var last4 = pan[^4..];
        return $"{first6}******{last4}";
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record TokenizeRequest(string Pan, string? ExpiryYyMm);
public sealed record TokenizeResponse(string Token, string MaskedPan, string? Bin, string KeyId, DateTimeOffset CreatedOn);

public sealed record DetokenizeResponse(string Token, string Pan, string? ExpiryYyMm, string KeyId);

public sealed record TokenMetadataResponse(string Token, string? MaskedPan, string? Bin, string KeyId, DateTimeOffset CreatedOn, DateTimeOffset? LastAccessedOn);

public sealed record RotateKeyResponse(string ActiveKeyId, DateTimeOffset RotatedOn, string Actor);
public sealed record ReEncryptBatchResponse(string ActiveKeyId, int UpdatedCount, DateTimeOffset RotatedOn);