using MediatR;

namespace IsoSwitch.Api.Features.Transactions.Commands.ReversalTransaction;

public record ReversalTransactionCommand(
    string OriginalTraceId,
    string? MerchantId = null,
    string? TerminalId = null,
    string? Currency = null,
    string? PinBlock = null,
    string? EmvTlv = null
) : IRequest<ReversalTransactionResult>;

public record ReversalTransactionResult(
    string OriginalTraceId,
    string ReversalResponseCode,
    string Status,
    string Decision
);
