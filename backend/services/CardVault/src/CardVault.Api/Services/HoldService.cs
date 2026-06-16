using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Issuer;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Api.Services;

public sealed class HoldService
{

    private static string MapResponseCode(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "05";

        // Common ISO8583 DE39 mapping (demo):
        // 00 Approved (handled elsewhere)
        // 51 Insufficient funds / credit
        // 65 Activity limit exceeded (velocity)
        // 59 Suspected fraud
        // 62 Restricted card (policy/MCC)
        var r = reason.Trim().ToUpperInvariant();

        if (r.Contains("INSUFFICIENT") || r.Contains("NO_FUNDS") || r.Contains("AVAILABLE_CREDIT")) return "51";
        if (r.StartsWith("VELOCITY") || r.Contains("VELOCITY")) return "65";
        if (r.StartsWith("FRAUD") || r.Contains("FRAUD") || r.Contains("RISK_SCORE")) return "59";
        if (r.StartsWith("MCC") || r.Contains("MCC") || r.Contains("RESTRICT") || r.Contains("BLOCKED")) return "62";

        return "05"; // Do not honor
    }


    private readonly CardVaultDbContext _db;
    private readonly AuditService _audit;
    private readonly IServiceProvider _sp;

    public HoldService(CardVaultDbContext db, AuditService audit, IServiceProvider sp)
    {
        _db = db;
        _audit = audit;
        _sp = sp;
    }

    public async Task<AuthorizationHoldEntity> AuthorizeAsync(Guid accountId, Guid? cardId, string network, string mti, string stan, string rrn, string? ode90, string? merchantId, string? mcc, string? countryCode, string? pinBlock, decimal amount, DateTimeOffset postedOn, CancellationToken ct)
    {
        var existing = await _db.AuthorizationHolds.FirstOrDefaultAsync(x =>
            x.AccountId == accountId && x.Network == network && x.Stan == stan && x.Rrn == rrn, ct);

        if (existing is not null) return existing;

        
        // v44 - risk decision (MCC / available credit / policy / PIN)
        var risk = _sp.GetRequiredService<RiskDecisionService>();
        var available = _sp.GetRequiredService<AvailableCreditService>();
        var decision = await risk.DecideAuthAsync(accountId, cardId, Math.Abs(amount), mcc, countryCode, pinBlock, ct);
        if (!decision.Approved)
        {
            var pub = _sp.GetRequiredService<AuthDecisionPublisher>();
            await pub.PublishAuthResponseAsync(accountId.ToString("N"), new
            {
                accountId,
                network,
                mti,
                stan,
                rrn,
                merchantId,
                acceptorId = merchantId,
                mcc = mcc,
                amount,
                responseCode = MapResponseCode(decision.Reason),
                reason = decision.Reason,
                currency = "840"
            }, ct);

            await _audit.WriteAsync("risk.auth.declined", new { accountId, network, mti, stan, rrn, merchantId, mcc, amount, reason = decision.Reason }, null, System.Diagnostics.Activity.Current?.TraceId.ToString(), ct);
            throw new InvalidOperationException($"AUTH_DECLINED:{decision.Reason}");
        }

        var availableBefore = decision.Reason == "OVERLIMIT_ALLOWED"
            ? await available.GetAsync(accountId, ct)
            : null;

        // v43 - determine hold TTL by product policy
        var acct = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == accountId, ct);
        var ttlHours = 72;
        if (acct is not null)
        {
            var pol = await _db.CreditPolicies.AsNoTracking().FirstOrDefaultAsync(x => x.ProductCode == acct.ProductCode, ct);
            if (pol is not null && pol.HoldTtlHours > 0) ttlHours = pol.HoldTtlHours;
        }
        var expiresOn = postedOn.AddHours(ttlHours);

        // Post a hold entry (does not affect statement purchases; we track separately)
        var holdLedgerId = Guid.NewGuid();
        _db.LedgerEntries.Add(new LedgerEntryEntity
        {
            Id = holdLedgerId,
            AccountId = accountId,
            Type = LedgerEntryType.AuthorizationHold,
            Amount = Math.Abs(amount),
            Description = $"AUTH HOLD {network} MTI:{mti} STAN:{stan} RRN:{rrn}",
            PostedOn = postedOn,
            StatementId = null
        });

