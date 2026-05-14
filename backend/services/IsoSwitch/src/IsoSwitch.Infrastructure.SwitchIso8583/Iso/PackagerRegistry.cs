namespace IsoSwitch.Infrastructure.SwitchIso8583.Iso;

public sealed class PackagerRegistry
{
    private readonly Dictionary<string, IIso8583Packager> _byConnector = new(StringComparer.OrdinalIgnoreCase);
    private readonly IIso8583Packager _default = new DefaultIso8583Packager();

    public void Register(string connectorId, IIso8583Packager packager) => _byConnector[connectorId] = packager;

    public IIso8583Packager Get(string connectorId)
        => _byConnector.TryGetValue(connectorId, out var p) ? p : _default;
}