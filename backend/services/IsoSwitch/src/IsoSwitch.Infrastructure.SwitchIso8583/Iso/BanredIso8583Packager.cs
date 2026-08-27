using System;

namespace IsoSwitch.Infrastructure.SwitchIso8583.Iso;

public sealed class BanredIso8583Packager : IIso8583Packager
{
    public byte[] Encode(IsoMessage msg)
    {
        // Skeleton for Banred formatting. 
        // In real implementation, this would handle Banred-specific bitmaps and field lengths.
        return SimpleIso8583Packager.Pack(msg);
    }
    
    public IsoMessage Decode(ReadOnlySpan<byte> payload)
    {
        return SimpleIso8583Packager.Unpack(payload.ToArray());
    }
}
