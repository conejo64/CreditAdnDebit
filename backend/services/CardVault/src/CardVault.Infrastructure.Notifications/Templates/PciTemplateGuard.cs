using System.Text.RegularExpressions;

namespace CardVault.Infrastructure.Notifications.Templates;

/// <summary>
/// Pre-render PCI guard. Validates a <see cref="TemplateModel"/> and throws
/// <see cref="PciTemplateViolationException"/> if any field contains data that
/// could expose raw PAN, OTP seed/secret, or other Cardholder Data (CHD).
/// <para>
/// Design contract (ADR-6): fails CLOSED — any violation throws, rendering is blocked.
/// </para>
/// </summary>
public sealed class PciTemplateGuard
{
    /// <summary>
    /// Matches 6 or more consecutive digits — the minimum length for a BIN (first 6 digits of a PAN).
    /// A masked PAN (****NNNN) contains only 4 trailing digits and does NOT trigger this rule.
    /// </summary>
    private static readonly Regex ConsecutiveDigitsRegex =
        new(@"\d{6,}", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Keywords that indicate an OTP seed or shared secret is present.
    /// Matches case-insensitively.
    /// </summary>
    private static readonly Regex OtpSeedKeywordRegex =
        new(@"(?i)(otp[_\-]?secret|totp[_\-]?secret|hotp[_\-]?secret|shared[_\-]?secret|seed[_\-]?key)",
            RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    // ── OtpCode field exemption ───────────────────────────────────────────────
    // The OtpCode field is the DISPLAY code (e.g. "654321") — it is explicitly allowed to
    // contain 6 digits.  It is NOT a seed/secret.  The guard exempts this field from
    // the ConsecutiveDigitsRegex but still blocks it if it looks like a secret keyword.

    /// <summary>
    /// Validates the model. Throws <see cref="PciTemplateViolationException"/> on the
    /// first detected violation. Returns normally if the model is safe to render.
    /// </summary>
    /// <exception cref="ArgumentNullException">If <paramref name="model"/> is null.</exception>
    /// <exception cref="PciTemplateViolationException">If a PCI violation is detected.</exception>
    public void Validate(TemplateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Validate each string field individually so we can report the exact field name.
        ValidateField(model.MaskedPan,      nameof(TemplateModel.MaskedPan),      checkDigits: true,  checkOtpSeed: true);
        ValidateField(model.CurrencyCode,   nameof(TemplateModel.CurrencyCode),   checkDigits: false, checkOtpSeed: false);
        ValidateField(model.MaskedMerchant, nameof(TemplateModel.MaskedMerchant), checkDigits: true,  checkOtpSeed: true);
        // OtpCode is the display token — exempt from digit check, but still checked for seed keywords
        ValidateField(model.OtpCode,        nameof(TemplateModel.OtpCode),        checkDigits: false, checkOtpSeed: true);
        // AdditionalData is free-form — apply both checks
        ValidateField(model.AdditionalData, nameof(TemplateModel.AdditionalData), checkDigits: true,  checkOtpSeed: true);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void ValidateField(
        string? value,
        string fieldName,
        bool checkDigits,
        bool checkOtpSeed)
    {
        if (string.IsNullOrEmpty(value))
            return;

        if (checkDigits)
        {
            // Check the raw value first
            if (ConsecutiveDigitsRegex.IsMatch(value))
                throw new PciTemplateViolationException(
                    fieldName,
                    "contains 6 or more consecutive digits (potential raw PAN/account number)");

            // Also check after stripping common separators (hyphens, spaces)
            // to catch formats like "4111-1111-1111-1111" or "4111 1111 1111 1111"
            var stripped = StripSeparators(value);
            if (stripped.Length != value.Length && ConsecutiveDigitsRegex.IsMatch(stripped))
                throw new PciTemplateViolationException(
                    fieldName,
                    "contains 6 or more consecutive digits after separator removal (potential hyphenated/spaced PAN)");
        }

        if (checkOtpSeed && OtpSeedKeywordRegex.IsMatch(value))
            throw new PciTemplateViolationException(
                fieldName,
                "contains OTP seed/secret keyword");
    }

    private static string StripSeparators(string value)
        => value.Replace("-", string.Empty).Replace(" ", string.Empty);
}
