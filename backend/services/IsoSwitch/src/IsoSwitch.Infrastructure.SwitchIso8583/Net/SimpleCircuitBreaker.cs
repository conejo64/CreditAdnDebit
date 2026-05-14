namespace IsoSwitch.Infrastructure.SwitchIso8583.Net;

public sealed class SimpleCircuitBreaker
{
    private readonly object _lock = new();
    private readonly int _failureThreshold;
    private readonly TimeSpan _breakDuration;

    private int _failures;
    private DateTimeOffset? _openUntil;

    public SimpleCircuitBreaker(int failureThreshold, TimeSpan breakDuration)
    {
        _failureThreshold = Math.Max(1, failureThreshold);
        _breakDuration = breakDuration <= TimeSpan.Zero ? TimeSpan.FromSeconds(5) : breakDuration;
    }

    public void ThrowIfOpen()
    {
        lock (_lock)
        {
            if (_openUntil is null) return;
            if (DateTimeOffset.UtcNow < _openUntil.Value)
                throw new InvalidOperationException($"Circuit open until {_openUntil:O}");
            // half-open: allow next call
            _openUntil = null;
            _failures = 0;
        }
    }

    public void OnSuccess()
    {
        lock (_lock)
        {
            _failures = 0;
            _openUntil = null;
        }
    }

    public void OnFailure()
    {
        lock (_lock)
        {
            _failures++;
            if (_failures >= _failureThreshold)
            {
                _openUntil = DateTimeOffset.UtcNow.Add(_breakDuration);
            }
        }
    }
}