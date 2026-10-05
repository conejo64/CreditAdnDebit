using System.Globalization;

namespace CardVault.Domain;

/// <summary>
/// Reads the card expiry stored as <c>YYMM</c> (ISO 8583 DE14) or <c>YYYYMM</c> and decides whether
/// a card is still valid at a given instant. A card is valid through the last day of its expiry
/// month, in UTC; the first instant of the following month is the first expired instant.
/// </summary>
public static class CardExpiry
{
    /// <summary>
    /// Parses <paramref name="expiryYyMm"/> into the first UTC instant at which the card is expired
    /// (midnight of the first day of the month after the expiry month).
    /// </summary>
    /// <returns><c>false</c> when the value is not a well-formed <c>YYMM</c> or <c>YYYYMM</c>.</returns>
    public static bool TryGetExpiredFrom(string? expiryYyMm, out DateTimeOffset expiredFrom)
    {
        expiredFrom = default;
        if (string.IsNullOrWhiteSpace(expiryYyMm)) return false;

        var value = expiryYyMm.Trim();
        if (value.Length != 4 && value.Length != 6) return false;
        if (!value.All(char.IsAsciiDigit)) return false;

        var yearPart = value[..^2];
        var monthPart = value[^2..];

        var year = int.Parse(yearPart, NumberStyles.None, CultureInfo.InvariantCulture);
        var month = int.Parse(monthPart, NumberStyles.None, CultureInfo.InvariantCulture);
        if (month is < 1 or > 12) return false;

        // Two-digit years are read in the 2000s, matching how DE14 is interpreted on the network.
        if (yearPart.Length == 2) year += 2000;

        expiredFrom = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
        return true;
    }

    /// <summary>
    /// <c>true</c> when the card has expired at <paramref name="asOf"/>. Unparsable values are reported
    /// through <see cref="TryGetExpiredFrom"/>; callers must fail closed on them.
    /// </summary>
    public static bool IsExpired(string? expiryYyMm, DateTimeOffset asOf)
        => TryGetExpiredFrom(expiryYyMm, out var expiredFrom) && asOf >= expiredFrom;
}
