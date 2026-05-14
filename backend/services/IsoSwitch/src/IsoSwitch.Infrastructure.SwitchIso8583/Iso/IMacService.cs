namespace IsoSwitch.Infrastructure.SwitchIso8583.Iso;

public interface IMacService
{
    string ComputeMacHex(string payloadAscii);
}
