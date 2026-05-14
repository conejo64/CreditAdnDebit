namespace CardVault.Api.Pci;

public sealed class PciOptions
{
    public bool LogSensitiveData { get; set; } = false;

    /// <summary>
    /// FIRST6_LAST4 or LAST4_ONLY
    /// </summary>
    public string MaskPanLevel { get; set; } = "FIRST6_LAST4";
}