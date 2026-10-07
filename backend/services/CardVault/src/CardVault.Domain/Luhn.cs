namespace CardVault.Domain;

/// <summary>
/// Luhn (ISO/IEC 7812-1) check digit rules for primary account numbers. Pure and allocation-free:
/// the issuer uses <see cref="ComputeCheckDigit"/> when generating a PAN and every consumer can use
/// <see cref="IsValid"/> to reject a mistyped or corrupted number before touching the vault.
/// </summary>
public static class Luhn
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="digits"/> is a string of at least two ASCII digits whose
    /// last digit is the correct Luhn check digit for the preceding ones. Whitespace and separators are
    /// never tolerated; callers normalize before validating.
    /// </summary>
    public static bool IsValid(string? digits)
    {
        if (digits is null || digits.Length < 2 || !AllDigits(digits)) return false;

        return Checksum(digits, doubleRightmost: false) % 10 == 0;
    }

    /// <summary>
    /// Returns the digit that makes <paramref name="partial"/> Luhn-valid once appended to it.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="partial"/> is empty or contains a non-digit.</exception>
    public static char ComputeCheckDigit(string partial)
    {
        if (string.IsNullOrEmpty(partial) || !AllDigits(partial))
            throw new ArgumentException("The partial number must be a non-empty string of digits.", nameof(partial));

        // The appended digit will sit at the rightmost (undoubled) position, so within the partial
        // number the rightmost digit is the first doubled one.
        var sum = Checksum(partial, doubleRightmost: true);
        return (char)('0' + (10 - sum % 10) % 10);
    }

    private static int Checksum(string digits, bool doubleRightmost)
    {
        var sum = 0;
        var doubleThis = doubleRightmost;

        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var digit = digits[i] - '0';
            if (doubleThis)
            {
                digit *= 2;
                if (digit > 9) digit -= 9;
            }

            sum += digit;
            doubleThis = !doubleThis;
        }

        return sum;
    }

    private static bool AllDigits(string value)
    {
        foreach (var c in value)
        {
            if (c is < '0' or > '9') return false;
        }

        return true;
    }
}
