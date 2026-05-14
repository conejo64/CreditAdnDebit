namespace CardVault.Api.Contracts;

public sealed record CreateCountryRequest(string Code, string Name, string NumericCode, string Currency);
public sealed record CreateBinRangeRequest(int BinStart, int BinEnd, string Brand, string Product, string? IssuerName, string? CountryCode);
public sealed record CreateCardProductRequest(string Code, string Brand, string ProductType, string Name);