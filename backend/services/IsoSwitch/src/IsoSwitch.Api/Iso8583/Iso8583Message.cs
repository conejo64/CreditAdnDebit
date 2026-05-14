using System.Globalization;
using System.Text;

namespace IsoSwitch.Api.Iso8583;

/// <summary>
/// Minimal ISO8583 packer for demo (ASCII). Builds MTI + primary bitmap + selected DE fields.
/// Supports fixed and LLVAR fields needed for v46.
/// This is NOT a full ISO8583 implementation; it's a focused encoder to demonstrate real bitmap+fields.
/// </summary>
public sealed class Iso8583Message
{
    public string Mti { get; }
    private readonly SortedDictionary<int, string> _fields = new();

    public Iso8583Message(string mti)
    {
        if (mti.Length != 4) throw new ArgumentException("MTI must be 4 chars.");
        Mti = mti;
    }

    public Iso8583Message Set(int de, string value)
    {
        _fields[de] = value ?? "";
        return this;
    }

    public bool Has(int de) => _fields.ContainsKey(de);

    public string PackAscii()
    {
        // primary bitmap (1..64). We keep it simple for demo.
        ulong bitmap = 0;

        foreach (var de in _fields.Keys)
        {
            if (de < 1 || de > 64) throw new NotSupportedException("Only DE 1..64 supported in demo");
            bitmap |= 1UL << (64 - de);
        }

        var sb = new StringBuilder();
        sb.Append(Mti);

        // bitmap as 16 hex chars
        sb.Append(bitmap.ToString("X16", CultureInfo.InvariantCulture));

        // append fields in numeric order
        foreach (var kv in _fields)
        {
            var de = kv.Key;
            var v = kv.Value;

            sb.Append(EncodeFieldAscii(de, v));
        }

        return sb.ToString();
    }

    public string PackHex()
    {
        var ascii = PackAscii();
        // represent ascii bytes as HEX for transport/inspection
        var bytes = Encoding.ASCII.GetBytes(ascii);
        return Convert.ToHexString(bytes);
    }

    private static string EncodeFieldAscii(int de, string v)
    {
        // Very small spec subset:
        // DE2 PAN: LLVAR numeric
        // DE3 ProcessingCode: fixed 6
        // DE4 Amount: fixed 12 numeric
        // DE7 TransmissionDateTime: fixed 10 (MMDDhhmmss)
        // DE11 STAN: fixed 6
        // DE12 LocalTime: fixed 6 (hhmmss)
        // DE13 LocalDate: fixed 4 (MMDD)
        // DE37 RRN: fixed 12
        // DE38 AuthIdResponse: fixed 6
        // DE39 ResponseCode: fixed 2
        // DE41 TerminalId: fixed 8
        // DE42 AcceptorId: fixed 15
        // DE49 CurrencyCode: fixed 3
        switch (de)
        {
            case 2:
                return Llvar(v);
            case 3:
                return FixedNumeric(v, 6);
            case 4:
                return FixedNumeric(v, 12);
            case 7:
                return FixedNumeric(v, 10);
            case 11:
                return FixedNumeric(v, 6);
            case 12:
                return FixedNumeric(v, 6);
            case 13:
                return FixedNumeric(v, 4);
            case 18:
                return FixedNumeric(v, 4);
            case 37:
                return FixedAlnum(v, 12);
            case 38:
                return FixedAlnum(v, 6);
            case 39:
                return FixedAlnum(v, 2);
            case 41:
                return FixedAlnum(v, 8);
            case 42:
                return FixedAlnum(v, 15);
            case 49:
                return FixedNumeric(v, 3);
            default:
                // For demo: treat as fixed alnum with no padding
                return v;
        }
    }

    private static string Llvar(string v)
    {
        if (v.Length > 99) throw new ArgumentException("LLVAR max 99");
        return v.Length.ToString("D2", CultureInfo.InvariantCulture) + v;
    }

    private static string FixedNumeric(string v, int len)
    {
        var digits = new string(v.Where(char.IsDigit).ToArray());
        if (digits.Length > len) digits = digits[^len..];
        return digits.PadLeft(len, '0');
    }

    private static string FixedAlnum(string v, int len)
    {
        var s = v ?? "";
        if (s.Length > len) s = s[..len];
        return s.PadRight(len, ' ');
    }
}
