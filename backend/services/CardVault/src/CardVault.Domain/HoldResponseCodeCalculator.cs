namespace CardVault.Domain;

/// <summary>
/// Maps a hold-decline reason string to an ISO 8583 DE39 response code.
/// Extracted from HoldService — primitive-parameterized so Domain stays dependency-free.
/// </summary>
public static class HoldResponseCodeCalculator
{
    /// <summary>
    /// Returns the ISO 8583 DE39 response code for the given decline reason.
    /// </summary>
    /// <param name="reason">The raw decline reason string, or null/whitespace.</param>
    /// <returns>Two-digit ISO 8583 DE39 response code string.</returns>
    public static string MapResponseCode(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "05";

        // Common ISO8583 DE39 mapping (demo):
        // 00 Approved (handled elsewhere)
        // 51 Insufficient funds / credit
        // 65 Activity limit exceeded (velocity)
        // 59 Suspected fraud
        // 62 Restricted card (policy/MCC)
        var r = reason.Trim().ToUpperInvariant();

        if (r.Contains("INSUFFICIENT") || r.Contains("NO_FUNDS") || r.Contains("AVAILABLE_CREDIT")) return "51";
        if (r.StartsWith("VELOCITY") || r.Contains("VELOCITY")) return "65";
        if (r.StartsWith("FRAUD") || r.Contains("FRAUD") || r.Contains("RISK_SCORE")) return "59";
        if (r.StartsWith("MCC") || r.Contains("MCC") || r.Contains("RESTRICT") || r.Contains("BLOCKED")) return "62";

        return "05"; // Do not honor
    }
}
