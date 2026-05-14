using System.Security.Cryptography;
using System.Text;

namespace IsoSwitch.Api.Security;

public interface ITokenPanService
{
    string Tokenize(string pan);
}

public sealed class TokenPanService : ITokenPanService
{
    private readonly string _secret;

    public TokenPanService(IConfiguration cfg)
    {
        _secret = cfg["Tokenization:Secret"] ?? "DEV_ONLY_CHANGE_ME";
    }

    public string Tokenize(string pan)
    {
        // Deterministic token: TPAN_<first24hex(HMACSHA256(secret, pan))>
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(pan.Trim()));
        var hex = Convert.ToHexString(hash);
        return "TPAN_" + hex[..24];
    }
}
