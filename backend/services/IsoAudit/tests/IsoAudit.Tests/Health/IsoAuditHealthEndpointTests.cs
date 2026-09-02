using System.Net;
using IsoAudit.Api.Health;
using IsoAudit.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace IsoAudit.Tests.Health;

public sealed class IsoAuditHealthEndpointTests
{
    private static readonly string[] Unsafe = ["password", "connectionstring", "host=", "username=", "jwt", "token", "pan", "pin", "cardvault", "isoswitch"];

    [Fact]
    public async Task Live_IsAliveAndDoesNotExposeDependencies()
    {
        await using var factory = new IsoAuditWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("alive", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("checks", body, StringComparison.OrdinalIgnoreCase);
        AssertSafe(body);
    }

    [Theory]
    [InlineData("/health/ready")]
    [InlineData("/health")]
    public async Task Ready_AliasAndReadinessStayUnavailableUntilAllChecksAreReady(string path)
    {
        await using var factory = new IsoAuditWebApplicationFactory();
        using var client = factory.CreateClient();

        var unready = await client.GetAsync(path);
        var unreadyBody = await unready.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unready.StatusCode);
        Assert.Contains("database", unreadyBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kafka-topic", unreadyBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("consumer", unreadyBody, StringComparison.OrdinalIgnoreCase);
        AssertSafe(unreadyBody);

        Mark(factory.Services, IsoAuditReadinessStatus.Ready);
        var ready = await client.GetAsync(path);
        var readyBody = await ready.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Contains("ready", readyBody, StringComparison.OrdinalIgnoreCase);
        AssertSafe(readyBody);
    }

    [Fact]
    public async Task Readiness_ClassifiesMissingTopicAndConsumerFailureSafely()
    {
        await using var factory = new IsoAuditWebApplicationFactory();
        using var client = factory.CreateClient();
        var state = factory.Services.GetRequiredService<IIsoAuditReadinessState>();
        state.MarkReady(IsoAuditReadinessChecks.Database);
        state.MarkReady(IsoAuditReadinessChecks.Kafka);
        state.MarkUnready(IsoAuditReadinessChecks.KafkaTopic, "missing topic sw.iso.audit");
        state.MarkFailed(IsoAuditReadinessChecks.Consumer, "consumer failed");

        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("sw.iso.audit", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("consumer", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" at IsoAudit", body, StringComparison.OrdinalIgnoreCase);
        AssertSafe(body);
    }

    private static void Mark(IServiceProvider services, string status)
    {
        var state = services.GetRequiredService<IIsoAuditReadinessState>();
        foreach (var check in IsoAuditReadinessChecks.Required)
            state.Mark(check, status);
    }

    private static void AssertSafe(string body)
    {
        foreach (var fragment in Unsafe)
            Assert.DoesNotContain(fragment, body, StringComparison.OrdinalIgnoreCase);
    }
}
