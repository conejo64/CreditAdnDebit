namespace IsoSwitch.Infrastructure.SwitchIso8583.Net;

public sealed class IsoSimulatorOptions
{
    public bool Enabled { get; set; } = true;
    public int LatencyMs { get; set; } = 150;
    public double FailRate { get; set; } = 0.01;
    public double DoNotHonorRate { get; set; } = 0.02;
    public long DeclineOverAmount { get; set; } = 500_000; // minor units
    public string DenyBinsCsv { get; set; } = "";
}