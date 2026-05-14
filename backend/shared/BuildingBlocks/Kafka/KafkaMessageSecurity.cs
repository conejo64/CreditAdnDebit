using System.Security.Cryptography;
using System.Text;
using Confluent.Kafka;

namespace BuildingBlocks.Kafka;

public static class KafkaMessageSecurity
{
    public const string SignatureHeader = "x-signature";
    public const string SignatureAlgHeader = "x-signature-alg";

    public static void Sign(string payloadJson, Headers headers, string secret)
    {
        var sig = ComputeHmacSha256(payloadJson, secret);
        headers.Remove(SignatureAlgHeader);
        headers.Remove(SignatureHeader);
        headers.Add(SignatureAlgHeader, Encoding.ASCII.GetBytes("HMAC-SHA256"));
        headers.Add(SignatureHeader, Encoding.ASCII.GetBytes(sig));
    }

    public static bool Verify(string payloadJson, Headers headers, string secret)
    {
        var got = GetAscii(headers, SignatureHeader);
        if (string.IsNullOrWhiteSpace(got)) return false;
        var expected = ComputeHmacSha256(payloadJson, secret);
        return FixedTimeEquals(got, expected);
    }

    private static string ComputeHmacSha256(string payload, string secret)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        using var h = new HMACSHA256(key);
        var bytes = h.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var ab = Encoding.ASCII.GetBytes(a);
        var bb = Encoding.ASCII.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(ab, bb);
    }

    private static string? GetAscii(Headers headers, string key)
    {
        var last = headers.LastOrDefault(h => h.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (last is null) return null;
        return last.GetValueBytes() is { Length: > 0 } b ? Encoding.ASCII.GetString(b) : null;
    }
}
