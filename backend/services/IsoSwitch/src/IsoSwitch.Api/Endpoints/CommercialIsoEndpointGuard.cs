using BuildingBlocks.Commercial;
using Microsoft.Extensions.Options;

namespace IsoSwitch.Api.Endpoints;

public enum CommercialIsoOperation
{
    NetworkCommand,
    ReversalAdvice
}

public sealed class CommercialIsoEndpointGuard : IEndpointFilter
{
    private readonly CommercialOptions _commercialOptions;

    public CommercialIsoEndpointGuard(IOptions<CommercialOptions> commercialOptions)
    {
        ArgumentNullException.ThrowIfNull(commercialOptions);
        _commercialOptions = commercialOptions.Value;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        foreach (var argument in context.Arguments)
        {
            if (ShouldDeny(argument))
            {
                return DeniedResult();
            }
        }

        return await next(context);
    }

    public ValueTask<object?> InvokeAsync(object? request, Func<ValueTask<object?>> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return ShouldDeny(request)
            ? ValueTask.FromResult<object?>(DeniedResult())
            : next();
    }

    private bool ShouldDeny(object? request)
    {
        if (!_commercialOptions.IsCommercialMode)
        {
            return false;
        }

        return request switch
        {
            global::AuthorizeRequest auth => HasSensitiveInputs(auth),
            global::ReversalRequest reversal => HasSensitiveInputs(reversal),
            CommercialIsoOperation => true,
            _ => false
        };
    }

    private static bool HasSensitiveInputs(global::AuthorizeRequest request) =>
        HasValue(request.Pan) ||
        HasValue(request.Track2) ||
        HasValue(request.PinBlock) ||
        HasValue(request.EmvTlv);

    private static bool HasSensitiveInputs(global::ReversalRequest request) =>
        HasValue(request.PinBlock) || HasValue(request.EmvTlv);

    private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);

    private static IResult DeniedResult() => Results.Problem(
        title: "Commercial ISO operation unavailable",
        detail: "Commercial mode rejects simulator-backed or sensitive ISO inputs before any handler, connector, audit, or event side effect.",
        statusCode: StatusCodes.Status403Forbidden);
}
