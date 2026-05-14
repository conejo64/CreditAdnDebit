using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.CreditLimits;

public enum CreditLimitProposalStatus
{
    Proposed = 1,
    Applied = 2,
    Rejected = 3
}

public sealed class CreditLimitProposalEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public decimal CurrentLimit { get; set; }

    public decimal ProposedIncreaseAmount { get; set; }

    public decimal ProposedLimit { get; set; }

    public decimal OnTimePaymentRatio { get; set; }

    public decimal AverageUtilizationRatio { get; set; }

    public int StatementsReviewed { get; set; }

    public CreditLimitProposalStatus Status { get; set; } = CreditLimitProposalStatus.Proposed;

    [MaxLength(200)]
    public string DecisionReason { get; set; } = default!;

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? AppliedOn { get; set; }
}
