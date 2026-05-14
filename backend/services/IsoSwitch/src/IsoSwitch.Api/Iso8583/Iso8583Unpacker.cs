using System.Globalization;

namespace IsoSwitch.Api.Iso8583;

/// <summary>
/// Minimal ISO8583 unpacker for messages formatted as:
/// MTI(4) + BitmapHex(16) + fields in ascending order.
/// This matches Iso8583Message.PackAscii() v46.
/// </summary>
public static class Iso8583Unpacker
{
    public sealed record Iso0100(string Mti, string BitmapHex,
        string? Pan, string? ProcessingCode, decimal Amount, string Stan, string Rrn,
        string? TerminalId, string? AcceptorId, string? Mcc, string Currency);

    public static Iso0100 ParseAscii(string ascii)
    {
        if (ascii.Length < 20) throw new ArgumentException("Invalid ISO message length");
        var mti = ascii.Substring(0, 4);
        var bmpHex = ascii.Substring(4, 16);
        var bitmap = ulong.Parse(bmpHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        int pos = 20;
        string? de2 = null, de3=null, de37=null, de41=null, de42=null, de18=null, de49="840";
        decimal amount=0m; string stan="000000";

        bool Has(int de) => (bitmap & (1UL << (64 - de))) != 0;

        // parse in order subset
        if (Has(2))
        {
            var len = int.Parse(ascii.Substring(pos,2)); pos+=2;
            de2 = ascii.Substring(pos,len); pos+=len;
        }
        if (Has(3)) { de3 = ascii.Substring(pos,6); pos+=6; }
        if (Has(4)) { var a = ascii.Substring(pos,12); pos+=12; amount = decimal.Parse(a)/100m; }
        if (Has(7)) { pos+=10; } // ignore
        if (Has(11)) { stan = ascii.Substring(pos,6); pos+=6; }
        if (Has(12)) { pos+=6; }
        if (Has(13)) { pos+=4; }
        if (Has(18)) { de18 = ascii.Substring(pos,4); pos+=4; } // MCC (demo if present)
        if (Has(37)) { de37 = ascii.Substring(pos,12).Trim(); pos+=12; }
        if (Has(38)) { pos+=6; }
        if (Has(39)) { pos+=2; }
        if (Has(41)) { de41 = ascii.Substring(pos,8).Trim(); pos+=8; }
        if (Has(42)) { de42 = ascii.Substring(pos,15).Trim(); pos+=15; }
        if (Has(49)) { de49 = ascii.Substring(pos,3); pos+=3; }

        if (string.IsNullOrWhiteSpace(de37)) de37 = Guid.NewGuid().ToString("N")[..12];
        return new Iso0100(mti, bmpHex, de2, de3, amount, stan, de37!, de41, de42, de18, de49);
    }
}
