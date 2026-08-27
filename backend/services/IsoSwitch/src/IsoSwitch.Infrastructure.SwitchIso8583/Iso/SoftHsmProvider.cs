using System;
using System.Security.Cryptography;
using System.Text;

namespace IsoSwitch.Infrastructure.SwitchIso8583.Iso;

public class SoftHsmProvider : IHsmService
{
    public string ComputeMacHex(string payloadAscii)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(payloadAscii));
        return Convert.ToHexString(bytes)[..16];
    }

    public bool TryParsePinBlock(string encryptedPinBlock, string accountInfo, out string clearPin)
    {
        if (string.IsNullOrWhiteSpace(encryptedPinBlock))
        {
            clearPin = string.Empty;
            return false;
        }

        // Basic placeholder logic for local testing
        clearPin = "1234";
        return true;
    }
}
