using System.Collections.Concurrent;
using IsoSwitch.Api.Persistence;

namespace IsoSwitch.Api.Tcp;

public static class IsoTraceStore
{
    public sealed record IsoTrace(string Key, DateTimeOffset When, string Direction, string Mti, string Stan, string Rrn, string PayloadHex, string? TpduHex);

    private static readonly ConcurrentQueue<IsoTrace> _traces = new();
    private static JsonFileStore? _store;

    public static void Configure(JsonFileStore store) => _store = store;

    public static void Add(IsoTrace t)
    {
        _traces.Enqueue(t);
        while (_traces.Count > 500 && _traces.TryDequeue(out _)) { }

        // Best-effort persistence
        if (_store is not null)
        {
            _ = _store.AppendJsonLineAsync("isotraces.jsonl", t, CancellationToken.None);
        }
    }

    public static IReadOnlyList<IsoTrace> GetLast(int take)
        => _traces.Reverse().Take(Math.Clamp(take, 1, 500)).ToList();
}
