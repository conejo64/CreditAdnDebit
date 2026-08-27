using System;

namespace IsoSwitch.Infrastructure.SwitchIso8583.Iso;

public sealed class DatafastIso8583Packager : IIso8583Packager
{
    public byte[] Encode(IsoMessage msg)
    {
        // Skeleton for Datafast formatting. 
        // In real implementation, this would handle Datafast-specific bitmaps and field lengths.
        return SimpleIso8583Packager.Pack(msg);
    }
    
    public IsoMessage Decode(ReadOnlySpan<byte> payload)
    {
        return SimpleIso8583Packager.Unpack(payload.ToArray());
    }
}
