using IsoAudit.Api.Health;

namespace IsoAudit.Tests.Health;

public sealed class IsoAuditReadinessStateTests
{
    [Fact]
    public void State_StartsUnreadyBecomesReadyOnlyWhenAllRequiredChecksAreReadyAndSanitizesReasons()
    {
        var state = new IsoAuditReadinessState();

        var starting = state.GetSnapshot();
        Assert.Equal(IsoAuditReadinessStatus.Unready, starting.Status);
        Assert.All(IsoAuditReadinessChecks.Required, name =>
            Assert.Equal(IsoAuditReadinessStatus.Starting, Assert.Single(starting.Checks, c => c.Name == name).Status));

        state.MarkReady(IsoAuditReadinessChecks.Database);
        state.MarkReady(IsoAuditReadinessChecks.Kafka);
        state.MarkReady(IsoAuditReadinessChecks.KafkaTopic);
        Assert.Equal(IsoAuditReadinessStatus.Unready, state.GetSnapshot().Status);

        state.MarkReady(IsoAuditReadinessChecks.Consumer);
        Assert.Equal(IsoAuditReadinessStatus.Ready, state.GetSnapshot().Status);

        state.MarkFailed(IsoAuditReadinessChecks.Database, "Host=db;Username=postgres;Password=secret;Token=abc;PAN=4111111111111111");
        var check = Assert.Single(state.GetSnapshot().Checks, c => c.Name == IsoAuditReadinessChecks.Database);
        Assert.Equal("database unavailable", check.Reason);
    }
}
