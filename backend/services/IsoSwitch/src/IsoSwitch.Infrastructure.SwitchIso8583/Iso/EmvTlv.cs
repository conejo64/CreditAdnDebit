using System.Globalization;
using System.Text;

namespace IsoSwitch.Infrastructure.SwitchIso8583.Iso;

/// <summary>
/// Minimal EMV TLV helpers for field 55.
/// Accepts/outputs HEX string TLV. This is a helper for demo/testing (not full EMV validation).
/// </summary>
public static class EmvTlv
{
    public static string Build(Dictionary<string, string> tagHexToValueHex)
    {
        var sb = new StringBuilder();
        foreach (var kv in tagHexToValueHex.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            var tag = kv.Key.ToUpperInvariant();
            var value = kv.Value.ToUpperInvariant();
            var len = value.Length / 2;
            sb.Append(tag);
            sb.Append(len.ToString("X2", CultureInfo.InvariantCulture));
            sb.Append(value);
        }
        return sb.ToString();
    }

    public static Dictionary<string, string> Parse(string tlvHex)
    {
        var s = tlvHex.ToUpperInvariant();
        var i = 0;
        var dict = new Dictionary<string, string>();
        while (i < s.Length)
        {
            // Tag: 1 or 2 bytes (very simplified)
            if (i + 2 > s.Length) break;
            var tag = s.Substring(i, 2);
            i += 2;

            // If tag indicates more bytes (0x1F), read next byte (simplified)
            var first = byte.Parse(tag, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if ((first & 0x1F) == 0x1F)
            {
                if (i + 2 > s.Length) break;
                tag += s.Substring(i, 2);
                i += 2;
            }

            if (i + 2 > s.Length) break;
            var lenHex = s.Substring(i, 2);
            i += 2;
            var len = int.Parse(lenHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

            var valueLen = len * 2;
            if (i + valueLen > s.Length) break;
            var val = s.Substring(i, valueLen);
            i += valueLen;

            dict[tag] = val;
        }
        return dict;
    }
}