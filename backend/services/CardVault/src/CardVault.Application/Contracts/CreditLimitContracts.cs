namespace CardVault.Application.Contracts;

public sealed record OverlimitEventView(
    Guid OverlimitEventId,
    Guid AccountId,
    Guid? HoldId,
    decimal ApprovedAmount,
    decimal AvailableCreditBefore,
    decimal OverlimitAmount,
    string? TraceId,
    DateTimeOffset CreatedOn);

public sealed record CreditLimitProposalView(
    Guid ProposalId,
    Guid AccountId,
    decimal CurrentLimit,
    decimal ProposedIncreaseAmount,
    decimal ProposedLimit,
    decimal OnTimePaymentRatio,
    decimal AverageUtilizationRatio,
    int StatementsReviewed,
    string Status,
    string DecisionReason,
    DateTimeOffset CreatedOn,
    DateTimeOffset? AppliedOn);

public sealed record CreditLimitEvaluationView(
    Guid AccountId,
    bool Eligible,
    decimal CurrentLimit,
    decimal OnTimePaymentRatio,
    decimal AverageUtilizationRatio,
    int StatementsReviewed,
    string DecisionReason,
    CreditLimitProposalView? Proposal);
