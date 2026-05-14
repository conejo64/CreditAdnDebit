using System.Diagnostics;
using System.Text.Json;
using CardVault.Api.Contracts;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.CreditLimits;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Api.Services;

public sealed class CreditLimitManagementService
{
    private const string ProposalTopic = "cardvault.creditlimit.proposal.changed";
    private const string OverlimitTopic = "cardvault.creditlimit.overlimit.recorded";

    private readonly CardVaultDbContext _db;
    private readonly CreditPolicyService _policies;
    private readonly AuditService _audit;

    public CreditLimitManagementService(CardVaultDbContext db, CreditPolicyService policies, AuditService audit)
    {
        _db = db;
        _policies = policies;
        _audit = audit;
    }

    public async Task<OverlimitEventView?> RecordOverlimitAsync(Guid accountId, Guid? holdId, decimal approvedAmount, decimal availableCreditBefore, CancellationToken ct)
    {
        var overlimitAmount = Math.Max(0m, approvedAmount - availableCreditBefore);
        if (overlimitAmount <= 0m)
            return null;

        var existing = await _db.OverlimitEvents.AsNoTracking()
            .FirstOrDefaultAsync(x => x.HoldId == holdId && holdId != null, ct);
        if (existing is not null)
            return Map(existing);

        var entity = new OverlimitEventEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            HoldId = holdId,
            ApprovedAmount = approvedAmount,
            AvailableCreditBefore = availableCreditBefore,
            OverlimitAmount = overlimitAmount,
            TraceId = Activity.Current?.TraceId.ToString(),
            CreatedOn = DateTimeOffset.UtcNow
        };

        _db.OverlimitEvents.Add(entity);
        _db.OutboxMessages.Add(new OutboxMessageEntity
        {
            Topic = OverlimitTopic,
            Key = entity.Id.ToString("N"),
            PayloadJson = JsonSerializer.Serialize(new
            {
                overlimitEventId = entity.Id,
                accountId = entity.AccountId,
                holdId = entity.HoldId,
                approvedAmount = entity.ApprovedAmount,
                availableCreditBefore = entity.AvailableCreditBefore,
                overlimitAmount = entity.OverlimitAmount,
                traceId = entity.TraceId
            })
        });

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("creditlimit.overlimit.recorded", new
        {
            overlimitEventId = entity.Id,
            accountId = entity.AccountId,
            holdId = entity.HoldId,
            approvedAmount = entity.ApprovedAmount,
            availableCreditBefore = entity.AvailableCreditBefore,
            overlimitAmount = entity.OverlimitAmount
        }, entity.Id.ToString("N"), entity.TraceId, ct);

