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
        // 05 Do not honor
        // 14 Invalid card number (card unknown, not this account's, cancelled, unreadable expiry)
        // 51 Insufficient funds / credit
        // 54 Expired card
        // 59 Suspected fraud
        // 62 Restricted card (card or account blocked / not yet active / closed, policy, MCC)
        // 65 Activity limit exceeded (velocity)
        var r = reason.Trim().ToUpperInvariant();

        // Card and account state (RiskDecisionService step 0) — exact reasons first so the
        // substring heuristics below cannot reclassify them.
        switch (r)
        {
            case "CARD_EXPIRED": return "54";
            case "CARD_NOT_FOUND":
            case "CARD_ACCOUNT_MISMATCH":
            case "CARD_CANCELLED":
            case "CARD_EXPIRY_INVALID": return "14";
            case "CARD_BLOCKED":
            case "CARD_NOT_ACTIVE":
            case "ACCOUNT_BLOCKED":
            case "ACCOUNT_CLOSED": return "62";
            case "ACCOUNT_DELINQUENT": return "05";
        }

        if (r.Contains("INSUFFICIENT") || r.Contains("NO_FUNDS") || r.Contains("AVAILABLE_CREDIT")) return "51";
        if (r.StartsWith("VELOCITY") || r.Contains("VELOCITY")) return "65";
        if (r.StartsWith("FRAUD") || r.Contains("FRAUD") || r.Contains("RISK_SCORE")) return "59";
        if (r.StartsWith("MCC") || r.Contains("MCC") || r.Contains("RESTRICT") || r.Contains("BLOCKED")) return "62";

        return "05"; // Do not honor
    }
}
