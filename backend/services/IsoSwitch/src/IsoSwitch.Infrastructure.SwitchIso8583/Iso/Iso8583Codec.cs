using System.Globalization;
using System.Text;

namespace IsoSwitch.Infrastructure.SwitchIso8583.Iso;

/// <summary>
/// ISO8583 encoder/decoder with primary+optional secondary bitmap (1-128).
/// Demo payload ASCII:
///   MTI(4) + PrimaryBitmapHex(32) + [SecondaryBitmapHex(32)] + fields in ascending order.
/// Variable fields: LLVAR(2) / LLLVAR(3) by spec.
/// Transport framing (TCP): 2-byte big-endian length prefix (handled outside).
/// </summary>
public static class Iso8583Codec
{
    public static byte[] Encode(IsoMessage msg)
    {
        var fields = msg.Fields.Keys.Order().ToList();

        // determine if secondary bitmap needed
        var needsSecondary = fields.Any(f => f > 64);
        var primary = BuildBitmap(fields.Where(f => f <= 64).ToList(), primary: true);
        var secondary = needsSecondary ? BuildBitmap(fields.Where(f => f > 64).ToList(), primary: false) : null;

        if (needsSecondary)
        {
            // set bit 1 in primary to indicate secondary
            primary[0] |= 0x80;
        }

        var sb = new StringBuilder();
        sb.Append(msg.Mti);
        sb.Append(Convert.ToHexString(primary));

        if (secondary is not null)
            sb.Append(Convert.ToHexString(secondary));

        foreach (var field in fields)
        {
            if (!Iso8583Spec.Fields.TryGetValue(field, out var spec))
                throw new InvalidOperationException($"Missing spec for field {field}");

            var value = msg.Fields[field] ?? string.Empty;
            Validate(field, value, spec);

            if (spec.Variable)
            {
                var maxByDigits = spec.LenDigits == 2 ? 99 : 999;
                var max = Math.Min(spec.MaxLength ?? maxByDigits, maxByDigits);
                if (value.Length > max) throw new InvalidOperationException($"VAR too long for field {field} (max {max})");
                sb.Append(value.Length.ToString(spec.LenDigits == 2 ? "00" : "000", CultureInfo.InvariantCulture));
                sb.Append(value);
            }
            else if (spec.FixedLength.HasValue)
            {
                var len = spec.FixedLength.Value;
                if (value.Length > len) throw new InvalidOperationException($"Field {field} too long (max {len})");
                if (spec.DataType == IsoFieldDataType.N && spec.PadLeftNumeric)
                    value = value.PadLeft(len, '0');
                else
                    value = value.PadRight(len, ' ');
                sb.Append(value);
            }
            else
            {
                throw new InvalidOperationException($"Invalid spec for field {field}");
            }
        }

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    public static IsoMessage Decode(ReadOnlySpan<byte> payload)
    {
        var s = Encoding.ASCII.GetString(payload);
        if (s.Length < 4 + 16) throw new InvalidOperationException("Payload too short");

        var idx = 0;
        var mti = s.Substring(idx, 4); idx += 4;
        
        // Primary bitmap is always 16 hex characters (8 bytes / 64 bits)
        var primaryHex = s.Substring(idx, 16); idx += 16;
        var primary = Convert.FromHexString(primaryHex);

        var hasSecondary = (primary[0] & 0x80) != 0;
        byte[]? secondary = null;
        if (hasSecondary)
        {
            if (s.Length < idx + 16) throw new InvalidOperationException("Secondary bitmap missing");
            var secondaryHex = s.Substring(idx, 16); idx += 16;
            secondary = Convert.FromHexString(secondaryHex);
        }


        var fields = new List<int>();
        fields.AddRange(ParseBitmap(primary, primary: true));
        if (secondary is not null) fields.AddRange(ParseBitmap(secondary, primary: false));
        fields.Sort();

        var msg = new IsoMessage { Mti = mti };

        foreach (var field in fields)
        {
            if (!Iso8583Spec.Fields.TryGetValue(field, out var spec))
                throw new InvalidOperationException($"Missing spec for field {field}");

            if (spec.Variable)
            {
                var ld = spec.LenDigits;
                if (s.Length < idx + ld) throw new InvalidOperationException($"Truncated length for field {field}");
                var lenStr = s.Substring(idx, ld); idx += ld;

                if (!int.TryParse(lenStr, NumberStyles.None, CultureInfo.InvariantCulture, out var len))
                    throw new InvalidOperationException($"Invalid length for field {field}");

                var maxByDigits = ld == 2 ? 99 : 999;
                var max = Math.Min(spec.MaxLength ?? maxByDigits, maxByDigits);
                if (len < 0 || len > max) throw new InvalidOperationException($"Invalid VAR length for field {field} (len {len}, max {max})");

                if (s.Length < idx + len) throw new InvalidOperationException($"Truncated value for field {field}");
                var value = s.Substring(idx, len); idx += len;

                Validate(field, value, spec);
                msg.Set(field, value);
            }
            else if (spec.FixedLength.HasValue)
            {
                var len = spec.FixedLength.Value;
                if (s.Length < idx + len) throw new InvalidOperationException($"Truncated fixed value for field {field}");
                var value = s.Substring(idx, len); idx += len;

                // trim right spaces for non-numeric fixed values
                value = spec.DataType == IsoFieldDataType.N ? value.TrimStart('0') : value.TrimEnd(' ');
                if (string.IsNullOrEmpty(value) && spec.DataType == IsoFieldDataType.N) value = "0";

                Validate(field, value, spec);
                msg.Set(field, value);
            }
        }

        return msg;
    }

    private static void Validate(int field, string value, Iso8583Spec.FieldSpec spec)
    {
        if (spec.FixedLength.HasValue && value.Length > spec.FixedLength.Value)
            throw new InvalidOperationException($"Field {field} too long");

        if (spec.MaxLength.HasValue && value.Length > spec.MaxLength.Value)
            throw new InvalidOperationException($"Field {field} exceeds MaxLength");

        switch (spec.DataType)
        {
            case IsoFieldDataType.N:
                if (!value.All(char.IsDigit))
                    throw new InvalidOperationException($"Field {field} must be numeric");
                break;
            case IsoFieldDataType.AN:
                if (!value.All(c => char.IsLetterOrDigit(c) || c == ' '))
                    throw new InvalidOperationException($"Field {field} must be alphanumeric");
                break;
            case IsoFieldDataType.ANS:
                // Allow common printable ASCII; for track2 allow '=' and 'D'
                if (!value.All(c => c >= 0x20 && c <= 0x7E))
                    throw new InvalidOperationException($"Field {field} must be printable ASCII");
                break;
            case IsoFieldDataType.HEX:
                if (value.Length % 2 != 0) throw new InvalidOperationException($"Field {field} hex length must be even");
                if (!value.All(c => "0123456789ABCDEFabcdef".Contains(c)))
                    throw new InvalidOperationException($"Field {field} must be hex");
                break;
        }
    }

    private static byte[] BuildBitmap(List<int> fields, bool primary)
    {
        var bitmap = new byte[8];
        foreach (var f in fields)
        {
            if (primary)
            {
                if (f <= 0 || f > 64) continue;
                var bit = f - 1;
                bitmap[bit / 8] |= (byte)(1 << (7 - (bit % 8)));
            }
            else
            {
                if (f <= 64 || f > 128) continue;
                var bit = (f - 65); // 0..63
                bitmap[bit / 8] |= (byte)(1 << (7 - (bit % 8)));
            }
        }
        return bitmap;
    }

    private static List<int> ParseBitmap(byte[] bitmap, bool primary)
    {
        var result = new List<int>();
        for (int i = 0; i < 64; i++)
        {
            var mask = (byte)(1 << (7 - (i % 8)));
            if ((bitmap[i / 8] & mask) != 0)
            {
                var field = primary ? (i + 1) : (i + 65);
                if (!(primary && field == 1))
                    result.Add(field);
            }
        }
        return result;
    }
    private static (string primaryHex, string? secondaryHex) ComputeBitmaps(IsoMessage msg)
    {
        var fields = msg.Fields.Keys.Order().ToList();
        var needsSecondary = fields.Any(f => f > 64);
        var primary = BuildBitmap(fields.Where(f => f <= 64).ToList(), primary: true);
        var secondary = needsSecondary ? BuildBitmap(fields.Where(f => f > 64).ToList(), primary: false) : null;
        if (needsSecondary) primary[0] |= 0x80;
        return (Convert.ToHexString(primary), secondary is not null ? Convert.ToHexString(secondary) : null);
    }

    // v23 helpers for diagnostics
    public static string ComputePrimaryBitmapHex(IsoMessage msg)
    {
        var (p, _) = ComputeBitmaps(msg);
        return p;
    }

    public static string? ComputeSecondaryBitmapHexOrNull(IsoMessage msg)
    {
        var (_, s) = ComputeBitmaps(msg);
        return s;
    }
}
