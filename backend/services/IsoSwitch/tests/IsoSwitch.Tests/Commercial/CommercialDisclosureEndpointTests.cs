using System.Net;
using IsoSwitch.Tests.Infrastructure;

namespace IsoSwitch.Tests.Commercial;

/// <summary>
/// Covers task 3.1 at the endpoint: the disclosure route is mapped in every mode and
/// is not readable without authorization.
/// </summary>
public sealed class CommercialDisclosureEndpointTests
{
    [Fact]
    public async Task Claims_WithoutCredentials_IsNotReadable()
    {
        using var factory = new IsoSwitchWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/commercial/claims");

        // The payload is sanitized, but which capabilities a deployment treats as
        // simulated is still operational detail, so the route must not be anonymous.
        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Expected the disclosure route to require authorization, got {(int)response.StatusCode}.");
    }

    [Fact]
    public async Task Claims_IsMappedEvenInCommercialMode()
    {
        // Commercial mode refuses to start alongside any exposure flag, so the whole
        // commercial shape has to be set here, not just the mode.
        using var factory = new IsoSwitchWebApplicationFactory()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("Commercial:Mode", "Commercial");
                b.UseSetting("Commercial:EnableDemoSurfaces", "false");
                b.UseSetting("Commercial:EnableSwagger", "false");
                b.UseSetting("Commercial:EnableAnonymousDiagnostics", "false");
            });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/commercial/claims");

        // A client cannot ask what mode it is talking to if the answer is itself
        // gated on that mode, so this route is never one of the demo surfaces.
        // Refused for lacking credentials is fine; 404 would mean it was not mapped.
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
