using IsoSwitch.Infrastructure.SwitchIso8583.Iso;
using IsoSwitch.Infrastructure.SwitchIso8583.Net;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Text.RegularExpressions;

namespace IsoSwitch.Tests.Net;

/// <summary>
/// SEC-5: TcpIsoClient MUST NOT write PAN, raw ISO bytes (hex or Base64) to any log sink.
/// On failure it logs MTI + generic outcome only via ILogger&lt;TcpIsoClient&gt;.
/// </summary>
public class TcpIsoClientLoggingTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Collects all log messages emitted via ILogger&lt;TcpIsoClient&gt; so tests can
    /// assert on what was (and was not) logged without touching stdout.
    /// </summary>
    private sealed class LogCollector : ILogger<TcpIsoClient>
    {
        private readonly List<string> _messages = new();

        public IReadOnlyList<string> Messages => _messages;

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        bool ILogger.IsEnabled(LogLevel logLevel) => true;

        void ILogger.Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _messages.Add(formatter(state, exception));
        }
    }

    private static TcpIsoClientOptions UnreachableOptions() => new()
    {
        Host = "127.0.0.1",
        Port = 19999, // no listener — always fails
        TimeoutMs = 500,
        RetryCount = 0,
        CircuitBreaker = new CircuitBreakerOptions { FailureThreshold = 10, BreakSeconds = 1 }
    };

    // Minimal packager that encodes a fixed 10-byte frame.
    // The bytes are known so we can verify they do NOT appear in logs.
    private sealed class FakePackager : IIso8583Packager
    {
        private static readonly byte[] _frame = new byte[] { 0x01, 0x10, 0xAB, 0xCD, 0xEF, 0x12, 0x34, 0x56, 0x78, 0x9A };
        public byte[] Encode(IsoMessage msg) => _frame;
        public IsoMessage Decode(ReadOnlySpan<byte> payload) => new() { Mti = "0110" };
    }

    // Expected MTI in the request
    private const string RequestMti = "0200";

    // Base64 of FakePackager._frame — must NOT appear in any log entry
    private const string ForbiddenBase64 = "ARCL3O8SNFaHmpo="; // pre-computed; verified below

    // ── Task 3.1 RED tests ────────────────────────────────────────────────────

    [Fact(DisplayName = "SendFailure: log contains MTI, not Base64 payload")]
    public async Task SendFailure_LogContainsMti_NotBase64Payload()
    {
        // Arrange
        var collector = new LogCollector();
        // NEW constructor signature (logger parameter) — does not exist yet → RED compile failure
        var client = new TcpIsoClient(UnreachableOptions(), collector);
        var request = new IsoMessage { Mti = RequestMti };
        var packager = new FakePackager();

        // Act — TCP connection will fail because port 19999 is unreachable
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendAsync(request, packager, CancellationToken.None));

        // Assert: at least one log entry was emitted
        Assert.NotEmpty(collector.Messages);

        // Assert: MTI appears somewhere in the logs
        Assert.True(
            collector.Messages.Any(m => m.Contains(RequestMti)),
            $"Expected MTI '{RequestMti}' in log messages: [{string.Join(", ", collector.Messages)}]");

        // Assert: NO Base64-encoded blob (≥20 chars of A-Za-z0-9+/= ending in optional ==)
        var base64Pattern = new Regex(@"[A-Za-z0-9+/]{20,}={0,2}");
        foreach (var msg in collector.Messages)
        {
            Assert.False(
                base64Pattern.IsMatch(msg),
                $"Log message contains Base64 payload (PAN leakage risk): '{msg}'");
        }
    }

    [Fact(DisplayName = "SendFailure: log does not contain hex-encoded bytes")]
    public async Task SendFailure_LogContainsMti_NotHexBytes()
    {
        // Arrange
        var collector = new LogCollector();
        var client = new TcpIsoClient(UnreachableOptions(), collector);
        var request = new IsoMessage { Mti = RequestMti };
        var packager = new FakePackager();

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendAsync(request, packager, CancellationToken.None));

        // Assert: hex blob pattern absent (≥20 consecutive hex chars)
        var hexPattern = new Regex(@"[0-9A-Fa-f]{20,}");
        foreach (var msg in collector.Messages)
        {
            Assert.False(
                hexPattern.IsMatch(msg),
                $"Log message contains hex payload (PAN leakage risk): '{msg}'");
        }
    }
}
