using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Infrastructure.Persistence.Switch;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Application.Services;

public sealed class RiskDecisionService
{
    private readonly CardVaultDbContext _db;
    private readonly AvailableCreditService _available;
    private readonly PinService _pin;

    public RiskDecisionService(CardVaultDbContext db, AvailableCreditService available, PinService pin)
    {
        _db = db;
        _available = available;
        _pin = pin;
    }

    public sealed record RiskDecision(bool Approved, string Reason);

    public async Task<RiskDecision> DecideAuthAsync(Guid accountId, Guid? cardId, decimal amount, string? mcc, string? countryCode, string? pinBlock, CancellationToken ct)
    {
        // 0) Account and card state. Checked before anything else: a closed account or a blocked,
        //    cancelled, not-yet-active or expired card is never authorized, whatever the amount, and
        //    must not burn PIN retries or velocity counters on the way out.
        var acct = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == accountId, ct);
        if (acct is null) return new(false, "ACCOUNT_NOT_FOUND");

        var accountState = CheckAccountState(acct);
        if (accountState is not null) return accountState;

        // Card checks apply only when the caller identifies a card; card-less authorizations
        // (account-level flows) keep today's contract and are governed by the account state alone.
        if (cardId.HasValue)
        {
            var card = await _db.Cards.AsNoTracking().FirstOrDefaultAsync(x => x.Id == cardId.Value, ct);
            var cardState = CheckCardState(card, accountId, DateTimeOffset.UtcNow);
            if (cardState is not null) return cardState;
        }

        // 0b) PIN check (if pinBlock provided)
        if (cardId.HasValue && !string.IsNullOrWhiteSpace(pinBlock))
        {
            var pinOk = await _pin.VerifyPinAsync(cardId.Value, pinBlock, ct);
            if (!pinOk) return new(false, "INVALID_PIN");
        }

        // 1) Geography/Country block
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            var georule = await _db.AntifraudRules.AsNoTracking()
                .FirstOrDefaultAsync(x => x.IsEnabled && x.Type == AntifraudRuleType.BlockCountry && x.TargetValue == countryCode, ct);
            
            if (georule is not null) return new(false, $"COUNTRY_BLOCKED:{countryCode}");
        }

        // 2) MCC block/limit
        if (!string.IsNullOrWhiteSpace(mcc))
        {
            var rule = await _db.MccRules.AsNoTracking().FirstOrDefaultAsync(x => x.Mcc == mcc, ct);
            if (rule is not null)
            {
                if (rule.IsBlocked) return new(false, $"MCC_BLOCKED:{mcc}");
                if (rule.PerTxnLimit.HasValue && amount > rule.PerTxnLimit.Value) return new(false, $"MCC_PER_TXN_LIMIT:{mcc}");
            }
        }

        // 3) Available credit / policy
        var pol = await _db.CreditPolicies.AsNoTracking().FirstOrDefaultAsync(x => x.ProductCode == acct.ProductCode, ct);

        var available = await _available.GetAsync(accountId, ct);
        if (amount > available.AvailableCredit)
        {
            if (pol?.AllowOverlimit == true)
            {
                var buffer = Math.Max(0m, pol.OverlimitBufferAmount);
                if (amount <= available.AvailableCredit + buffer)
                    return new(true, "OVERLIMIT_ALLOWED");
            }
            return new(false, "INSUFFICIENT_AVAILABLE_CREDIT");
        }

        // 4) Velocity rules (per product)
        if (pol is not null)
        {
            var rules = await _db.VelocityRules.AsNoTracking()
                .Where(x => x.ProductCode == acct.ProductCode)
                .ToListAsync(ct);

            foreach (var rule in rules)
            {
                var from = DateTimeOffset.UtcNow.AddMinutes(-Math.Abs(rule.WindowMinutes));
                var q = _db.TxnJournal.AsNoTracking()
                    .Where(x => x.AccountId == accountId && x.PostedOn >= from && x.TxnType == CardVault.Infrastructure.Persistence.Switch.SwitchTxnType.Authorization);

                var cnt = await q.CountAsync(ct);
                if (cnt >= rule.MaxCount) return new(false, $"VELOCITY_MAX_COUNT:{rule.WindowMinutes}m");

                var sum = await q.SumAsync(x => x.Amount, ct);
                if (sum + amount > rule.MaxAmount) return new(false, $"VELOCITY_MAX_AMOUNT:{rule.WindowMinutes}m");
            }
        }

        // 5) Risk Score (Heuristic Demo)
        decimal currentRisk = 0;
        // Increase risk for high amount
        var floorLimit = pol != null && pol.FloorLimit > 0 ? pol.FloorLimit : 1000m;
        if (pol is not null && amount > floorLimit * 2) currentRisk += 30;
        // Increase risk for unusual hours (demo: between 1 and 4 AM)
        var hr = DateTime.UtcNow.Hour;
        if (hr >= 1 && hr <= 4) currentRisk += 20;

        // Check if there are country-based monitor multipliers
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            var mon = await _db.AntifraudRules.AsNoTracking()
                .Where(x => x.IsEnabled && x.Type == AntifraudRuleType.MonitorCountry && x.TargetValue == countryCode)
                .Select(x => x.RiskScore)
                .FirstOrDefaultAsync(ct);
            currentRisk += mon;
        }

        if (currentRisk >= 70) return new(false, $"FRAUD_SUSPECTED:Score={currentRisk}");

        // 6) Floor limit (placeholder for extra checks)
        if (pol is not null && pol.FloorLimit > 0 && amount >= pol.FloorLimit)
        {
            return new(true, "FLOOR_LIMIT_REVIEW");
        }

        return new(true, "OK");
    }

    /// <summary>
    /// Only an <see cref="AccountStatus.Active"/> account may authorize. Delinquent accounts are
    /// declined as well: no credit policy in the codebase grants them authorization, and
    /// <c>ThreeDsService</c> already treats every non-Active account as not transactable.
    /// </summary>
    private static RiskDecision? CheckAccountState(CardAccountEntity account) => account.Status switch
    {
        AccountStatus.Active => null,
        AccountStatus.Blocked => new(false, "ACCOUNT_BLOCKED"),
        AccountStatus.Closed => new(false, "ACCOUNT_CLOSED"),
        AccountStatus.Delinquent => new(false, "ACCOUNT_DELINQUENT"),
        _ => new(false, "ACCOUNT_NOT_ACTIVE")
    };

    /// <summary>
    /// The card must exist, belong to the account being charged, be <see cref="CardStatus.Active"/>
    /// and have a readable expiry that has not passed. An unreadable expiry fails closed.
    /// </summary>
    private static RiskDecision? CheckCardState(CardEntity? card, Guid accountId, DateTimeOffset asOf)
    {
        if (card is null) return new(false, "CARD_NOT_FOUND");
        if (card.AccountId != accountId) return new(false, "CARD_ACCOUNT_MISMATCH");

        switch (card.Status)
        {
            case CardStatus.Active:
                break;
            case CardStatus.Blocked:
                return new(false, "CARD_BLOCKED");
            case CardStatus.Cancelled:
                return new(false, "CARD_CANCELLED");
            case CardStatus.Expired:
                return new(false, "CARD_EXPIRED");
            default:
                // Created, Personalized, Printed, Delivered: issued but not yet activated.
                return new(false, "CARD_NOT_ACTIVE");
        }

        if (!CardExpiry.TryGetExpiredFrom(card.ExpiryYyMm, out var expiredFrom)) return new(false, "CARD_EXPIRY_INVALID");
        if (asOf >= expiredFrom) return new(false, "CARD_EXPIRED");

        return null;
    }
}

