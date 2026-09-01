using System.Text.Json.Serialization;

namespace BuildingBlocks.Commercial;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CommercialMode
{
    Commercial = 0,
    Demo = 1
}
