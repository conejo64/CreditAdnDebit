using System.Text.Json.Serialization;

namespace BuildingBlocks.Commercial;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClaimMaturity
{
    Verified = 0,
    Simulation = 1,
    Roadmap = 2
}
