namespace IsoSwitch.Domain;

public sealed class IsoMessage
{
    public string Mti { get; set; } = "0100";
    public Dictionary<int, string> Fields { get; } = new();

    public string Get(int field) => Fields[field];
    public void Set(int field, string value) => Fields[field] = value;
}