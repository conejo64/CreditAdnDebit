namespace IsoSwitch.Api;

/// <summary>
/// Field 90 (Original Data Elements) builder.
/// Common 42-char format: MTI(4) + STAN(6) + transmission datetime(10) + acqInstId(11) + fwdInstId(11).
/// </summary>
public static class OriginalDataElementsBuilder
{
    public static string Build(string originalMti, string stan, DateTimeOffset originalTransmissionTime, string acqInstId, string fwdInstId)
    {
        var mti = (originalMti ?? "0100").PadRight(4).Substring(0, 4);
        var s = (stan ?? "000000").PadLeft(6, '0');
        var dt = originalTransmissionTime.ToString("MMddHHmmss");
        var acq = Normalize11(acqInstId);
        var fwd = Normalize11(fwdInstId);
        return $"{mti}{s}{dt}{acq}{fwd}";
    }

    public static string BuildFromConfig(IConfiguration cfg, string originalMti, string stan, DateTimeOffset originalTransmissionTime)
    {
        var acq = cfg.GetValue<string>("Iso:AcqInstId") ?? "0";
        var fwd = cfg.GetValue<string>("Iso:FwdInstId") ?? "0";
        return Build(originalMti, stan, originalTransmissionTime, acq, fwd);
    }

    private static string Normalize11(string? v)
    {
        var digits = new string((v ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 0) digits = "0";
        if (digits.Length > 11) digits = digits[^11..];
        return digits.PadLeft(11, '0');
    }
}