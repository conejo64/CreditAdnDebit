namespace IsoSwitch.Api;

public static class IdempotencyExtensions
{
    public const string HeaderName = "Idempotency-Key";

    public static string? GetIdempotencyKey(this HttpRequest req)
    {
        if (!req.Headers.TryGetValue(HeaderName, out var v)) return null;
        var s = v.ToString().Trim();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }
}