using IsoSwitch.Infrastructure.SwitchIso8583.Iso;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

namespace IsoSwitch.Api;

public sealed class IsoSimulatorServer : BackgroundService
{
    private readonly ILogger<IsoSimulatorServer> _logger;
    private readonly int _port;

    public IsoSimulatorServer(IConfiguration cfg, ILogger<IsoSimulatorServer> logger)
    {
        _logger = logger;
        _port = cfg.GetValue<int>("IsoSimulator:Port", 5000);
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
            _logger.LogError(ex, "Failed to start ISO Simulator on port {Port}. Port might be in use. Simulator disabled.", _port);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    using var c = client;
                    using var stream = c.GetStream();

                    // read header
                    var header = new byte[2];
                    await ReadExactAsync(stream, header, stoppingToken);
                    var len = (header[0] << 8) | header[1];

                    var payload = new byte[len];
                    await ReadExactAsync(stream, payload, stoppingToken);
                    var full = header.Concat(payload).ToArray();

                    var req = SimpleIso8583Packager.Unpack(full);

                    // Build 0110 response
                    var resp = new IsoMessage { Mti = "0110" };
                    // Copy STAN (11) if present
                    if (req.Fields.TryGetValue(11, out var stan))
                        resp.Set(11, stan);

                    // Response code 00 approved
                    resp.Set(39, "00");

                    var respFrame = SimpleIso8583Packager.Pack(resp);
                    await stream.WriteAsync(respFrame, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Simulator error");
                }
            }, stoppingToken);
        }

        listener.Stop();
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), ct);
            if (n == 0) throw new IOException("Connection closed");
            read += n;
        }
    }
}