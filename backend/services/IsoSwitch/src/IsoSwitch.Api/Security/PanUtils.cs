using IsoSwitch.Domain;

namespace IsoSwitch.Api.Security;

/// <summary>
/// Thin forwarder kept for the Api call sites. Masking and Luhn live in
/// <see cref="CardDataMasking"/> (Domain) so Application and Infrastructure can use the same rules
/// without referencing the Api project.
/// </summary>
public static class PanUtils
{
    public static bool IsValidLuhn(string? pan) => CardDataMasking.IsValidLuhn(pan);

    /// <summary>First 6 + <c>*</c> + last 4; inputs shorter than 13 characters are fully masked.</summary>
    public static string Mask(string pan) => CardDataMasking.MaskPan(pan);

    public static string Bin6(string pan) => pan.Length >= 6 ? pan[..6] : "";
    public static string Last4(string pan) => pan.Length >= 4 ? pan[^4..] : "";
}
