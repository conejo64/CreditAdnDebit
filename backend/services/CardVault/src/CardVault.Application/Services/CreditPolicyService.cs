using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Application.Services;

public sealed class CreditPolicyService
{
    private readonly CardVaultDbContext _db;
    public CreditPolicyService(CardVaultDbContext db) => _db = db;

    public async Task<CreditPolicyEntity> UpsertAsync(CreditPolicyEntity policy, CancellationToken ct)
    {
        var existing = await _db.CreditPolicies.FirstOrDefaultAsync(x => x.ProductCode == policy.ProductCode, ct);
        if (existing is null)
        {
            policy.UpdatedOn = DateTimeOffset.UtcNow;
            _db.CreditPolicies.Add(policy);
        }
        else
        {
            existing.MinPaymentPercent = policy.MinPaymentPercent;
            existing.MinPaymentAbsolute = policy.MinPaymentAbsolute;
            existing.GraceDays = policy.GraceDays;
            existing.HoldTtlHours = policy.HoldTtlHours;
            existing.FloorLimit = policy.FloorLimit;
            existing.AllowOverlimit = policy.AllowOverlimit;
            existing.OverlimitBufferAmount = policy.OverlimitBufferAmount;
            existing.InterestApr = policy.InterestApr;
            existing.PurchaseApr = policy.PurchaseApr;
            existing.CashAdvanceApr = policy.CashAdvanceApr;
            existing.PenaltyApr = policy.PenaltyApr;
            existing.PurchaseGraceDays = policy.PurchaseGraceDays;
            existing.OverlimitFee = policy.OverlimitFee;
            existing.OverlimitFeeOncePerDay = policy.OverlimitFeeOncePerDay;
            existing.AutoIncreasePercent = policy.AutoIncreasePercent;
            existing.AutoIncreaseMinStatements = policy.AutoIncreaseMinStatements;
            existing.AutoIncreaseMinOnTimeRatio = policy.AutoIncreaseMinOnTimeRatio;
            existing.AutoIncreaseMinUtilization = policy.AutoIncreaseMinUtilization;
            existing.AnnualFee = policy.AnnualFee;
            existing.CashAdvanceFeeFixed = policy.CashAdvanceFeeFixed;
            existing.CashAdvanceFeePercent = policy.CashAdvanceFeePercent;
            existing.LateFee = policy.LateFee;
            existing.UpdatedOn = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return await _db.CreditPolicies.AsNoTracking().FirstAsync(x => x.ProductCode == policy.ProductCode, ct);
    }

    public Task<CreditPolicyEntity?> GetAsync(string productCode, CancellationToken ct) =>
        _db.CreditPolicies.AsNoTracking().FirstOrDefaultAsync(x => x.ProductCode == productCode, ct);

    public async Task<CreditPolicyEntity> GetOrDefaultAsync(string productCode, CancellationToken ct)
    {
        var p = await GetAsync(productCode, ct);
        if (p is not null) return p;

        // default fallback
        var def = new CreditPolicyEntity
        {
            ProductCode = productCode,
            MinPaymentPercent = 0.05m,
            MinPaymentAbsolute = 15m,
            GraceDays = 15,
            HoldTtlHours = 72,
            FloorLimit = 0m,
            AllowOverlimit = false,
            OverlimitBufferAmount = 0m,
            InterestApr = 0.35m,
            PurchaseApr = 0.35m,
            CashAdvanceApr = 0.45m,
            PenaltyApr = 0.55m,
            PurchaseGraceDays = 0,
            OverlimitFee = 15m,
            OverlimitFeeOncePerDay = true,
            AutoIncreasePercent = 0.10m,
            AutoIncreaseMinStatements = 3,
            AutoIncreaseMinOnTimeRatio = 1.00m,
            AutoIncreaseMinUtilization = 0.35m,
            AnnualFee = 0m,
            CashAdvanceFeeFixed = 5m,
            CashAdvanceFeePercent = 0.03m,
            LateFee = 10m,
            UpdatedOn = DateTimeOffset.UtcNow
        };
        _db.CreditPolicies.Add(def);
        await _db.SaveChangesAsync(ct);
        return def;
    }
}
