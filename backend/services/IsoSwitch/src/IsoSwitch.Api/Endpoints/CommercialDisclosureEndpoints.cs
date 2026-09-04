using BuildingBlocks.Commercial;
using IsoSwitch.Api.Security;
using Microsoft.Extensions.Options;

namespace IsoSwitch.Api.Endpoints;

public static class CommercialDisclosureEndpoints
{
    /// <summary>
    /// Publishes the running commercial mode and the sanitized capability claims.
    /// </summary>
    /// <remarks>
    /// Authorization is required even though the payload is sanitized: which
    /// capabilities a deployment considers simulated is operational detail, and the
    /// governance spec asks for a policy on the service endpoints. Read-only, so it
    /// rides the existing monitor policy rather than introducing another one.
    /// </remarks>
    public static void MapCommercialDisclosureEndpoints(this IEndpointRouteBuilder app)
    {
        var disclosure = app.MapGroup("/api")
            .RequireAuthorization(IsoSwitchAuthorizationPolicies.ViewSwitchMonitor);

        disclosure.MapGet("/commercial/claims", (
            IOptions<CommercialOptions> options,
            IClaimRegister register) =>
                Results.Ok(CommercialDisclosure.Build(options.Value, register)))
            .WithOpenApi();
    }
}
