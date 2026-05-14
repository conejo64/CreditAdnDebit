namespace IsoSwitch.Api;

public sealed record RoutingDecisionRequest(
    int Bin,
    string? CountryCode,
    string? Network,
    string? TxType);