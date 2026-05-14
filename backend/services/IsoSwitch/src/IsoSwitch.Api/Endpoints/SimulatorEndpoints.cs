using IsoSwitch.Api.Background;
using IsoSwitch.Api.Iso8583;
using IsoSwitch.Api.Security;
using IsoSwitch.Api.Tcp;
using IsoSwitch.Infrastructure.SwitchIso8583.Iso;
using IsoSwitch.Infrastructure.SwitchIso8583.Net;

namespace IsoSwitch.Api.Endpoints;

public static class SimulatorEndpoints
{
    public static void MapSimulatorEndpoints(this IEndpointRouteBuilder app)
    {
        var demoRead = app.MapGroup("/api").AllowAnonymous();
        var switchOps = app.MapGroup("/api")
            .RequireAuthorization(IsoSwitchAuthorizationPolicies.OperateSwitch);
        var monitorRead = app.MapGroup("/api")
            .RequireAuthorization(IsoSwitchAuthorizationPolicies.ViewSwitchMonitor);
        var adminDiagnostics = app.MapGroup("/api")
            .RequireAuthorization(IsoSwitchAuthorizationPolicies.ManageSwitchRoutes);

        demoRead.MapGet("/simulator/options", (IsoSimulatorOptions opt) => Results.Ok(opt)).WithOpenApi();

        switchOps.MapPost("/simulate/purchase/approve", async (ISwitchEventPublisher pub, SimPurchaseRequest req, CancellationToken ct) =>
        {
            var env = new
            {
                eventName = "switch.v1.purchase.approved",
                eventId = Guid.NewGuid().ToString("N"),
                occurredOn = DateTimeOffset.UtcNow,
                payload = new
                {
                    accountId = req.AccountId,
                    amount = req.Amount,
                    network = req.Network,
                    mti = req.Mti ?? "0100",
                    stan = req.Stan ?? "000001",
                    rrn = req.Rrn ?? Guid.NewGuid().ToString("N")[..12],
                    postedOn = req.PostedOn ?? DateTimeOffset.UtcNow
                }
            };
            await pub.PublishTxAsync(req.AccountId.ToString("N"), env, ct);
            return Results.Accepted();
        }).WithOpenApi();

        switchOps.MapPost("/simulate/purchase/reverse", async (ISwitchEventPublisher pub, SimPurchaseRequest req, CancellationToken ct) =>
        {
            var env = new
            {
                eventName = "switch.v1.purchase.reversed",
                eventId = Guid.NewGuid().ToString("N"),
                occurredOn = DateTimeOffset.UtcNow,
                payload = new
                {
                    accountId = req.AccountId,
                    amount = req.Amount,
                    network = req.Network,
                    mti = req.Mti ?? "0100",
                    stan = req.Stan ?? "000001",
                    rrn = req.Rrn ?? Guid.NewGuid().ToString("N")[..12],
                    postedOn = req.PostedOn ?? DateTimeOffset.UtcNow
                }
            };
            await pub.PublishTxAsync(req.AccountId.ToString("N"), env, ct);
            return Results.Accepted();
        }).WithOpenApi();

        switchOps.MapPost("/simulate/refund", async (ISwitchEventPublisher pub, SimPurchaseRequest req, CancellationToken ct) =>
        {
            var env = new
            {
                eventName = "switch.v1.refund.posted",
                eventId = Guid.NewGuid().ToString("N"),
                occurredOn = DateTimeOffset.UtcNow,
                payload = new
                {
                    accountId = req.AccountId,
                    amount = req.Amount,
                    network = req.Network,
                    mti = req.Mti ?? "0220",
                    stan = req.Stan ?? "000002",
                    rrn = req.Rrn ?? Guid.NewGuid().ToString("N")[..12],
                    postedOn = req.PostedOn ?? DateTimeOffset.UtcNow
                }
            };
            await pub.PublishTxAsync(req.AccountId.ToString("N"), env, ct);
            return Results.Accepted();
        }).WithOpenApi();

        switchOps.MapPost("/simulate/chargeback", async (ISwitchEventPublisher pub, SimPurchaseRequest req, CancellationToken ct) =>
        {
            var env = new
            {
                eventName = "switch.v1.chargeback.posted",
                eventId = Guid.NewGuid().ToString("N"),
                occurredOn = DateTimeOffset.UtcNow,
                payload = new
                {
                    accountId = req.AccountId,
                    amount = req.Amount,
                    network = req.Network,
                    mti = req.Mti ?? "0420",
                    stan = req.Stan ?? "000003",
                    rrn = req.Rrn ?? Guid.NewGuid().ToString("N")[..12],
                    reasonCode = req.ReasonCode ?? "4855",
                    postedOn = req.PostedOn ?? DateTimeOffset.UtcNow
                }
            };
            await pub.PublishTxAsync(req.AccountId.ToString("N"), env, ct);
            return Results.Accepted();
        }).WithOpenApi();

        switchOps.MapPost("/simulate/auth/approve", async (ISwitchEventPublisher pub, SimAuthRequest req, CancellationToken ct) =>
        {
            var env = new
            {
                eventName = "switch.v1.auth.approved",
                eventId = Guid.NewGuid().ToString("N"),
                occurredOn = DateTimeOffset.UtcNow,
                payload = new
                {
                    accountId = req.AccountId,
                    amount = req.Amount,
                    network = req.Network,
                    mti = req.Mti ?? "0100",
                    stan = req.Stan ?? "000101",
                    rrn = req.Rrn ?? Guid.NewGuid().ToString("N")[..12],
                    originalDataElements90 = req.OriginalDataElements90,
                    merchantId = req.MerchantId,
                    merchantCategory = req.MerchantCategory,
                    postedOn = req.PostedOn ?? DateTimeOffset.UtcNow
                }
            };
            await pub.PublishTxAsync(req.AccountId.ToString("N"), env, ct);
            return Results.Accepted();
        }).WithOpenApi();

        switchOps.MapPost("/simulate/auth/reverse", async (ISwitchEventPublisher pub, SimAuthRequest req, CancellationToken ct) =>
        {
            var env = new
            {
                eventName = "switch.v1.auth.reversed",
                eventId = Guid.NewGuid().ToString("N"),
                occurredOn = DateTimeOffset.UtcNow,
                payload = new
                {
                    accountId = req.AccountId,
                    amount = req.Amount,
                    network = req.Network,
                    mti = req.Mti ?? "0400",
                    stan = req.Stan ?? "000101",
                    rrn = req.Rrn ?? Guid.NewGuid().ToString("N")[..12],
                    originalDataElements90 = req.OriginalDataElements90,
                    merchantId = req.MerchantId,
                    merchantCategory = req.MerchantCategory,
                    postedOn = req.PostedOn ?? DateTimeOffset.UtcNow
                }
            };
            await pub.PublishTxAsync(req.AccountId.ToString("N"), env, ct);
            return Results.Accepted();
        }).WithOpenApi();

        switchOps.MapPost("/simulate/clearing", async (ISwitchEventPublisher pub, SimAuthRequest req, CancellationToken ct) =>
        {
            var env = new
            {
                eventName = "switch.v1.clearing.posted",
                eventId = Guid.NewGuid().ToString("N"),
                occurredOn = DateTimeOffset.UtcNow,
                payload = new
                {
                    accountId = req.AccountId,
                    amount = req.Amount,
                    network = req.Network,
                    mti = req.Mti ?? "0200",
                    stan = req.Stan ?? "000201",
                    rrn = req.Rrn ?? Guid.NewGuid().ToString("N")[..12],
                    originalDataElements90 = req.OriginalDataElements90,
                    merchantId = req.MerchantId,
                    merchantCategory = req.MerchantCategory,
                    postedOn = req.PostedOn ?? DateTimeOffset.UtcNow
                }
            };
            await pub.PublishTxAsync(req.AccountId.ToString("N"), env, ct);
            return Results.Accepted();
        }).WithOpenApi();

        adminDiagnostics.MapPost("/demo/pan-map", (PanMapRequest req) =>
        {
            PanMapStore.Map[req.Pan] = req.AccountId;
            return Results.Ok(new { req.Pan, req.AccountId });
        }).WithOpenApi();

        adminDiagnostics.MapGet("/demo/pan-map", () =>
        {
            var list = PanMapStore.Map.Select(kv => new { pan = kv.Key, accountId = kv.Value }).ToList();
            return Results.Ok(list);
        }).WithOpenApi();

        demoRead.MapGet("/demo/iso0100", (string pan, decimal amount, string? stan, string? rrn, string? mcc) =>
        {
            var now = DateTime.UtcNow;
            var msg = new IsoSwitch.Api.Iso8583.Iso8583Message("0100").Set(2, pan).Set(3, "000000").Set(4, ((long)Math.Round(amount * 100m)).ToString()).Set(7, now.ToString("MMddHHmmss")).Set(11, stan ?? "123456").Set(37, rrn ?? Guid.NewGuid().ToString("N")[..12]).Set(18, mcc ?? "5411").Set(41, "TERM0001").Set(42, "ACCEPTOR0000001").Set(49, "840");
            return Results.Ok(new { ascii = msg.PackAscii(), hex = msg.PackHex() });
        }).WithOpenApi();

        demoRead.MapGet("/tcp/status", (IConfiguration cfg) =>
        {
            var port = cfg.GetValue<int?>("Tcp:Iso8583Port") ?? 7000;
            return Results.Ok(new { iso8583Port = port, protocol = "ASCII line per message (MTI+BitmapHex+Fields)" });
        }).WithOpenApi();

        monitorRead.MapGet("/iso/responses", (int? take) =>
        {
            var t = take.GetValueOrDefault(20);
            if (t <= 0) t = 20;
            if (t > 100) t = 100;
            var list = SwitchResponseConsumer.LastResponses.Where(x => x.StartsWith("ISO0110_PAYLOAD_HEX=")).Reverse().Take(t).ToList();
            return Results.Ok(list);
        }).WithOpenApi();

        monitorRead.MapGet("/responses", (int? take) =>
        {
            var t = take.GetValueOrDefault(50);
            if (t <= 0) t = 50;
            if (t > 100) t = 100;
            var list = SwitchResponseConsumer.LastResponses.Reverse().Take(t).ToList();
            return Results.Ok(list);
        }).WithOpenApi();

        demoRead.MapGet("/demo/iso0100/binary", (string pan, decimal amount, string stan, string rrn, string? mcc, string? terminalId, string? acceptorId, string? currency) =>
        {
            var cur = string.IsNullOrWhiteSpace(currency) ? "840" : currency!;
            var msg = new IsoSwitch.Api.Iso8583.Iso8583MessageBinary("0100").Set(2, pan).Set(3, "000000").Set(4, ((long)Math.Round(Math.Abs(amount) * 100m)).ToString()).Set(11, stan).Set(37, rrn).Set(49, cur);
            if (!string.IsNullOrWhiteSpace(mcc)) msg.Set(18, mcc!);
            if (!string.IsNullOrWhiteSpace(terminalId)) msg.Set(41, terminalId!);
            if (!string.IsNullOrWhiteSpace(acceptorId)) msg.Set(42, acceptorId!);
            var payload = msg.PackPayload();
            var frame = msg.PackFrame();
            return Results.Ok(new { asciiHint = "Binary framing (2-byte length) + payload in HEX. Payload begins with MTI ASCII.", payloadHex = Convert.ToHexString(payload), frameHex = Convert.ToHexString(frame) });
        }).WithOpenApi();

        adminDiagnostics.MapPost("/demo/tcp-send", async (TcpSendRequest req) =>
        {
            var host = req.Host ?? "127.0.0.1";
            var port = req.Port ?? 7000;
            var frame = Convert.FromHexString(req.FrameHex);
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(host, port);
            await using var stream = client.GetStream();
            await stream.WriteAsync(frame);
            if (!Iso8583Binary.TryReadFrame(stream, out var payload)) return Results.Problem("No response");
            var respHex = Convert.ToHexString(payload);
            return Results.Ok(new { responsePayloadHex = respHex });
        }).WithOpenApi();

        monitorRead.MapGet("/iso/traces", (int? take) =>
        {
            var t = take.GetValueOrDefault(50);
            return Results.Ok(IsoTraceStore.GetLast(t));
        }).WithOpenApi();

        demoRead.MapGet("/demo/iso0100/binary-tpdu", (string pan, decimal amount, string stan, string rrn, string? mcc, string? terminalId, string? acceptorId, string? currency, string? tpduHex) =>
        {
            var cur = string.IsNullOrWhiteSpace(currency) ? "840" : currency!;
            var msg = new IsoSwitch.Api.Iso8583.Iso8583MessageBinary("0100").Set(2, pan).Set(3, "000000").Set(4, ((long)Math.Round(Math.Abs(amount) * 100m)).ToString()).Set(7, DateTime.UtcNow.ToString("MMddHHmmss")).Set(11, stan).Set(12, DateTime.UtcNow.ToString("HHmmss")).Set(13, DateTime.UtcNow.ToString("MMdd")).Set(37, rrn).Set(49, cur);
            if (!string.IsNullOrWhiteSpace(mcc)) msg.Set(18, mcc!);
            if (!string.IsNullOrWhiteSpace(terminalId)) msg.Set(41, terminalId!);
            if (!string.IsNullOrWhiteSpace(acceptorId)) msg.Set(42, acceptorId!);
            var payload = msg.PackPayload();
            var tpdu = Convert.FromHexString(string.IsNullOrWhiteSpace(tpduHex) ? "6000030000" : tpduHex!);
            if (tpdu.Length != 5) return Results.BadRequest("TPDU must be 5 bytes (10 hex chars)");
            var full = tpdu.Concat(payload).ToArray();
            var frame = Iso8583Binary.BuildFrame(full);
            return Results.Ok(new { tpduUsedHex = Convert.ToHexString(tpdu), payloadHex = Convert.ToHexString(full), frameHex = Convert.ToHexString(frame) });
        }).WithOpenApi();

        demoRead.MapGet("/demo/iso0100/binary-v50", (string pan, decimal amount, string stan, string rrn, string? mcc, string? terminalId, string? acceptorId, string? currency, string? track2, string? pinBlock, string? emv55) =>
        {
            var cur = string.IsNullOrWhiteSpace(currency) ? "840" : currency!;
            var msg = new IsoSwitch.Api.Iso8583.Iso8583MessageBinary("0100").Set(2, pan).Set(3, "000000").Set(4, ((long)Math.Round(Math.Abs(amount) * 100m)).ToString()).Set(7, DateTime.UtcNow.ToString("MMddHHmmss")).Set(11, stan).Set(12, DateTime.UtcNow.ToString("HHmmss")).Set(13, DateTime.UtcNow.ToString("MMdd")).Set(37, rrn).Set(49, cur);
            if (!string.IsNullOrWhiteSpace(mcc)) msg.Set(18, mcc!);
            if (!string.IsNullOrWhiteSpace(terminalId)) msg.Set(41, terminalId!);
            if (!string.IsNullOrWhiteSpace(acceptorId)) msg.Set(42, acceptorId!);
            if (!string.IsNullOrWhiteSpace(track2)) msg.Set(35, track2!);
            if (!string.IsNullOrWhiteSpace(pinBlock)) msg.Set(52, pinBlock!);
            if (!string.IsNullOrWhiteSpace(emv55)) msg.Set(55, emv55!);
            var payload = msg.PackPayload();
            var frame = msg.PackFrame();
            return Results.Ok(new { payloadHex = Convert.ToHexString(payload), frameHex = Convert.ToHexString(frame) });
        }).WithOpenApi();

        demoRead.MapGet("/demo/iso0100/binary-tpdu-v50", (string pan, decimal amount, string stan, string rrn, string? mcc, string? terminalId, string? acceptorId, string? currency, string? tpduHex, string? track2, string? pinBlock, string? emv55) =>
        {
            var cur = string.IsNullOrWhiteSpace(currency) ? "840" : currency!;
            var msg = new IsoSwitch.Api.Iso8583.Iso8583MessageBinary("0100").Set(2, pan).Set(3, "000000").Set(4, ((long)Math.Round(Math.Abs(amount) * 100m)).ToString()).Set(7, DateTime.UtcNow.ToString("MMddHHmmss")).Set(11, stan).Set(12, DateTime.UtcNow.ToString("HHmmss")).Set(13, DateTime.UtcNow.ToString("MMdd")).Set(37, rrn).Set(49, cur);
            if (!string.IsNullOrWhiteSpace(mcc)) msg.Set(18, mcc!);
            if (!string.IsNullOrWhiteSpace(terminalId)) msg.Set(41, terminalId!);
            if (!string.IsNullOrWhiteSpace(acceptorId)) msg.Set(42, acceptorId!);
            if (!string.IsNullOrWhiteSpace(track2)) msg.Set(35, track2!);
            if (!string.IsNullOrWhiteSpace(pinBlock)) msg.Set(52, pinBlock!);
            if (!string.IsNullOrWhiteSpace(emv55)) msg.Set(55, emv55!);
            var payload = msg.PackPayload();
            var tpdu = Convert.FromHexString(string.IsNullOrWhiteSpace(tpduHex) ? "6000030000" : tpduHex!);
            if (tpdu.Length != 5) return Results.BadRequest("TPDU must be 5 bytes (10 hex chars)");
            var full = tpdu.Concat(payload).ToArray();
            var frame = Iso8583Binary.BuildFrame(full);
            return Results.Ok(new { tpduUsedHex = Convert.ToHexString(tpdu), payloadHex = Convert.ToHexString(full), frameHex = Convert.ToHexString(frame) });
        }).WithOpenApi();

        adminDiagnostics.MapPost("/demo/pan-map-token", async (PanMapTokenRequest req, CatalogAuditPersistence audit) =>
        {
            if (string.IsNullOrWhiteSpace(req.TokenPan) || !req.TokenPan.StartsWith("TPAN_")) return Results.BadRequest("TokenPan must start with TPAN_");
            if (!Guid.TryParse(req.AccountId, out var id)) return Results.BadRequest("Invalid accountId");
            PanMapStore.MapToken(req.TokenPan, id);
            await audit.AppendEventAsync("tokenpan.mapped", $"tpan:{req.TokenPan}", new PanMapStore.TokenPanMap(req.TokenPan, id.ToString()), CancellationToken.None);
            return Results.Ok(new { mapped = true, tokenPan = req.TokenPan, accountId = id });
        }).WithOpenApi();

        adminDiagnostics.MapPost("/tokenization/tokenize", (TokenizePanRequest req, IsoSwitch.Api.Security.ITokenPanService tokenSvc) =>
        {
            if (string.IsNullOrWhiteSpace(req.Pan)) return Results.BadRequest("pan required");
            if (!IsoSwitch.Api.Security.PanUtils.IsValidLuhn(req.Pan)) return Results.BadRequest("invalid pan (luhn)");
            var tokenPan = tokenSvc.Tokenize(req.Pan);
            return Results.Ok(new { tokenPan, bin6 = IsoSwitch.Api.Security.PanUtils.Bin6(req.Pan), last4 = IsoSwitch.Api.Security.PanUtils.Last4(req.Pan), panMasked = IsoSwitch.Api.Security.PanUtils.Mask(req.Pan) });
        }).WithOpenApi();

        adminDiagnostics.MapPost("/demo/pan-map-v51", async (PanMapV51Request req, IsoSwitch.Api.Security.ITokenPanService tokenSvc, CatalogAuditPersistence audit) =>
        {
            if (string.IsNullOrWhiteSpace(req.Pan)) return Results.BadRequest("pan required");
            if (!Guid.TryParse(req.AccountId, out var id)) return Results.BadRequest("invalid accountId");
            if (!IsoSwitch.Api.Security.PanUtils.IsValidLuhn(req.Pan)) return Results.BadRequest("invalid pan (luhn)");
            var tokenPan = tokenSvc.Tokenize(req.Pan);
            PanMapStore.MapToken(tokenPan, id);
            await audit.AppendEventAsync("tokenpan.mapped", $"tpan:{tokenPan}", new PanMapStore.TokenPanMap(tokenPan, id.ToString()), CancellationToken.None);
            return Results.Ok(new { mapped = true, tokenPan, panMasked = IsoSwitch.Api.Security.PanUtils.Mask(req.Pan), accountId = id });
        }).WithOpenApi();
    }
}
