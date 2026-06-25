using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Issuer;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CardVault.Application.Features.Delinquency.Commands;

/// <summary>
/// Command dispatched once per day by <see cref="Background.DelinquencyEvaluationWorker"/>.
/// Pass <paramref name="ReferenceDate"/> as <c>DateTime.UtcNow.Date</c> in production,
/// or an arbitrary date in tests to simulate future/past evaluations.
/// </summary>
public record EvaluateDelinquencyCommand(DateTime ReferenceDate) : IRequest<Unit>;

public sealed class EvaluateDelinquencyCommandHandler : IRequestHandler<EvaluateDelinquencyCommand, Unit>
{
    private readonly CardVaultDbContext _db;
    private readonly ILogger<EvaluateDelinquencyCommandHandler>? _logger;

    public EvaluateDelinquencyCommandHandler(
        CardVaultDbContext db,
        ILogger<EvaluateDelinquencyCommandHandler>? logger = null)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task<Unit> Handle(EvaluateDelinquencyCommand request, CancellationToken cancellationToken)
    {
        var referenceDate = request.ReferenceDate.Date;

        // Fetch all credit accounts that have at least one statement whose DueDate
        // is strictly before the reference date. We include both Active and Delinquent
        // accounts because we need to re-evaluate Delinquent ones for resolution.
        var accounts = await _db.Accounts
            .Where(a => a.AccountType == AccountType.Credit
                        && (a.Status == AccountStatus.Active || a.Status == AccountStatus.Delinquent))
            .ToListAsync(cancellationToken);

        if (accounts.Count == 0) return Unit.Value;

        var accountIds = accounts.Select(a => a.Id).ToHashSet();

        // Load the MOST RECENT statement per account where DueDate < referenceDate.
        // We use a group-by to get the latest one (highest DueDate) per account.
        var statements = await _db.Statements
            .Where(s => accountIds.Contains(s.AccountId) && s.DueDate < referenceDate)
            .GroupBy(s => s.AccountId)
            .Select(g => g.OrderByDescending(s => s.DueDate).First())
            .ToListAsync(cancellationToken);

        if (statements.Count == 0) return Unit.Value;

        // Load existing ACTIVE delinquency records for these accounts
        var existingRecords = await _db.DelinquencyRecords
            .Where(r => accountIds.Contains(r.AccountId) && r.Status == DelinquencyRecordStatus.Active)
            .ToListAsync(cancellationToken);

        var recordsByAccount = existingRecords.ToDictionary(r => r.AccountId);
        var accountById      = accounts.ToDictionary(a => a.Id);

        foreach (var statement in statements)
        {
            if (!accountById.TryGetValue(statement.AccountId, out var account)) continue;

            var overdueAmount = statement.MinimumPayment - statement.PaidAmount;

            if (overdueAmount > 0m)
            {
                // Account owes money → ensure it's delinquent and the record is up to date.
                account.Status = AccountStatus.Delinquent;

                var daysInArrears = (int)(referenceDate - statement.DueDate.Date).TotalDays;
                var bucket        = CalculateBucket(daysInArrears);

                if (recordsByAccount.TryGetValue(account.Id, out var existing))
                {
                    // Update aging on the existing record
                    existing.OverdueAmount = overdueAmount;
                    existing.DaysInArrears = daysInArrears;
                    existing.Bucket        = bucket;
                    existing.UpdatedOn     = DateTimeOffset.UtcNow;
                }
                else
                {
                    // Create a new delinquency record for this cycle
                    _db.DelinquencyRecords.Add(new DelinquencyRecordEntity
                    {
                        Id            = Guid.NewGuid(),
                        AccountId     = account.Id,
                        StatementId   = statement.Id,
                        OverdueAmount = overdueAmount,
                        DaysInArrears = daysInArrears,
                        Bucket        = bucket,
                        Status        = DelinquencyRecordStatus.Active,
                    });
                }

                _logger?.LogInformation(
                    "Account {AccountId} marked DELINQUENT. Days={Days}, Bucket={Bucket}, Overdue={Overdue}",
                    account.Id, daysInArrears, bucket, overdueAmount);
            }
            else if (account.Status == AccountStatus.Delinquent)
            {
                // Customer has caught up → resolve
                account.Status = AccountStatus.Active;

                if (recordsByAccount.TryGetValue(account.Id, out var existing))
                {
                    existing.Status     = DelinquencyRecordStatus.Resolved;
                    existing.ResolvedOn = DateTimeOffset.UtcNow;
                    existing.UpdatedOn  = DateTimeOffset.UtcNow;
                }

                _logger?.LogInformation(
                    "Account {AccountId} RESOLVED. Delinquency cleared.", account.Id);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }

    /// <summary>Assigns the aging bucket based on calendar days in arrears.</summary>
    private static DelinquencyBucket CalculateBucket(int daysInArrears) => daysInArrears switch
    {
        <= 30  => DelinquencyBucket.DaysOneToThirty,
        <= 60  => DelinquencyBucket.DaysThirtyOneToSixty,
        <= 90  => DelinquencyBucket.DaysSixtyOneToNinety,
        _      => DelinquencyBucket.OverNinety
    };
}
