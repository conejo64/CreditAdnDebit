using System.Text;

namespace IsoSwitch.Infrastructure.SwitchIso8583.Iso;

/// <summary>
/// Minimal demo packager (NOT a full ISO8583 spec implementation).
/// Encodes MTI + key=value fields in a deterministic format with a 2-byte length prefix.
/// Use only for local testing; replace with real packager per acquirer specs.
/// </summary>
public static class SimpleIso8583Packager
{
    public static byte[] Pack(IsoMessage msg)
    {
        // MTI|f3=000000;f4=000000010000;f11=123456;...
        var sb = new StringBuilder();
        sb.Append(msg.Mti).Append('|');

        foreach (var kv in msg.Fields.OrderBy(k => k.Key))
        {
            sb.Append('f').Append(kv.Key).Append('=').Append(kv.Value).Append(';');
        }

        var payload = Encoding.ASCII.GetBytes(sb.ToString());
        var len = payload.Length;
        if (len > 65535) throw new InvalidOperationException("Payload too large");

        return new[] { (byte)(len >> 8), (byte)(len & 0xFF) }.Concat(payload).ToArray();
    }

    public static IsoMessage Unpack(byte[] frame)
    {
        if (frame.Length < 2) throw new InvalidOperationException("Frame too small");
        var len = (frame[0] << 8) | frame[1];
        var payload = Encoding.ASCII.GetString(frame, 2, len);

        var parts = payload.Split('|', 2);
        var mti = parts[0];

        var msg = new IsoMessage { Mti = mti };
        if (parts.Length == 2)
        {
            var fields = parts[1].Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var f in fields)
            {
                var eq = f.IndexOf('=');
                if (eq <= 1) continue;
                var keyPart = f.Substring(0, eq); // f11
                var val = f[(eq + 1)..];
                if (keyPart.StartsWith('f') && int.TryParse(keyPart[1..], out var fieldNo))
                    msg.Set(fieldNo, val);
            }
        }
        return msg;
    }
}