using BuildingBlocks.Commercial;
using FluentAssertions;
using IsoSwitch.Api.Endpoints;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;

namespace IsoSwitch.Tests.Commercial;

public class CommercialIsoEndpointGuardTests
{
    [Fact]
    public async Task CommercialMode_AuthorizeWithSensitiveFields_IsRejectedBeforeHandler()
    {
        var guard = new CommercialIsoEndpointGuard(Options.Create(new CommercialOptions { Mode = CommercialMode.Commercial }));
        var request = new AuthorizeRequest(
            TraceId: "tr-sensitive",
            Bin: 411111,
            Amount: 10m,
            Currency: "840",
            MerchantId: "M001",
            TerminalId: "T001",
            Stan: "123456",
            PinBlock: "ABCD",
            EmvTlv: "9F2608ABCDEF",
            Pan: "4111111111111111",
            ExpiryYyMm: "2912",
            PosEntryMode: "051",
            PosConditionCode: "00",
            Track2: "4111111111111111=29122010000000000000",
            AdditionalAmounts54: null,
            Private60: null,
            Private61: null,
            Private62: null);
        var handlerCalled = false;

        var result = await guard.InvokeAsync(
            request,
            () =>
            {
                handlerCalled = true;
                return ValueTask.FromResult<object?>(Results.Ok());
            });

        handlerCalled.Should().BeFalse();
        result.Should().BeAssignableTo<IResult>();
    }

    [Fact]
    public async Task DemoMode_AuthorizeWithSyntheticFields_AllowsHandler()
    {
        var guard = new CommercialIsoEndpointGuard(Options.Create(new CommercialOptions { Mode = CommercialMode.Demo, EnableDemoSurfaces = true }));
        var request = new AuthorizeRequest("tr-demo", 411111, 10m, "840", "M001", "T001", "123456", null, null, "4111111111111111", null, null, null, null, null, null, null, null);
        var handlerCalled = false;

        await guard.InvokeAsync(
            request,
            () =>
            {
                handlerCalled = true;
                return ValueTask.FromResult<object?>(Results.Ok(new { accepted = true }));
            });

        handlerCalled.Should().BeTrue();
    }

    [Fact]
    public async Task CommercialMode_NetworkCommand_IsRejectedBeforeHandler()
    {
        var guard = new CommercialIsoEndpointGuard(Options.Create(new CommercialOptions { Mode = CommercialMode.Commercial }));
        var handlerCalled = false;

        await guard.InvokeAsync(
            CommercialIsoOperation.NetworkCommand,
            () =>
            {
                handlerCalled = true;
                return ValueTask.FromResult<object?>(Results.Ok());
            });

        handlerCalled.Should().BeFalse();
    }
}


