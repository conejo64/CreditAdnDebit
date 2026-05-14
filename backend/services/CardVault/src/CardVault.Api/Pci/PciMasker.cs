namespace CardVault.Api.Pci;

public static class PciMasker
{
    public static string MaskPan(string pan, PciOptions opt)
    {
        if (string.IsNullOrWhiteSpace(pan)) return "****";
        pan = new string(pan.Where(char.IsDigit).ToArray());

        if (pan.Length < 10) return "****";

        if (string.Equals(opt.MaskPanLevel, "LAST4_ONLY", StringComparison.OrdinalIgnoreCase))
            return $"************{pan[^4..]}";

        // default FIRST6_LAST4
        return $"{pan[..6]}******{pan[^4..]}";
    }

    public static string Bin(string pan)
    {
        if (string.IsNullOrWhiteSpace(pan)) return "";
        pan = new string(pan.Where(char.IsDigit).ToArray());
        return pan.Length >= 6 ? pan[..6] : "";
    }
}