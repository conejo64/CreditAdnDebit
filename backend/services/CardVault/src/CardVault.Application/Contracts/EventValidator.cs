using System.Text.Json;

namespace CardVault.Application.Contracts;

/// <summary>
/// Very lightweight validation to avoid pulling heavy schema libs.
/// For production: JSON Schema / Avro / Protobuf with registry.
/// </summary>
public static class EventValidator
{
    public static bool TryValidateRequired(JsonElement payload, params string[] required)
    {
        foreach (var r in required)
        {
            if (!payload.TryGetProperty(r, out var _))
                return false;
        }
        return true;
    }
}
