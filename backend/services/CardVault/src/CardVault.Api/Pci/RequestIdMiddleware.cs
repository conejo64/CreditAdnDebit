using Microsoft.AspNetCore.Http;

namespace CardVault.Api.Pci;

public sealed class RequestIdMiddleware : IMiddleware
{
    public const string HeaderName = "X-Request-Id";

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var reqId = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(reqId))
            reqId = $"req_{Guid.NewGuid():N}";

        // ensure TraceIdentifier is set for logs/traces
        context.TraceIdentifier = reqId;
        context.Response.Headers[HeaderName] = reqId;

        using (context.RequestServices.GetRequiredService<ILogger<RequestIdMiddleware>>()
               .BeginScope(new Dictionary<string, object> { ["traceId"] = reqId }))
        {
            await next(context);
        }
    }
}