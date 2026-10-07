using System.Security.Cryptography;
using System.Text;
using CardVault.Domain;

namespace CardVault.Application.Services;

/// <summary>
/// Server-side PAN construction. A PAN is <c>BIN + account identifier + Luhn check digit</c>; the account
/// identifier comes from <see cref="RandomNumberGenerator"/> so a card number can never be predicted
/// from an earlier one. This type is pure: BIN-range lookup against the catalog and the uniqueness retry
/// are the caller's responsibility (<see cref="IssuerService"/>).
/// </summary>
public static class PanGenerator
{
    /// <summary>Length issued today. ISO/IEC 7812 also allows 19 digits; that length is reserved, not implemented.</summary>
    public const int DefaultLength = 16;

    /// <summary>A BIN is the first 6 digits (legacy) or 8 digits (ISO/IEC 7812:2017) of the PAN.</summary>
    public static bool IsWellFormedBin(string? bin)
    {
        if (bin is null || bin.Length is not (6 or 8)) return false;

        foreach (var c in bin)
        {
            if (c is < '0' or > '9') return false;
        }

        return true;
    }

    /// <summary>
    /// Builds a Luhn-valid PAN of <paramref name="totalLength"/> digits that starts with <paramref name="bin"/>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="bin"/> is not 6 or 8 digits.</exception>
    /// <exception cref="NotSupportedException"><paramref name="totalLength"/> is not 16.</exception>
    public static string Generate(string bin, int totalLength = DefaultLength)
    {
        if (!IsWellFormedBin(bin))
            throw new ArgumentException("The BIN must be exactly 6 or 8 digits.", nameof(bin));

        if (totalLength != DefaultLength)
            throw new NotSupportedException($"Only {DefaultLength}-digit PANs are issued; {totalLength} digits is reserved for a later product decision.");

        var sb = new StringBuilder(totalLength).Append(bin);
        var freeDigits = totalLength - bin.Length - 1;
        for (var i = 0; i < freeDigits; i++)
        {
            sb.Append((char)('0' + RandomNumberGenerator.GetInt32(10)));
        }

        var partial = sb.ToString();
        return partial + Luhn.ComputeCheckDigit(partial);
    }

    /// <summary>
    /// Whether <paramref name="bin"/> falls inside the catalog range <c>[binStart, binEnd]</c>. Ranges are stored
    /// as integers at either 6 or 8 digits; the BIN is compared on its numeric prefix of the range's width, so a
    /// 6-digit range covers every 8-digit BIN that extends one of its values, while an 8-digit range can only
    /// match an 8-digit BIN.
    /// </summary>
    public static bool BinMatchesRange(string bin, int binStart, int binEnd)
    {
        if (!IsWellFormedBin(bin) || binStart > binEnd) return false;

        var width = Math.Max(binStart, binEnd).ToString().Length;
        if (width is not (6 or 8) || bin.Length < width) return false;

        var prefix = int.Parse(bin.AsSpan(0, width));
        return prefix >= binStart && prefix <= binEnd;
    }
}
