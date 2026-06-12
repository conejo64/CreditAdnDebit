using IsoSwitch.Infrastructure.SwitchIso8583.Iso;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IsoSwitch.Infrastructure.SwitchIso8583.Net;

/// <summary>
/// TCP ISO client using 2-byte big-endian length prefix framing.
/// Supports optional TLS (SslStream), timeouts, basic retry and circuit breaker.
/// </summary>
public sealed class TcpIsoClient
{
    private readonly TcpIsoClientOptions _opt;
    private readonly SimpleCircuitBreaker _cb;
    private readonly ILogger<TcpIsoClient> _logger;

    public TcpIsoClient(TcpIsoClientOptions opt, ILogger<TcpIsoClient> logger)
    {
        _opt = opt;
        _logger = logger;
        _cb = new SimpleCircuitBreaker(opt.CircuitBreaker.FailureThreshold, TimeSpan.FromSeconds(opt.CircuitBreaker.BreakSeconds));
    }

    // Backwards-compatible ctor used by Program.cs (kept for callers that pass host/port/timeout)
    public TcpIsoClient(string host, int port, TimeSpan timeout)
        : this(
            new TcpIsoClientOptions { Host = host, Port = port, TimeoutMs = (int)timeout.TotalMilliseconds },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TcpIsoClient>.Instance)
    {
    }

    public async Task<IsoMessage> SendAsync(IsoMessage request, IIso8583Packager packager, CancellationToken ct)
    {
        _cb.ThrowIfOpen();

        var attempts = Math.Max(1, 1 + _opt.RetryCount);
        Exception? last = null;

        for (var i = 1; i <= attempts; i++)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromMilliseconds(_opt.TimeoutMs));

                using var tcp = new TcpClient();
                await tcp.ConnectAsync(_opt.Host, _opt.Port, cts.Token);

                await using var stream = tcp.GetStream();

                if (_opt.UseTls)
                {
                    var ssl = new SslStream(stream, false, ValidateServerCertificate);
                    await ssl.AuthenticateAsClientAsync(_opt.Host);
                    var respBytes = await SendFramedAsync(ssl, packager, request, cts.Token);
                    _cb.OnSuccess();
                    return respBytes;
                }
                else
                {
                    try
                    {
                        var resp = await SendFramedAsync(stream, packager, request, cts.Token);
                        _cb.OnSuccess();
                        return resp;
                    }
                    catch (Exception ex)
                    {
                        // SEC-5: log MTI and generic outcome only — never log encoded frame bytes
                        _logger.LogWarning(ex,
                            "ISO exchange failed mti={Mti} host={Host}:{Port}",
                            request.Mti, _opt.Host, _opt.Port);
                        throw;
                    }
                }
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                last = ex;
                _cb.OnFailure();
                // SEC-5: log MTI and generic outcome only — never log payload bytes
                _logger.LogWarning(ex,
                    "ISO exchange failed mti={Mti} host={Host}:{Port} attempt={Attempt}/{Attempts}",
                    request.Mti, _opt.Host, _opt.Port, i, attempts);
                if (i == attempts) break;
                // small backoff
                await Task.Delay(TimeSpan.FromMilliseconds(150 * i), ct);
                continue;
            }
        }

        throw new InvalidOperationException("TCP send failed", last);
    }

    private async Task<IsoMessage> SendFramedAsync(Stream stream, IIso8583Packager packager, IsoMessage request, CancellationToken ct)
    {
        var payload = packager.Encode(request);
        if (payload.Length > ushort.MaxValue) throw new InvalidOperationException("Payload too large");

        var len = (ushort)payload.Length;
        var header = new byte[2];
        header[0] = (byte)(len >> 8);
        header[1] = (byte)(len & 0xFF);

        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(payload, ct);
        await stream.FlushAsync(ct);

        var respLenBytes = await ReadExactAsync(stream, 2, ct);
        var respLen = (respLenBytes[0] << 8) | respLenBytes[1];
        if (respLen <= 0 || respLen > 65535) throw new InvalidOperationException("Invalid response length");

        var respPayload = await ReadExactAsync(stream, respLen, ct);

        // SEC-5: log MTI only — never log raw response bytes (hex or Base64)
        _logger.LogDebug("ISO response received mti={Mti}", request.Mti);

        return packager.Decode(respPayload);
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken ct)
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

    private bool ValidateServerCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
        => _opt.AllowInvalidCert || errors == SslPolicyErrors.None;

    private static bool IsTransient(Exception ex)
        => ex is TimeoutException
           || ex is OperationCanceledException
           || ex is SocketException
           || ex is IOException;
}
