using System.Buffers.Binary;
using System.Text;
using System.Net.Sockets;

namespace IsoSwitch.Api.Iso8583;

public static class Iso8583Binary
{
    public sealed record IsoRequest(
        string Mti,
        ulong Bitmap,
        string? Pan,
        string ProcessingCode,
        decimal Amount,
        string Stan,
        string Rrn,
        string? TransmissionDateTime7,
        string? LocalTime12,
        string? LocalDate13,
        string? TerminalId41,
        string? AcceptorId42,
        string? Mcc18,
        string Currency49,
        string? Track2_35,
        string? PinBlock52,
        string? Emv55
    );

    public static byte[] BuildFrame(byte[] payload)
    {
        if (payload.Length > ushort.MaxValue) throw new ArgumentException("Payload too large");
        var buf = new byte[payload.Length + 2];
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(0, 2), (ushort)payload.Length);
        Buffer.BlockCopy(payload, 0, buf, 2, payload.Length);
        return buf;
    }

    public static bool TryReadFrame(NetworkStream stream, out byte[] payload)
    {
        payload = Array.Empty<byte>();
        var lenBuf = new byte[2];
        int r = stream.Read(lenBuf, 0, 2);
        if (r == 0) return false;
        if (r < 2) throw new IOException("Incomplete length prefix");
        var len = BinaryPrimitives.ReadUInt16BigEndian(lenBuf);
        payload = new byte[len];
        int offset = 0;
        while (offset < len)
        {
            var n = stream.Read(payload, offset, len - offset);
            if (n == 0) throw new IOException("Unexpected socket close");
            offset += n;
        }
        return true;
    }

    public static IsoRequest Parse(byte[] payload, bool hasTpdu, out byte[]? tpdu)
    {
        tpdu = null;
        var pos0 = 0;
        if (hasTpdu)
        {
            if (payload.Length < 5) throw new ArgumentException("Invalid TPDU");
            tpdu = payload[..5];
            pos0 = 5;
        }

        if (payload.Length < pos0 + 12) throw new ArgumentException("Invalid ISO payload");
        var mti = Encoding.ASCII.GetString(payload, pos0 + 0, 4);
        var bitmap = BinaryPrimitives.ReadUInt64BigEndian(payload.AsSpan(pos0 + 4, 8));

        int pos = pos0 + 12;

        string? pan = null;
        string pc = "000000";
        string stan = "000000";
        string rrn = "000000000000";
        string? de7 = null;
        string? de12 = null;
        string? de13 = null;
        string? tid = null;
        string? aid = null;
        string? mcc = null;
        string cur = "840";
        decimal amount = 0m;
        string? track2 = null;
        string? pin = null;
        string? emv = null;

        for (int de = 2; de <= 64; de++)
        {
            if ((bitmap & (1UL << (64 - de))) == 0) continue;

            switch (de)
            {
                case 2:
                    (pan, pos) = ReadLlvar(payload, pos, 2);
                    break;
                case 3:
                    (pc, pos) = ReadFixedAscii(payload, pos, 6);
                    break;
                case 4:
                    var (a12, p2) = ReadFixedAscii(payload, pos, 12);
                    pos = p2;
                    amount = ParseAmount12(a12);
                    break;
                case 7:
                    (de7, pos) = ReadFixedAscii(payload, pos, 10);
                    break;
                case 11:
                    (stan, pos) = ReadFixedAscii(payload, pos, 6);
                    break;
                case 12:
                    (de12, pos) = ReadFixedAscii(payload, pos, 6);
                    break;
                case 13:
                    (de13, pos) = ReadFixedAscii(payload, pos, 4);
                    break;
                case 18:
                    (mcc, pos) = ReadFixedAscii(payload, pos, 4);
                    break;
                case 35:
                    (track2, pos) = ReadLlvar(payload, pos, 2);
                    break;
                case 37:
                    (rrn, pos) = ReadFixedAscii(payload, pos, 12);
                    break;
                case 41:
                    (tid, pos) = ReadFixedAscii(payload, pos, 8);
                    break;
                case 42:
                    (aid, pos) = ReadFixedAscii(payload, pos, 15);
                    break;
                case 49:
                    (cur, pos) = ReadFixedAscii(payload, pos, 3);
                    break;
                case 52:
                    (pin, pos) = ReadFixedAscii(payload, pos, 16);
                    break;
                case 55:
                    (emv, pos) = ReadLlvar(payload, pos, 3);
                    break;
                default:
                    throw new NotSupportedException($"DE{de} not supported in demo binary unpacker");
            }
        }

        return new IsoRequest(mti, bitmap, pan, pc, amount, stan, rrn, de7, de12, de13, tid, aid, mcc, cur, track2, pin, emv);
    }

    private static decimal ParseAmount12(string digits12)
    {
        var digits = new string(digits12.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits)) return 0m;
        var cents = long.Parse(digits);
        return cents / 100m;
    }

    private static (string, int) ReadFixedAscii(byte[] buf, int pos, int len)
    {
        var s = Encoding.ASCII.GetString(buf, pos, len);
        return (s.Trim(), pos + len);
    }

    private static (string, int) ReadLlvar(byte[] buf, int pos, int digits)
    {
        if (pos + digits > buf.Length) throw new ArgumentException("Invalid VAR");
        var lenStr = Encoding.ASCII.GetString(buf, pos, digits);
        if (!int.TryParse(lenStr, out var len)) throw new ArgumentException("Invalid VAR length");
        pos += digits;
        var s = Encoding.ASCII.GetString(buf, pos, len);
        return (s, pos + len);
    }
}
