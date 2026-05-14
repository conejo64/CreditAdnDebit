using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace IsoSwitch.Api.Iso8583;

public sealed class Iso8583MessageBinary
{
    public string Mti { get; }
    private readonly SortedDictionary<int, string> _fields = new();

    public Iso8583MessageBinary(string mti)
    {
        if (mti.Length != 4) throw new ArgumentException("MTI must be 4 chars");
        Mti = mti;
    }

    public Iso8583MessageBinary Set(int de, string value)
    {
        _fields[de] = value ?? "";
        return this;
    }

    public byte[] PackPayload()
    {
        ulong bitmap = 0;
        foreach (var de in _fields.Keys)
        {
            if (de < 1 || de > 64) throw new NotSupportedException("Only primary bitmap supported");
            bitmap |= 1UL << (64 - de);
        }

        using var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes(Mti));

        var bmpBytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bmpBytes, bitmap);
        ms.Write(bmpBytes);

        foreach (var kv in _fields)
        {
            ms.Write(EncodeField(kv.Key, kv.Value));
        }

        return ms.ToArray();
    }

    public byte[] PackFrame() => Iso8583Binary.BuildFrame(PackPayload());

    private static byte[] EncodeField(int de, string v)
    {
        var ascii = de switch
        {
            2 => Llvar(v),
            3 => FixedNumeric(v, 6),
            4 => FixedNumeric(v, 12),
            7 => FixedNumeric(v, 10),
            11 => FixedNumeric(v, 6),
            12 => FixedNumeric(v, 6),
            13 => FixedNumeric(v, 4),
            18 => FixedNumeric(v, 4),
            35 => Llvar(v),              // Track2
            37 => FixedAlnum(v, 12),
            38 => FixedAlnum(v, 6),
            39 => FixedAlnum(v, 2),
            41 => FixedAlnum(v, 8),
            42 => FixedAlnum(v, 15),
            49 => FixedNumeric(v, 3),
            52 => FixedHexOrAlnum16(v),  // PIN block (demo)
            55 => Lllvar(v),             // EMV ICC data (demo)
            _ => v
        };
        return Encoding.ASCII.GetBytes(ascii);
    }

    private static string Llvar(string v)
    {
        if (v.Length > 99) v = v[..99];
        return v.Length.ToString("D2", CultureInfo.InvariantCulture) + v;
    }

    private static string Lllvar(string v)
    {
        if (v.Length > 999) v = v[..999];
        return v.Length.ToString("D3", CultureInfo.InvariantCulture) + v;
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

    private static string FixedHexOrAlnum16(string v)
    {
        var s = (v ?? "").Trim();
        if (s.Length > 16) s = s[..16];
        return s.PadRight(16, '0');
    }
}
