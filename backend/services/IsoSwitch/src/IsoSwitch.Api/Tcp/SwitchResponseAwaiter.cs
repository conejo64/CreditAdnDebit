using System.Collections.Concurrent;
using IsoSwitch.Api.Iso8583;

namespace IsoSwitch.Api.Tcp;

public static class SwitchResponseAwaiter
{
    private static readonly ConcurrentDictionary<string, TaskCompletionSource<IsoResponseBuilder.SwitchAuthResponse>> _pending = new();

    public static string Key(string stan, string rrn) => $"{stan}|{rrn}";

    public static Task<IsoResponseBuilder.SwitchAuthResponse> Register(string stan, string rrn, TimeSpan timeout, CancellationToken ct)
    {
        var key = Key(stan, rrn);
        var tcs = new TaskCompletionSource<IsoResponseBuilder.SwitchAuthResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[key] = tcs;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(timeout, ct);
                if (_pending.TryRemove(key, out var p))
                    p.TrySetException(new TimeoutException("Timeout waiting for auth response"));
            }
            catch { }
        });

        return tcs.Task;
    }

    public static void TryComplete(IsoResponseBuilder.SwitchAuthResponse res)
    {
        var key = Key(res.Stan, res.Rrn);
        if (_pending.TryRemove(key, out var tcs))
            tcs.TrySetResult(res);
    }
}
