namespace IsoSwitch.Infrastructure.SwitchIso8583.Iso;

public interface IIso8583Packager
{
    byte[] Encode(IsoMessage msg);
    IsoMessage Decode(ReadOnlySpan<byte> payload);
}

public sealed class DefaultIso8583Packager : IIso8583Packager
{
    public byte[] Encode(IsoMessage msg) => Iso8583Codec.Encode(msg);
    public IsoMessage Decode(ReadOnlySpan<byte> payload) => Iso8583Codec.Decode(payload);
}