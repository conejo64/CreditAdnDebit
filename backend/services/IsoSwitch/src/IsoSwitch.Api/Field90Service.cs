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

        return OriginalDataElementsBuilder.BuildFromConfig(_cfg, originalMti, stan, originalTransmissionTime);
    }
}