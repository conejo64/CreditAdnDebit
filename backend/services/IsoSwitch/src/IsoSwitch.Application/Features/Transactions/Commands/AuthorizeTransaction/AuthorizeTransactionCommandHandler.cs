using IsoSwitch.Application.Config;
using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Persistence.Transactions;
using IsoSwitch.Infrastructure.SwitchIso8583.Iso;
using IsoSwitch.Infrastructure.SwitchIso8583.Routing;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace IsoSwitch.Application.Features.Transactions.Commands.AuthorizeTransaction;

public class AuthorizeTransactionCommandHandler : IRequestHandler<AuthorizeTransactionCommand, AuthorizeTransactionResult>
{
    private readonly IsoSwitchDbContext _db;
    private readonly ConnectorRegistry _registry;
    private readonly IRoutingEngineV2 _routerV2;
    private readonly IMacService _macSvc;
    private readonly ISwitchEventPublisher _publisher;
    private readonly IIsoAuditService _audit;
    private readonly ILogger<AuthorizeTransactionCommandHandler> _logger;

    public AuthorizeTransactionCommandHandler(
        IsoSwitchDbContext db,
        ConnectorRegistry registry,
        IRoutingEngineV2 routerV2,
        IMacService macSvc,
        ISwitchEventPublisher publisher,
        IIsoAuditService audit,
        ILogger<AuthorizeTransactionCommandHandler> logger)
    {
        _db = db;
        _registry = registry;
        _routerV2 = routerV2;
        _macSvc = macSvc;
        _publisher = publisher;
        _audit = audit;
        _logger = logger;
    }

    public async Task<AuthorizeTransactionResult> Handle(AuthorizeTransactionCommand request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await _db.Transactions
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.IdempotencyKey == request.IdempotencyKey && t.TxType == TransactionTypes.Auth, ct);
                
            if (existing is not null)
            {
                return new AuthorizeTransactionResult(
                    existing.TraceId,
                    existing.Status,
                    existing.Decision,
                    existing.ResponseCode ?? "XX",
                    existing.ConnectorId,
                    existing.IdempotencyKey
                );
            }
        }

        var traceId = request.TraceId;
        var decision = await _routerV2.ResolveAsync(request.Bin, null, null, "AUTH", ct);
        var connectorId = decision.ConnectorId;

        var iso = new IsoMessage { Mti = "0100" };
        iso.Set(3, "000000");
        iso.Set(4, ((int)(request.Amount * 100)).ToString());
        iso.Set(11, request.Stan);
        iso.Set(41, request.TerminalId);
        iso.Set(42, request.MerchantId);
        iso.Set(49, request.Currency);

        if (!string.IsNullOrWhiteSpace(request.Pan)) iso.Set(2, request.Pan.Trim());
        if (!string.IsNullOrWhiteSpace(request.ExpiryYyMm)) iso.Set(14, request.ExpiryYyMm.Trim());
        if (!string.IsNullOrWhiteSpace(request.PosEntryMode)) iso.Set(22, request.PosEntryMode.Trim());
        if (!string.IsNullOrWhiteSpace(request.PosConditionCode)) iso.Set(25, request.PosConditionCode.Trim());
        if (!string.IsNullOrWhiteSpace(request.Track2)) iso.Set(35, request.Track2.Trim());
        if (!string.IsNullOrWhiteSpace(request.AdditionalAmounts54)) iso.Set(54, request.AdditionalAmounts54.Trim());
        if (!string.IsNullOrWhiteSpace(request.Private60)) iso.Set(60, request.Private60.Trim());
        if (!string.IsNullOrWhiteSpace(request.Private61)) iso.Set(61, request.Private61.Trim());
        if (!string.IsNullOrWhiteSpace(request.Private62)) iso.Set(62, request.Private62.Trim());

        var now = DateTimeOffset.UtcNow;
        iso.Set(7, now.ToString("MMddHHmmss"));
        iso.Set(12, now.ToString("HHmmss"));
        iso.Set(13, now.ToString("MMdd"));
        iso.Set(37, Guid.NewGuid().ToString("N")[..12].ToUpperInvariant());

        if (!string.IsNullOrWhiteSpace(request.PinBlock)) iso.Set(52, request.PinBlock.Trim());
        if (!string.IsNullOrWhiteSpace(request.EmvTlv)) iso.Set(55, request.EmvTlv.Trim().ToUpperInvariant());

        iso.Set(64, _macSvc.ComputeMacHex("AUTH"));
        iso.Set(128, _macSvc.ComputeMacHex("AUTH128"));

        var tx = new TransactionEntity
        {
            TraceId = traceId,
            CorrelationId = traceId,
            IdempotencyKey = request.IdempotencyKey,
            RequestMti = iso.Mti,
            Stan = request.Stan,
            TxType = TransactionTypes.Auth,
            Status = TransactionStatuses.Pending,
            Decision = "UNKNOWN",
            ConnectorId = connectorId,
            CreatedOn = DateTimeOffset.UtcNow,
            UpdatedOn = DateTimeOffset.UtcNow,
            InDoubt = false,
            ReversalStatus = "N/A",
            ProcessingCode = iso.Fields.TryGetValue(3, out var pc) ? pc : null,
            Amount12 = iso.Fields.TryGetValue(4, out var a12) ? a12 : null,
            Currency = iso.Fields.TryGetValue(49, out var c49) ? c49 : null,
            TerminalId = iso.Fields.TryGetValue(41, out var t41) ? t41 : null,
            MerchantId = iso.Fields.TryGetValue(42, out var m42) ? m42 : null,
            RequestJson = JsonSerializer.Serialize(new { mti = iso.Mti, fields = iso.Fields })
        };

        _db.Transactions.Add(tx);
        await _db.SaveChangesAsync(ct);

        var connector = _registry.Get(connectorId);
        await _audit.LogAsync(traceId, "OUT", iso, ct);
        await _publisher.PublishIsoAsync(traceId, new { type = "sw.iso.sent", traceId, mti = iso.Mti, connectorId, at = DateTimeOffset.UtcNow }, ct);

        IsoMessage resp;
        try
        {
            resp = await connector.AuthorizeAsync(iso, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connector {ConnectorId} failed for trace {TraceId}. Marking InDoubt.", connectorId, traceId);
            tx.InDoubt = true;
            tx.Status = TransactionStatuses.InDoubt;
            await _db.SaveChangesAsync(ct);
            throw;
        }

        await _audit.LogAsync(traceId, "IN", resp, ct);
        await _publisher.PublishIsoAsync(traceId, new { type = "sw.iso.received", traceId, mti = resp.Mti, connectorId, at = DateTimeOffset.UtcNow }, ct);

        var rc = resp.Fields.TryGetValue(39, out var code) ? code : "XX";
        var nextStatus = rc == "00" ? TransactionStatuses.Confirmed : TransactionStatuses.Declined;
        
        TransactionStateMachine.EnsureTransition(tx.TxType, tx.Status, nextStatus);
        tx.Status = nextStatus;
        tx.Decision = rc == "00" ? "APPROVED" : "DECLINED";
        tx.ResponseCode = rc;
        tx.UpdatedOn = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        await _publisher.PublishTxAsync(traceId, new { type = "sw.tx.updated", traceId, status = tx.Status, decision = tx.Decision, responseCode = rc, connectorId, updatedOn = DateTimeOffset.UtcNow, idempotencyKey = request.IdempotencyKey }, ct);

        return new AuthorizeTransactionResult(
            traceId,
            tx.Status,
            tx.Decision,
            rc,
            connectorId
        );
    }
}
