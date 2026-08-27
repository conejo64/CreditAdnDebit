namespace IsoSwitch.Infrastructure.SwitchIso8583.Iso;

public interface IHsmService
{
    string ComputeMacHex(string payloadAscii);
    bool TryParsePinBlock(string encryptedPinBlock, string accountInfo, out string clearPin);
}
