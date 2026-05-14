using System.Security.Cryptography;
using System.Text;

namespace IsoSwitch.Api.Security;

public static class PanUtils
{
    public static bool IsValidLuhn(string? pan)
    {
        if (string.IsNullOrWhiteSpace(pan)) return false;
        var digits = pan.Where(char.IsDigit).Select(c => c - '0').ToArray();
        if (digits.Length < 12) return false;

        int sum = 0;
        bool alt = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int d = digits[i];
            if (alt)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
            alt = !alt;
        }
        return (sum % 10) == 0;
    }

    public static string Mask(string pan)
    {
        if (string.IsNullOrWhiteSpace(pan) || pan.Length < 10) return "****";
        var last4 = pan[^4..];
        var bin6 = pan[..6];
        return $"{bin6}******{last4}";
    }

    public static string Bin6(string pan) => pan.Length >= 6 ? pan[..6] : "";
    public static string Last4(string pan) => pan.Length >= 4 ? pan[^4..] : "";
}
