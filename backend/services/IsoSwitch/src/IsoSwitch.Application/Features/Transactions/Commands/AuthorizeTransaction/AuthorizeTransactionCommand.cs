using MediatR;

namespace IsoSwitch.Application.Features.Transactions.Commands.AuthorizeTransaction;

public record AuthorizeTransactionCommand(
    string TraceId,
    int Bin,
    decimal Amount,
    string Currency,
    string MerchantId,
    string TerminalId,
    string Stan,
    string? PinBlock,
    string? EmvTlv,
    string? Pan,
    string? ExpiryYyMm,
    string? PosEntryMode,
    string? PosConditionCode,
    string? Track2,
    string? AdditionalAmounts54,
    string? Private60,
    string? Private61,
    string? Private62,
    string? IdempotencyKey
) : IRequest<AuthorizeTransactionResult>;

public record AuthorizeTransactionResult(
    string TraceId,
    string Status,
    string Decision,
    string ResponseCode,
    string ConnectorId,
    string? IdempotencyKey = null
);
