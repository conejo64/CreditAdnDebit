namespace IsoSwitch.Infrastructure.SwitchIso8583.Net;

public sealed class TcpIsoClientOptions
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 5000;

    public bool UseTls { get; set; } = true;
    public bool AllowInvalidCert { get; set; } = false; // ADR-7: must be explicitly opted in via config

    public int TimeoutMs { get; set; } = 3000;

    public int RetryCount { get; set; } = 1; // total attempts = 1 + RetryCount

    public CircuitBreakerOptions CircuitBreaker { get; set; } = new();
}

public sealed class CircuitBreakerOptions
{
    public int FailureThreshold { get; set; } = 5;
    public int BreakSeconds { get; set; } = 15;
}