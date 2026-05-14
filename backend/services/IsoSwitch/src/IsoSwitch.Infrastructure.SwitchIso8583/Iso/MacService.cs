using System.Security.Cryptography;
using System.Text;

namespace IsoSwitch.Infrastructure.SwitchIso8583.Iso;

/// <summary>
/// Placeholder MAC service.
/// In production, compute ISO9797/ANSI X9.19 or network-specific MAC using keys from HSM.
/// </summary>
public sealed class MacService : IMacService
{
    public string ComputeMacHex(string payloadAscii)
    {
        // demo: stable 16 hex chars derived from SHA256
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(payloadAscii));
        return Convert.ToHexString(bytes)[..16];
    }
}