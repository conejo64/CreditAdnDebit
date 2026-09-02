using System.Diagnostics;
using IsoAudit.Api.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace IsoAudit.Tests.Health;

public sealed class IsoAuditKafkaTopicProbeTests
{
    [Fact]
    public void EvaluateTopicMetadata_ReturnsReadyWhenRequiredTopicExists()
    {
        var result = IsoAuditKafkaTopicProbe.EvaluateTopicMetadata(["other.topic", "sw.iso.audit"], "sw.iso.audit");

        Assert.Equal(IsoAuditKafkaTopicProbeResult.Ready, result.Status);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void EvaluateTopicMetadata_ReturnsMissingWhenRequiredTopicIsAbsent()
    {
        var result = IsoAuditKafkaTopicProbe.EvaluateTopicMetadata(["other.topic"], "sw.iso.audit");

        Assert.Equal(IsoAuditKafkaTopicProbeResult.Missing, result.Status);
        Assert.Equal("missing topic sw.iso.audit", result.Reason);
    }

    [Fact]
    public async Task CheckAsync_ReturnsWithoutRunningSynchronousMetadataOnCallerThread()
    {
        using var metadataStarted = new ManualResetEventSlim(false);
        using var releaseMetadata = new ManualResetEventSlim(false);
        var probe = CreateProbe((_, _) =>
        {
            metadataStarted.Set();
            releaseMetadata.Wait();
            return ["sw.iso.audit"];
        });

        var elapsed = Stopwatch.StartNew();
        var checkTask = probe.CheckAsync(CancellationToken.None);
        elapsed.Stop();

        Assert.True(metadataStarted.Wait(TimeSpan.FromSeconds(1)));
        Assert.False(checkTask.IsCompleted);
        Assert.True(elapsed.Elapsed < TimeSpan.FromMilliseconds(100));

        releaseMetadata.Set();
        var result = await checkTask;
        Assert.Equal(IsoAuditKafkaTopicProbeResult.Ready, result.Status);
    }

    [Fact]
    public async Task CheckAsync_ObservesCancellationWhileMetadataReadIsStillBoundedBySyncClient()
    {
        using var metadataStarted = new ManualResetEventSlim(false);
        using var releaseMetadata = new ManualResetEventSlim(false);
        var probe = CreateProbe((_, _) =>
        {
            metadataStarted.Set();
            releaseMetadata.Wait();
            return ["sw.iso.audit"];
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var checkTask = probe.CheckAsync(cts.Token);
        Assert.True(metadataStarted.Wait(TimeSpan.FromSeconds(1)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => checkTask.WaitAsync(TimeSpan.FromSeconds(1)));
        releaseMetadata.Set();
    }

    private static IsoAuditKafkaTopicProbe CreateProbe(Func<TimeSpan, IReadOnlyCollection<string>> readAllTopicNames) =>
        CreateProbe((_, timeout) => readAllTopicNames(timeout));

    private static IsoAuditKafkaTopicProbe CreateProbe(Func<Confluent.Kafka.AdminClientConfig, TimeSpan, IReadOnlyCollection<string>> readAllTopicNames)
    {
        var configuration = new ConfigurationBuilder().Build();
        var options = Options.Create(new IsoAuditReadinessOptions());
        return new IsoAuditKafkaTopicProbe(configuration, options, readAllTopicNames);
    }
}