using MediatR;

namespace IsoSwitch.Api.Features.Transactions.Commands.NetworkManagement;

public enum NetworkOperation
{
    Ping,
    SignOn,
    SignOff
}

public record NetworkCommand(
    string TraceId,
    NetworkOperation Operation,
    string ConnectorId = "SIMULATOR"
) : IRequest<NetworkResult>;

public record NetworkResult(
    string TraceId,
    string Mti,
    string ResponseCode,
    string Status
);
