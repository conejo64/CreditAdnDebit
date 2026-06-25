namespace IsoSwitch.Api;

public sealed class Field90Service
{
    private readonly IsoConfigRoot _root;
    private readonly IConfiguration _cfg;

    public Field90Service(IConfiguration cfg)
    {
        _cfg = cfg;
        _root = cfg.GetSection("Iso:Connectors").Get<IsoConfigRoot>() ?? new IsoConfigRoot();
    }

    public string BuildForConnector(string connectorId, string originalMti, string stan, DateTimeOffset originalTransmissionTime)
    {
        if (_root.Connectors.TryGetValue(connectorId, out var c))
            return OriginalDataElementsBuilder.Build(originalMti, stan, originalTransmissionTime, c.AcqInstId, c.FwdInstId);

        var acq = _cfg.GetValue<string>("Iso:AcqInstId") ?? "0";
        var fwd = _cfg.GetValue<string>("Iso:FwdInstId") ?? "0";
        return OriginalDataElementsBuilder.Build(originalMti, stan, originalTransmissionTime, acq, fwd);
    }
}