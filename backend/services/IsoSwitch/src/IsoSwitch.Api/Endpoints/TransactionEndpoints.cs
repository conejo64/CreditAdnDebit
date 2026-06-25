using IsoSwitch.Api;
using IsoSwitch.Api.Routing;
using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Persistence.Transactions;
using IsoSwitch.Infrastructure.SwitchIso8583.Iso;
using MediatR;
using Microsoft.EntityFrameworkCore;
using IsoSwitch.Api.Security;
using IsoSwitch.Api.Services;
using IsoSwitch.Infrastructure.SwitchIso8583.Routing;
using IsoSwitch.Application.Features.Transactions.Commands.NetworkManagement;

namespace IsoSwitch.Api.Endpoints;

public static class TransactionEndpoints
{
    public static void MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        var operations = app.MapGroup("/api/iso")
            .RequireAuthorization(IsoSwitchAuthorizationPolicies.OperateSwitch);

        operations.MapPost("/authorize", async (HttpRequest http, AuthorizeRequest req, ISender sender, CancellationToken ct) =>
        {
            var command = new IsoSwitch.Application.Features.Transactions.Commands.AuthorizeTransaction.AuthorizeTransactionCommand(
                TraceId: req.TraceId,
                Bin: req.Bin,
                Amount: req.Amount,
                Currency: req.Currency,
                MerchantId: req.MerchantId,
                TerminalId: req.TerminalId,
                Stan: req.Stan,
                PinBlock: req.PinBlock,
                EmvTlv: req.EmvTlv,
                Pan: req.Pan,
                ExpiryYyMm: req.ExpiryYyMm,
                PosEntryMode: req.PosEntryMode,
                PosConditionCode: req.PosConditionCode,
                Track2: req.Track2,
                AdditionalAmounts54: req.AdditionalAmounts54,
                Private60: req.Private60,
                Private61: req.Private61,
                Private62: req.Private62,
                IdempotencyKey: http.GetIdempotencyKey()
            );

            var result = await sender.Send(command, ct);
            return Results.Ok(new { 
                traceId = result.TraceId, 
                status = result.Status, 
                decision = result.Decision, 
                responseCode = result.ResponseCode, 
                connectorId = result.ConnectorId, 
                idempotencyKey = result.IdempotencyKey 
            });
        });

        operations.MapPost("/reversal", async (ReversalRequest req, ISender sender, CancellationToken ct) =>
        {
            var command = new IsoSwitch.Application.Features.Transactions.Commands.ReversalTransaction.ReversalTransactionCommand(
                OriginalTraceId: req.OriginalTraceId,
                MerchantId: req.MerchantId,
                TerminalId: req.TerminalId,
                Currency: req.Currency,
                PinBlock: req.PinBlock,
                EmvTlv: req.EmvTlv
            );

            try
            {
                var result = await sender.Send(command, ct);
                return Results.Ok(new { 
                    originalTraceId = result.OriginalTraceId, 
                    reversalResponseCode = result.ReversalResponseCode,
                    status = result.Status,
                    decision = result.Decision
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { message = ex.Message });
            }
        });

        operations.MapPost("/capture", async (HttpRequest http, AuthorizeRequest req, ISender sender, CancellationToken ct) =>
        {
            var command = new IsoSwitch.Application.Features.Transactions.Commands.CaptureTransaction.CaptureTransactionCommand(
                TraceId: req.TraceId,
                Bin: req.Bin,
                Amount: req.Amount,
                Currency: req.Currency,
                MerchantId: req.MerchantId,
                TerminalId: req.TerminalId,
                Stan: req.Stan,
                PinBlock: req.PinBlock,
                EmvTlv: req.EmvTlv,
                Pan: req.Pan,
                ExpiryYyMm: req.ExpiryYyMm,
                PosEntryMode: req.PosEntryMode,
                PosConditionCode: req.PosConditionCode,
                Track2: req.Track2,
                AdditionalAmounts54: req.AdditionalAmounts54,
                Private60: req.Private60,
                Private61: req.Private61,
                Private62: req.Private62,
                IdempotencyKey: http.GetIdempotencyKey()
            );

            var result = await sender.Send(command, ct);
            return Results.Ok(new { 
                traceId = result.TraceId, 
                status = result.Status, 
                decision = result.Decision, 
                responseCode = result.ResponseCode, 
                connectorId = result.ConnectorId,
                idempotencyKey = result.IdempotencyKey
            });
        });

        operations.MapPost("/reversal-advice", async (HttpRequest http, string traceId, string originalTraceId, ISender sender, CancellationToken ct) =>
        {
            var command = new IsoSwitch.Application.Features.Transactions.Commands.ReversalAdvice.ReversalAdviceCommand(
                TraceId: traceId,
                OriginalTraceId: originalTraceId,
                IdempotencyKey: http.GetIdempotencyKey()
            );

            var result = await sender.Send(command, ct);
            return Results.Ok(new { 
                traceId = result.TraceId, 
                status = result.Status, 
                decision = result.Decision, 
                responseCode = result.ResponseCode, 
                connectorId = result.ConnectorId, 
                field90 = result.Field90 
            });
        });

        operations.MapPost("/network/ping", async (string traceId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new NetworkCommand(traceId, NetworkOperation.Ping), ct);
            return Results.Ok(new { traceId = result.TraceId, mti = result.Mti, responseCode = result.ResponseCode });
        });
 
        operations.MapPost("/network/signon", async (string traceId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new NetworkCommand(traceId, NetworkOperation.SignOn), ct);
            return Results.Ok(new { traceId = result.TraceId, mti = result.Mti, responseCode = result.ResponseCode });
        });
 
        operations.MapPost("/network/signoff", async (string traceId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new NetworkCommand(traceId, NetworkOperation.SignOff), ct);
            return Results.Ok(new { traceId = result.TraceId, mti = result.Mti, responseCode = result.ResponseCode });
        });
    }
}
