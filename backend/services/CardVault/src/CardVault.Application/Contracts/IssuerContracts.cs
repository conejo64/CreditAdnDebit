using CardVault.Domain;

namespace CardVault.Application.Contracts;

public sealed record CreateCustomerRequest(string FullName, string DocumentId, string Email, string Phone, string DocumentType, string Gender, string BillingAddress, string StatementAddress, string ResidenceCity, string StatementCity, string CardDeliveryCity);
public sealed record CreateAccountRequest(Guid CustomerId, AccountType AccountType, string ProductCode, decimal CreditLimit);
public sealed record IssueCardRequest(Guid AccountId, string Bin, string Pan, string ExpiryYyMm);
public sealed record BlockCardRequest(string Reason);
public sealed record CancelCardRequest(string? Reason);
public sealed record ReplaceCardRequest(string? Reason);
public sealed record DisputeTransitionRequest(string Action, string? Notes);
public sealed record MinimumPaymentPolicyUpsert(string Code, bool IsDefault, decimal FloorAmount, decimal PrincipalPercent, decimal? CeilingAmount, bool IncludeInterest, bool IncludeFees);
public sealed record ApplyPaymentRequest(decimal Amount, DateTimeOffset? PostedOn);
public sealed record PostLedgerRequest(Guid AccountId, decimal Amount, string Description, DateTimeOffset? PostedOn);
public sealed record GenerateStatementRequest(Guid AccountId, DateTime CycleStart, DateTime CycleEnd, DateTime StatementDate, DateTime? DueDate);
public sealed record VelocityRuleUpsertRequest(string ProductCode, int WindowMinutes, int MaxCount, decimal MaxAmount, string? Description);
public sealed record MccRuleUpsertRequest(string Mcc, bool IsBlocked, decimal? PerTxnLimit, string? Description);
public sealed record DeferPurchaseRequest(Guid AccountId, Guid LedgerEntryId, int Installments, decimal? Apr);
public sealed record SetPinRequest(string Pin);
