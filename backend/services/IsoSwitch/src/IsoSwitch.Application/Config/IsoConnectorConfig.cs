namespace IsoSwitch.Application.Config;

public sealed class IsoConnectorConfig
{
    public string AcqInstId { get; set; } = "0";
    public string FwdInstId { get; set; } = "0";
}

public sealed class IsoConfigRoot
{
    public Dictionary<string, IsoConnectorConfig> Connectors { get; set; } = new();
}
