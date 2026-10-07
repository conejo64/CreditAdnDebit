using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Application.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CardVault.Application.Services;

public sealed class HoldService
{

    private static string MapResponseCode(string? reason)
        => HoldResponseCodeCalculator.MapResponseCode(reason);


    private readonly CardVaultDbContext _db;
    private readonly AuditService _audit;
    private readonly IServiceProvider _sp;

    public HoldService(CardVaultDbContext db, AuditService audit, IServiceProvider sp)
    {
        _db = db;
        _audit = audit;
        _sp = sp;
    }

    /// <summary>
    /// Authorizes an amount against an account and places the hold.
    /// <para>
    /// On a relational provider the idempotency lookup, the risk decision (which reads available
    /// credit) and the hold insert run inside one transaction that first takes a row lock on the
    /// account (<c>SELECT ... FOR UPDATE</c>). Two authorizations racing on the same account are
    /// therefore serialized: the second one waits, then reads the first hold and is declined when
    /// the sum exceeds the limit. A row lock was chosen over a serializable transaction because it
    /// is deterministic: no <c>40001</c> retry loop and no false conflicts from unrelated accounts.
    /// </para>
    /// <para>
    /// The InMemory provider used by the unit tests has neither transactions nor raw SQL, so the
    /// same steps run unlocked there; behaviour is otherwise unchanged.
    /// </para>
    /// </summary>
    public async Task<AuthorizationHoldEntity> AuthorizeAsync(Guid accountId, Guid? cardId, string network, string mti, string stan, string rrn, string? ode90, string? merchantId, string? mcc, string? countryCode, string? pinBlock, decimal amount, DateTimeOffset postedOn, CancellationToken ct)
    {
        var risk = _sp.GetRequiredService<RiskDecisionService>();
        var available = _sp.GetRequiredService<AvailableCreditService>();

        AuthorizationOutcome outcome;
        if (_db.Database.IsRelational() && _db.Database.CurrentTransaction is null)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            await LockAccountRowAsync(accountId, ct);
            outcome = await DecideAndPlaceHoldAsync(risk, available, accountId, cardId, network, mti, stan, rrn, ode90, merchantId, mcc, countryCode, pinBlock, amount, postedOn, ct);
            // Committing on a decline writes nothing; it only releases the account lock before the
            // decline is published and audited outside the transaction.
            await tx.CommitAsync(ct);
        }
        else
        {
            outcome = await DecideAndPlaceHoldAsync(risk, available, accountId, cardId, network, mti, stan, rrn, ode90, merchantId, mcc, countryCode, pinBlock, amount, postedOn, ct);
        }

        if (outcome.IsReplay) return outcome.Hold!;

        var decision = outcome.Decision;
        if (!decision.Approved)
        {
            var pub = _sp.GetRequiredService<IAuthDecisionPublisher>();
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

        var hold = outcome.Hold!;

        await _audit.WriteAsync("holds.auth.approved",
            new { accountId, network, mti, stan, rrn, ode90, merchantId = hold.MerchantId, mcc = hold.MerchantCategory, amount },
            correlationId: null,
            traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(),
            ct: ct);

        if (decision.Reason == "OVERLIMIT_ALLOWED" && outcome.AvailableBefore is not null)
        {
            var limits = _sp.GetRequiredService<CreditLimitManagementService>();
            await limits.RecordOverlimitAsync(accountId, hold.Id, Math.Abs(amount), outcome.AvailableBefore.AvailableCredit, ct);
        }

        return hold;
    }

    private sealed record AuthorizationOutcome(
        bool IsReplay,
        RiskDecisionService.RiskDecision Decision,
        AuthorizationHoldEntity? Hold,
        AvailableCreditService.AvailableCreditResult? AvailableBefore);

    /// <summary>
    /// Serializes authorizations per account: blocks until any other transaction holding this
    /// account row commits or rolls back. Relational providers only.
    /// </summary>
    private Task LockAccountRowAsync(Guid accountId, CancellationToken ct)
        => _db.Database.ExecuteSqlAsync($"SELECT 1 FROM \"Accounts\" WHERE \"Id\" = {accountId} FOR UPDATE", ct);

    /// <summary>
    /// The critical section: idempotency lookup, risk decision over the current available credit,
    /// and hold placement. Runs under the account row lock on relational providers.
    /// </summary>
    private async Task<AuthorizationOutcome> DecideAndPlaceHoldAsync(RiskDecisionService risk, AvailableCreditService available, Guid accountId, Guid? cardId, string network, string mti, string stan, string rrn, string? ode90, string? merchantId, string? mcc, string? countryCode, string? pinBlock, decimal amount, DateTimeOffset postedOn, CancellationToken ct)
    {
        var existing = await _db.AuthorizationHolds.FirstOrDefaultAsync(x =>
            x.AccountId == accountId && x.Network == network && x.Stan == stan && x.Rrn == rrn, ct);

        if (existing is not null) return new(true, new(true, "REPLAY"), existing, null);

        // v44 - risk decision (card/account state / MCC / available credit / policy / PIN)
        var decision = await risk.DecideAuthAsync(accountId, cardId, Math.Abs(amount), mcc, countryCode, pinBlock, ct);
        if (!decision.Approved) return new(false, decision, null, null);

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

        return new(false, decision, hold, availableBefore);
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

        // Release the remaining pending amount. The hold is a shadow item, so the release is a
        // negative AuthorizationHold shadow entry (excluded from the posted balance), never a Reversal.
        _db.LedgerEntries.Add(new LedgerEntryEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = LedgerEntryType.AuthorizationHold,
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