        var hold = new AuthorizationHoldEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Network = network,
            Stan = stan,
            Rrn = rrn,
            OriginalDataElements90 = ode90,
            MerchantId = merchantId,
            MerchantCategory = mcc,
            Amount = Math.Abs(amount),
            Status = HoldStatus.Active,
            AuthorizedOn = postedOn,
            ExpiresOn = expiresOn,
            HoldLedgerEntryId = holdLedgerId
        };

        _db.AuthorizationHolds.Add(hold);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("holds.auth.approved",
            new { accountId, network, mti, stan, rrn, ode90, merchantId = hold.MerchantId, mcc = hold.MerchantCategory, amount },
            correlationId: null,
            traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(),
            ct: ct);

        if (decision.Reason == "OVERLIMIT_ALLOWED" && availableBefore is not null)
        {
            var limits = _sp.GetRequiredService<CreditLimitManagementService>();
            await limits.RecordOverlimitAsync(accountId, hold.Id, Math.Abs(amount), availableBefore.AvailableCredit, ct);
        }

        return hold;
    }

    public async Task<AuthorizationHoldEntity?> CaptureAsync(Guid accountId, string network, string mti, string stan, string rrn, string? ode90, decimal amount, DateTimeOffset postedOn, CancellationToken ct)
    {
        // Match hold by STAN/RRN (demo) or ODE90 if provided
        var hold = await _db.AuthorizationHolds.FirstOrDefaultAsync(x =>
            x.AccountId == accountId && x.Network == network &&
            ((x.Stan == stan && x.Rrn == rrn) || (ode90 != null && x.OriginalDataElements90 == ode90)), ct);

        if (hold is null) return null;
        if (hold.Status != HoldStatus.Active && hold.Status != HoldStatus.PartiallyCaptured) return hold;

        // Post clearing purchase as PURCHASE or CLEARING (we'll use Clearing)
        var captureLedgerId = Guid.NewGuid();
        _db.LedgerEntries.Add(new LedgerEntryEntity
        {
            Id = captureLedgerId,
            AccountId = accountId,
            Type = LedgerEntryType.Clearing,
            Amount = Math.Abs(amount),
            Description = $"CLEARING {network} MTI:{mti} STAN:{stan} RRN:{rrn}",
            PostedOn = postedOn,
            StatementId = null
        });

        hold.CapturedAmount += Math.Abs(amount);
        if (hold.CapturedAmount >= hold.Amount)
        {
            hold.Status = HoldStatus.Captured;
            hold.CapturedOn = postedOn;
            hold.CaptureLedgerEntryId = captureLedgerId;
        }
        else
        {
            hold.Status = HoldStatus.PartiallyCaptured;
            hold.CapturedOn = postedOn;
            hold.CaptureLedgerEntryId = captureLedgerId;
        }

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("holds.clearing.captured",
            new { accountId, network, mti, stan, rrn, ode90, merchantId = hold.MerchantId, mcc = hold.MerchantCategory, amount },
            correlationId: null,
            traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(),
            ct: ct);

        return hold;
    }

    public async Task<AuthorizationHoldEntity?> ReleaseAsync(Guid accountId, string network, string mti, string stan, string rrn, string? ode90, DateTimeOffset postedOn, CancellationToken ct)
    {
        var hold = await _db.AuthorizationHolds.FirstOrDefaultAsync(x =>
            x.AccountId == accountId && x.Network == network &&
            ((x.Stan == stan && x.Rrn == rrn) || (ode90 != null && x.OriginalDataElements90 == ode90)), ct);

        if (hold is null) return null;
        if (hold.Status != HoldStatus.Active && hold.Status != HoldStatus.PartiallyCaptured) return hold;

        // Release by posting a reversal of the hold (negative hold)
        _db.LedgerEntries.Add(new LedgerEntryEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = LedgerEntryType.Reversal,
            Amount = -Math.Abs(hold.Amount - hold.CapturedAmount),
            Description = $"AUTH RELEASE {network} MTI:{mti} STAN:{stan} RRN:{rrn}",
            PostedOn = postedOn,
            StatementId = null
        });

        hold.Status = HoldStatus.Released;
        hold.ReleasedOn = postedOn;

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("holds.auth.released",
            new { accountId, network, mti, stan, rrn, ode90, amount = hold.Amount },
            correlationId: null,
            traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(),
            ct: ct);

        return hold;
    }
}

