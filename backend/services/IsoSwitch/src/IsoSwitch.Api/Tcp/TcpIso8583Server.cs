using System.Net;
using System.Net.Sockets;
using IsoSwitch.Api.Iso8583;
using IsoSwitch.Api.Routing;
using IsoSwitch.Api.Security;
using IsoSwitch.Api;

namespace IsoSwitch.Api.Tcp;

public sealed class TcpIso8583Server : BackgroundService
{
    private readonly ILogger<TcpIso8583Server> _logger;
    private readonly IConfiguration _cfg;
    private readonly IServiceProvider _sp;

    public TcpIso8583Server(ILogger<TcpIso8583Server> logger, IConfiguration cfg, IServiceProvider sp)
    {
        _logger = logger;
        _cfg = cfg;
        _sp = sp;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var port = _cfg.GetValue<int?>("Tcp:Iso8583Port") ?? 7000;
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        _logger.LogInformation("TCP ISO8583 server listening on {Port} (len-prefix, optional TPDU)", port);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                _ = HandleClientAsync(client, stoppingToken);
            }
        }
        catch (OperationCanceledException) { }
        finally { listener.Stop(); }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var pub = scope.ServiceProvider.GetRequiredService<SwitchEventPublisher>();
        var useTpdu = _cfg.GetValue<bool?>("Tcp:UseTpdu") ?? false;
        var tokenSvc = scope.ServiceProvider.GetRequiredService<ITokenPanService>();
        var audit = scope.ServiceProvider.GetRequiredService<BinaryIsoAuditService>();

        await using var stream = client.GetStream();
        _logger.LogInformation("TCP client connected");

        while (!ct.IsCancellationRequested && client.Connected)
        {
            byte[] framedPayload;
            try
            {
                if (!Iso8583Binary.TryReadFrame(stream, out framedPayload)) break;
            }
            catch { break; }

            byte[]? tpdu;
            try
            {
                var req = Iso8583Binary.Parse(framedPayload, useTpdu, out tpdu);

                IsoTraceStore.Add(new IsoTraceStore.IsoTrace(
                    Key: $"{req.Stan}|{req.Rrn}",
                    When: DateTimeOffset.UtcNow,
                    Direction: "IN",
                    Mti: req.Mti,
                    Stan: req.Stan,
                    Rrn: req.Rrn,
                    PayloadHex: Convert.ToHexString(framedPayload),
                    TpduHex: tpdu is null ? null : Convert.ToHexString(tpdu)
                ));

                await audit.LogBinaryAsync($"{req.Stan}|{req.Rrn}", "IN", req.Mti, req.Stan, req.Rrn, Convert.ToHexString(framedPayload), tpdu is null ? null : Convert.ToHexString(tpdu), new { tokenPan = (string?)null, amount = req.Amount, mcc = req.Mcc18, terminalId = req.TerminalId41, acceptorId = req.AcceptorId42, currency = req.Currency49 }, ct);


                if (string.IsNullOrWhiteSpace(req.Pan) || !PanUtils.IsValidLuhn(req.Pan))
                {
                    var invalid = new IsoResponseBuilder.SwitchAuthResponse(
                        Network: "VISA", Mti: req.Mti, Stan: req.Stan, Rrn: req.Rrn, Amount: req.Amount,
                        ResponseCode: "14", Reason: "INVALID_PAN",
                        TerminalId: req.TerminalId41, AcceptorId: req.AcceptorId42, Mcc: req.Mcc18, Currency: req.Currency49,
                        TransmissionDateTime7: req.TransmissionDateTime7, LocalTime12: req.LocalTime12, LocalDate13: req.LocalDate13
                    );

                    var msg = (req.Mti == "0200")
                        ? IsoResponseBuilder.Build0210Binary(invalid)
                        : IsoResponseBuilder.Build0110Binary(invalid);

                    var payloadOut = msg.PackPayload();
                    var outBytes = useTpdu && tpdu is not null ? tpdu.Concat(payloadOut).ToArray() : payloadOut;
                    var frame = Iso8583Binary.BuildFrame(outBytes);

                    IsoTraceStore.Add(new IsoTraceStore.IsoTrace($"{req.Stan}|{req.Rrn}", DateTimeOffset.UtcNow, "OUT", msg.Mti, req.Stan, req.Rrn, Convert.ToHexString(outBytes), tpdu is null ? null : Convert.ToHexString(tpdu)));
                    await stream.WriteAsync(frame, ct);
                    continue;
                }

                var tokenPan = tokenSvc.Tokenize(req.Pan);
                var panMasked = PanUtils.Mask(req.Pan);

                // Resolve network by BIN
                var network = "VISA";
                var currency = req.Currency49;
                if (BinRoutingStore.TryResolve(req.Pan, out var route) && route is not null)
                {
                    network = route.Network;
                    currency = string.IsNullOrWhiteSpace(req.Currency49) ? route.Currency : req.Currency49;
                }

                // TokenPAN -> AccountId mapping
                if (!PanMapStore.TryGetAccount(tokenPan, out var accountId))
                {
                    var decline = new IsoResponseBuilder.SwitchAuthResponse(
                        Network: network, Mti: req.Mti, Stan: req.Stan, Rrn: req.Rrn, Amount: req.Amount,
                        ResponseCode: "05", Reason: "TOKENPAN_NOT_MAPPED",
                        TerminalId: req.TerminalId41, AcceptorId: req.AcceptorId42, Mcc: req.Mcc18, Currency: currency,
                        TransmissionDateTime7: req.TransmissionDateTime7, LocalTime12: req.LocalTime12, LocalDate13: req.LocalDate13
                    );

                    var msg = (req.Mti == "0200")
                        ? IsoResponseBuilder.Build0210Binary(decline)
                        : IsoResponseBuilder.Build0110Binary(decline);

                    var payloadOut = msg.PackPayload();
                    var outBytes = useTpdu && tpdu is not null ? tpdu.Concat(payloadOut).ToArray() : payloadOut;
                    var frame = Iso8583Binary.BuildFrame(outBytes);

                    IsoTraceStore.Add(new IsoTraceStore.IsoTrace($"{req.Stan}|{req.Rrn}", DateTimeOffset.UtcNow, "OUT", msg.Mti, req.Stan, req.Rrn, Convert.ToHexString(outBytes), tpdu is null ? null : Convert.ToHexString(tpdu)));
                    await stream.WriteAsync(frame, ct);
                    continue;
                }

                if (req.Mti == "0200")
                {
                    var env = new
                    {
                        eventName = "switch.v1.clearing.posted",
                        eventId = Guid.NewGuid().ToString("N"),
                        occurredOn = DateTimeOffset.UtcNow,
                        payload = new
                        {
                            accountId,
                            tokenPan,
                            panMasked,
                            amount = req.Amount,
                            network,
                            mti = "0200",
                            stan = req.Stan,
                            rrn = req.Rrn,
                            acceptorId = req.AcceptorId42,
                            terminalId = req.TerminalId41,
                            mcc = req.Mcc18,
                            currency,
                            de7 = req.TransmissionDateTime7,
                            de12 = req.LocalTime12,
                            de13 = req.LocalDate13,
                            track2 = req.Track2_35,
                            pinBlock = req.PinBlock52,
                            emv55 = req.Emv55,
                            postedOn = DateTimeOffset.UtcNow
                        }
                    };

                    await pub.PublishTxAsync(accountId.ToString("N"), env, ct);

                    var ok = new IsoResponseBuilder.SwitchAuthResponse(
                        Network: network, Mti: "0200", Stan: req.Stan, Rrn: req.Rrn, Amount: req.Amount,
                        ResponseCode: "00", Reason: "POSTED",
                        TerminalId: req.TerminalId41, AcceptorId: req.AcceptorId42, Mcc: req.Mcc18, Currency: currency,
                        TransmissionDateTime7: req.TransmissionDateTime7, LocalTime12: req.LocalTime12, LocalDate13: req.LocalDate13
                    );

                    var resp = IsoResponseBuilder.Build0210Binary(ok);
                    var payloadOut = resp.PackPayload();
                    var outBytes = useTpdu && tpdu is not null ? tpdu.Concat(payloadOut).ToArray() : payloadOut;
                    var frame = Iso8583Binary.BuildFrame(outBytes);

                    IsoTraceStore.Add(new IsoTraceStore.IsoTrace($"{req.Stan}|{req.Rrn}", DateTimeOffset.UtcNow, "OUT", resp.Mti, req.Stan, req.Rrn, Convert.ToHexString(outBytes), tpdu is null ? null : Convert.ToHexString(tpdu)));
                    await audit.LogBinaryAsync($"{req.Stan}|{req.Rrn}", "OUT", resp.Mti, req.Stan, req.Rrn, Convert.ToHexString(outBytes), tpdu is null ? null : Convert.ToHexString(tpdu), new { responseCode = "00", tokenPan, panMasked, network, currency }, ct);
                    await stream.WriteAsync(frame, ct);
                    continue;
                }

                // 0100 auth
                var wait = SwitchResponseAwaiter.Register(req.Stan, req.Rrn, TimeSpan.FromSeconds(5), ct);

                var envAuth = new
                {
                    eventName = "switch.v1.auth.approved",
                    eventId = Guid.NewGuid().ToString("N"),
                    occurredOn = DateTimeOffset.UtcNow,
                    payload = new
                    {
                        accountId,
                        tokenPan,
                        panMasked,
                        amount = req.Amount,
                        network,
                        mti = "0100",
                        stan = req.Stan,
                        rrn = req.Rrn,
                        acceptorId = req.AcceptorId42,
                        terminalId = req.TerminalId41,
                        mcc = req.Mcc18,
                        currency,
                        de7 = req.TransmissionDateTime7,
                        de12 = req.LocalTime12,
                        de13 = req.LocalDate13,
                        track2 = req.Track2_35,
                        pinBlock = req.PinBlock52,
                        emv55 = req.Emv55,
                        postedOn = DateTimeOffset.UtcNow
                    }
                };

                await pub.PublishTxAsync(accountId.ToString("N"), envAuth, ct);

                var decision = await wait;

                var respMsg = IsoResponseBuilder.Build0110Binary(new IsoResponseBuilder.SwitchAuthResponse(
                    Network: decision.Network,
                    Mti: decision.Mti,
                    Stan: decision.Stan,
                    Rrn: decision.Rrn,
                    Amount: decision.Amount,
                    ResponseCode: decision.ResponseCode,
                    Reason: decision.Reason,
                    TerminalId: req.TerminalId41,
                    AcceptorId: req.AcceptorId42,
                    Mcc: req.Mcc18,
                    Currency: currency,
                    TransmissionDateTime7: req.TransmissionDateTime7,
                    LocalTime12: req.LocalTime12,
                    LocalDate13: req.LocalDate13
                ));

                var payloadResp = respMsg.PackPayload();
                var outAll = useTpdu && tpdu is not null ? tpdu.Concat(payloadResp).ToArray() : payloadResp;
                var frameOut = Iso8583Binary.BuildFrame(outAll);

                IsoTraceStore.Add(new IsoTraceStore.IsoTrace($"{req.Stan}|{req.Rrn}", DateTimeOffset.UtcNow, "OUT", respMsg.Mti, req.Stan, req.Rrn, Convert.ToHexString(outAll), tpdu is null ? null : Convert.ToHexString(tpdu)));
                await audit.LogBinaryAsync($"{req.Stan}|{req.Rrn}", "OUT", respMsg.Mti, req.Stan, req.Rrn, Convert.ToHexString(outAll), tpdu is null ? null : Convert.ToHexString(tpdu), new { responseCode = decision.ResponseCode, tokenPan, panMasked, network, currency }, ct);
                await stream.WriteAsync(frameOut, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process ISO frame");
                var sys = new Iso8583MessageBinary("0110").Set(39, "96").PackPayload();
                var frame = Iso8583Binary.BuildFrame(sys);
                await stream.WriteAsync(frame, ct);
            }
        }

        _logger.LogInformation("TCP client disconnected");
    }
}
