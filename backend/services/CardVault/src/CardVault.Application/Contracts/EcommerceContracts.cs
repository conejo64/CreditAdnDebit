namespace CardVault.Application.Contracts;

public sealed record StartThreeDsChallengeRequest(
    Guid CardId,
    decimal Amount,
    string Currency,
    string MerchantId,
    string MerchantName,
    string? MerchantCountry,
    string? BrowserIpCountry,
    string? DeviceChannel);

public sealed record VerifyThreeDsChallengeRequest(string Otp);

public sealed record StartThreeDsChallengeResponse(
    Guid ChallengeId,
    Guid CardId,
    Guid AccountId,
    Guid CustomerId,
    string Status,
    string Decision,
    int RiskScore,
    IReadOnlyList<string> RiskReasons,
    string ContactHint,
    DateTimeOffset ExpiresOn,
    string? DevelopmentOtp);

public sealed record VerifyThreeDsChallengeResponse(
    Guid ChallengeId,
    string Status,
    string Decision,
    string DecisionReason,
    int AttemptsUsed,
    int AttemptsRemaining,
    DateTimeOffset? CompletedOn);

public sealed record ThreeDsChallengeView(
    Guid ChallengeId,
    Guid CardId,
    Guid AccountId,
    Guid CustomerId,
    string MaskedPan,
    decimal Amount,
    string Currency,
    string MerchantId,
    string MerchantName,
    string? MerchantCountry,
    string? BrowserIpCountry,
    string DeviceChannel,
    int RiskScore,
    IReadOnlyList<string> RiskReasons,
    string ContactHint,
    string Status,
    string Decision,
    string? DecisionReason,
    int AttemptsUsed,
    int MaxAttempts,
    DateTimeOffset ExpiresOn,
    DateTimeOffset? AuthenticatedOn,
    DateTimeOffset? CompletedOn,
    DateTimeOffset CreatedOn,
    DateTimeOffset UpdatedOn,
    string RequestedBy,
    string TraceId);
