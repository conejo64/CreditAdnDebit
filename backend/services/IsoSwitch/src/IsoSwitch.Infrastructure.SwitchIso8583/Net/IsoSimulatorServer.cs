using IsoSwitch.Infrastructure.SwitchIso8583.Iso;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace IsoSwitch.Infrastructure.SwitchIso8583.Net;

/// <summary>
/// Simple TCP ISO8583 simulator:
/// - Receives 0100 authorization and returns 0110 with response code 00
/// - Receives 0400 reversal and returns 0410 with response code 00
/// Uses 2-byte length prefix framing.
/// </summary>
public sealed class IsoSimulatorServer : BackgroundService
{
    private readonly ILogger<IsoSimulatorServer> _logger;
    private readonly IsoSimulatorOptions _opt;
    private readonly int _port;

    public IsoSimulatorServer(ILogger<IsoSimulatorServer> logger, IConfiguration cfg, IsoSimulatorOptions opt)
    {
        _logger = logger;
        _opt = opt;
        _port = cfg.GetValue<int>("IsoSimulator:Port", 5005);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, _port);
        try
        {
            listener.Start();
            _logger.LogInformation("ISO Simulator listening on 127.0.0.1:{Port}", _port);
        }
        catch (SocketException ex)
        {
            _logger.LogError(ex, "Failed to start ISO Simulator on port {Port}. Port might be in use.", _port);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var client = await listener.AcceptTcpClientAsync(stoppingToken);
            _ = Task.Run(() => HandleClientAsync(client, stoppingToken), stoppingToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            await using var stream = client.GetStream();
            var payload = await ReadFrameAsync(stream, ct);
            var req = Iso8583Codec.Decode(payload);

            _logger.LogInformation("ISO Simulator received MTI {Mti} fields={Count}", req.Mti, req.Fields.Count);

            var resp = new IsoMessage
            {
                Mti = req.Mti switch
                {
                    "0100" => "0110",
                    "0200" => "0210",
                    "0400" => "0410",
                    _ => "0810"
                }
            };

            // echo some fields
            if (req.Fields.TryGetValue(11, out var stan)) resp.Set(11, stan);
            if (req.Fields.TryGetValue(41, out var tid)) resp.Set(41, tid);
            if (req.Fields.TryGetValue(42, out var mid)) resp.Set(42, mid);
            if (req.Fields.TryGetValue(49, out var cur)) resp.Set(49, cur);

            var rc = ComputeResponseCode(req);
            resp.Set(39, rc);

            if (_opt.LatencyMs > 0)
                await Task.Delay(_opt.LatencyMs, ct);

            // echo MAC placeholders if present
            if (req.Fields.TryGetValue(64, out var mac64)) resp.Set(64, mac64);
            if (req.Fields.TryGetValue(128, out var mac128)) resp.Set(128, mac128);

            var outPayload = Iso8583Codec.Encode(resp);
            var frame = Frame(outPayload);
            await stream.WriteAsync(frame, ct);
            await stream.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ISO Simulator error");
        }
        finally
        {
            client.Close();
        }
    }

    private static byte[] Frame(byte[] payload)
    {
        var len = (ushort)payload.Length;
        return new[] { (byte)(len >> 8), (byte)(len & 0xFF) }.Concat(payload).ToArray();
    }

    private static async Task<byte[]> ReadFrameAsync(NetworkStream stream, CancellationToken ct)
    {
        var header = await ReadExactAsync(stream, 2, ct);
        var len = (header[0] << 8) | header[1];
        return await ReadExactAsync(stream, len, ct);
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, count - read), ct);
            if (n == 0) throw new InvalidOperationException("Connection closed");
            read += n;
        }
        return buffer;
    }

    private static string ComputeResponseCode(IsoMessage req)
    {
        return req.Mti.StartsWith("04") ? "00" : "00"; // Approve all by default for simulator
    }
}