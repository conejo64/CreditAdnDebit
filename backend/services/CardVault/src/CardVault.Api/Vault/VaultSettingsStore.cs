using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Vault;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Api.Vault;

public sealed class VaultSettingsStore
{
    private readonly CardVaultDbContext _db;

    public VaultSettingsStore(CardVaultDbContext db)
    {
        _db = db;
    }

    public async Task<VaultSettingsEntity> GetAsync(CancellationToken ct)
    {
        var s = await _db.VaultSettings.OrderBy(x => x.UpdatedOn).FirstOrDefaultAsync(ct);
        if (s is not null) return s;

        // create singleton row
        s = new VaultSettingsEntity
        {
            Id = Guid.NewGuid(),
            ActiveKeyId = "k1",
            UpdatedOn = DateTimeOffset.UtcNow,
            LastReencryptUpdated = 0
        };
        _db.VaultSettings.Add(s);
        await _db.SaveChangesAsync(ct);
        return s;
    }

    public async Task<string> GetActiveKeyIdAsync(CancellationToken ct)
    {
        var s = await GetAsync(ct);
        return s.ActiveKeyId;
    }

    public async Task<VaultSettingsEntity> SetActiveKeyIdAsync(string keyId, string actor, CancellationToken ct)
    {
        var s = await GetAsync(ct);
        s.ActiveKeyId = keyId;
        s.UpdatedOn = DateTimeOffset.UtcNow;
        s.LastReencryptStatus = "rotated";
        await _db.SaveChangesAsync(ct);
        return s;
    }

    public async Task UpdateReencryptStateAsync(string status, int updated, CancellationToken ct)
    {
        var s = await GetAsync(ct);
        s.LastReencryptRunOn = DateTimeOffset.UtcNow;
        s.LastReencryptUpdated = updated;
        s.LastReencryptStatus = status;
        s.UpdatedOn = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}