        return Map(entity);
    }

    public async Task<IReadOnlyList<OverlimitEventView>> GetOverlimitEventsAsync(Guid accountId, int take, CancellationToken ct)
    {
        var limit = take <= 0 ? 50 : Math.Min(take, 200);
        var items = await _db.OverlimitEvents.AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .OrderByDescending(x => x.CreatedOn)
            .Take(limit)
            .ToListAsync(ct);

        return items.Select(Map).ToList();
    }

    public async Task<CreditLimitEvaluationView> EvaluateAsync(Guid accountId, CancellationToken ct)
    {
        var account = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == accountId, ct)
            ?? throw new InvalidOperationException("Account not found.");
        if (account.AccountType != AccountType.Credit)
            throw new InvalidOperationException("Only credit accounts support limit proposals.");

        var policy = await _policies.GetOrDefaultAsync(account.ProductCode, ct);
        var statements = await _db.Statements.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.DueDate.Date < DateTime.UtcNow.Date)
            .OrderByDescending(x => x.StatementDate)
            .Take(Math.Max(policy.AutoIncreaseMinStatements, 6))
            .ToListAsync(ct);

        if (statements.Count < policy.AutoIncreaseMinStatements)
        {
            return new CreditLimitEvaluationView(
                accountId,
                false,
                account.CreditLimit,
                0m,
                0m,
                statements.Count,
                "INSUFFICIENT_STATEMENT_HISTORY",
                null);
        }

        var onTimeCount = statements.Count(x => x.PaidAmount >= x.MinimumPayment);
        var onTimeRatio = statements.Count == 0 ? 0m : decimal.Round((decimal)onTimeCount / statements.Count, 4);
        var avgUtilization = account.CreditLimit <= 0m
            ? 0m
            : decimal.Round(statements.Average(x => x.NewBalance <= 0m ? 0m : Math.Min(1m, x.NewBalance / account.CreditLimit)), 4);

        var eligible = onTimeRatio >= policy.AutoIncreaseMinOnTimeRatio &&
                       avgUtilization >= policy.AutoIncreaseMinUtilization &&
                       policy.AutoIncreasePercent > 0m;
        var reason = eligible
            ? "ELIGIBLE_FOR_INCREASE"
            : onTimeRatio < policy.AutoIncreaseMinOnTimeRatio
                ? "PAYMENT_BEHAVIOR_BELOW_THRESHOLD"
                : avgUtilization < policy.AutoIncreaseMinUtilization
                    ? "UTILIZATION_BELOW_THRESHOLD"
                    : "POLICY_NOT_ELIGIBLE";

        CreditLimitProposalView? proposalView = null;
        if (eligible)
        {
            var existing = await _db.CreditLimitProposals.FirstOrDefaultAsync(x =>
                x.AccountId == accountId &&
                x.Status == CreditLimitProposalStatus.Proposed, ct);

            if (existing is null)
            {
                var increaseAmount = Math.Round(account.CreditLimit * policy.AutoIncreasePercent, 2, MidpointRounding.AwayFromZero);
                if (increaseAmount <= 0m)
                    increaseAmount = 100m;

                existing = new CreditLimitProposalEntity
                {
                    Id = Guid.NewGuid(),
                    AccountId = accountId,
                    CurrentLimit = account.CreditLimit,
                    ProposedIncreaseAmount = increaseAmount,
                    ProposedLimit = account.CreditLimit + increaseAmount,
                    OnTimePaymentRatio = onTimeRatio,
                    AverageUtilizationRatio = avgUtilization,
                    StatementsReviewed = statements.Count,
                    DecisionReason = reason,
                    Status = CreditLimitProposalStatus.Proposed,
                    CreatedOn = DateTimeOffset.UtcNow
                };

                _db.CreditLimitProposals.Add(existing);
                _db.OutboxMessages.Add(new OutboxMessageEntity
                {
                    Topic = ProposalTopic,
                    Key = existing.Id.ToString("N"),
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        proposalId = existing.Id,
                        accountId = existing.AccountId,
                        currentLimit = existing.CurrentLimit,
                        proposedIncreaseAmount = existing.ProposedIncreaseAmount,
                        proposedLimit = existing.ProposedLimit,
                        status = existing.Status.ToString(),
                        decisionReason = existing.DecisionReason,
                        traceId = Activity.Current?.TraceId.ToString()
                    })
                });

                await _db.SaveChangesAsync(ct);
                await _audit.WriteAsync("creditlimit.proposal.created", new
                {
                    proposalId = existing.Id,
                    accountId = existing.AccountId,
                    currentLimit = existing.CurrentLimit,
                    proposedIncreaseAmount = existing.ProposedIncreaseAmount,
                    proposedLimit = existing.ProposedLimit,
                    onTimePaymentRatio = existing.OnTimePaymentRatio,
                    averageUtilizationRatio = existing.AverageUtilizationRatio,
                    statementsReviewed = existing.StatementsReviewed
                }, existing.Id.ToString("N"), Activity.Current?.TraceId.ToString(), ct);
            }

            proposalView = Map(existing);
        }

        return new CreditLimitEvaluationView(
            accountId,
            eligible,
            account.CreditLimit,
            onTimeRatio,
            avgUtilization,
            statements.Count,
            reason,
            proposalView);
    }

    public async Task<IReadOnlyList<CreditLimitProposalView>> GetProposalsAsync(string? status, int take, CancellationToken ct)
    {
        var limit = take <= 0 ? 50 : Math.Min(take, 200);
        var query = _db.CreditLimitProposals.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<CreditLimitProposalStatus>(status, true, out var parsed))
            query = query.Where(x => x.Status == parsed);

        var items = await query.OrderByDescending(x => x.CreatedOn).Take(limit).ToListAsync(ct);
        return items.Select(Map).ToList();
    }

    public async Task<CreditLimitProposalView> ApplyProposalAsync(Guid proposalId, CancellationToken ct)
    {
        var proposal = await _db.CreditLimitProposals.FirstOrDefaultAsync(x => x.Id == proposalId, ct)
            ?? throw new InvalidOperationException("Proposal not found.");
        if (proposal.Status != CreditLimitProposalStatus.Proposed)
            throw new InvalidOperationException("Proposal is not pending.");

        var account = await _db.Accounts.FirstOrDefaultAsync(x => x.Id == proposal.AccountId, ct)
            ?? throw new InvalidOperationException("Account not found.");

        var delta = proposal.ProposedIncreaseAmount;
        account.CreditLimit += delta;
        account.AvailableLimit += delta;
        proposal.Status = CreditLimitProposalStatus.Applied;
        proposal.AppliedOn = DateTimeOffset.UtcNow;

        _db.OutboxMessages.Add(new OutboxMessageEntity
        {
            Topic = ProposalTopic,
            Key = proposal.Id.ToString("N"),
            PayloadJson = JsonSerializer.Serialize(new
            {
                proposalId = proposal.Id,
                accountId = proposal.AccountId,
                currentLimit = proposal.CurrentLimit,
                proposedLimit = proposal.ProposedLimit,
                appliedOn = proposal.AppliedOn,
                status = proposal.Status.ToString(),
                traceId = Activity.Current?.TraceId.ToString()
            })
        });

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("creditlimit.proposal.applied", new
        {
            proposalId = proposal.Id,
            accountId = proposal.AccountId,
            previousLimit = proposal.CurrentLimit,
            newLimit = account.CreditLimit,
            proposedIncreaseAmount = proposal.ProposedIncreaseAmount
        }, proposal.Id.ToString("N"), Activity.Current?.TraceId.ToString(), ct);

        return Map(proposal);
    }

    private static OverlimitEventView Map(OverlimitEventEntity entity)
        => new(entity.Id, entity.AccountId, entity.HoldId, entity.ApprovedAmount, entity.AvailableCreditBefore, entity.OverlimitAmount, entity.TraceId, entity.CreatedOn);

    private static CreditLimitProposalView Map(CreditLimitProposalEntity entity)
        => new(entity.Id, entity.AccountId, entity.CurrentLimit, entity.ProposedIncreaseAmount, entity.ProposedLimit, entity.OnTimePaymentRatio, entity.AverageUtilizationRatio, entity.StatementsReviewed, entity.Status.ToString(), entity.DecisionReason, entity.CreatedOn, entity.AppliedOn);
}
