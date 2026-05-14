using System.Diagnostics;
using Confluent.Kafka;

namespace BuildingBlocks.Kafka;

public static class KafkaTracePropagation
{
    public const string TraceParentHeader = "traceparent";
    public const string TraceStateHeader = "tracestate";
    public const string RequestIdHeader = "x-request-id";

    public static void Inject(Activity? activity, Headers headers)
    {
        if (activity is null) return;

        headers.Remove(TraceParentHeader);
        headers.Add(TraceParentHeader, System.Text.Encoding.ASCII.GetBytes(activity.Id ?? string.Empty));

        if (!string.IsNullOrWhiteSpace(activity.TraceStateString))
        {
            headers.Remove(TraceStateHeader);
            headers.Add(TraceStateHeader, System.Text.Encoding.ASCII.GetBytes(activity.TraceStateString));
        }

        var reqId = activity.TraceId.ToString();
        headers.Remove(RequestIdHeader);
        headers.Add(RequestIdHeader, System.Text.Encoding.ASCII.GetBytes(reqId));
    }

    public static ActivityContext? Extract(Headers headers)
    {
        var tp = GetAscii(headers, TraceParentHeader);
        var ts = GetAscii(headers, TraceStateHeader);

        if (string.IsNullOrWhiteSpace(tp)) return null;

        if (ActivityContext.TryParse(tp, ts, out var ctx))
            return ctx;

        return null;
    }

    private static string? GetAscii(Headers headers, string key)
    {
        var last = headers.LastOrDefault(h => h.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (last is null) return null;
        return last.GetValueBytes() is { Length: > 0 } b ? System.Text.Encoding.ASCII.GetString(b) : null;
    }
}
