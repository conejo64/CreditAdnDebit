using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace IsoAudit.Api.Health;

public static class IsoAuditHealthEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IResult Live() => Results.Json(new { status = "alive", service = "IsoAudit.Api" });

    public static IResult Ready(IIsoAuditReadinessState state)
    {
        var snapshot = state.GetSnapshot();
        return string.Equals(snapshot.Status, IsoAuditReadinessStatus.Ready, StringComparison.OrdinalIgnoreCase)
            ? Results.Json(snapshot, JsonOptions, statusCode: StatusCodes.Status200OK)
            : Results.Json(snapshot, JsonOptions, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}