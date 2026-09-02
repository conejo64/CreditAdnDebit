using IsoAudit.Api.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IsoAudit.Tests.Health;

public sealed class IsoAuditConsumerWorkerStartupTests
{
    [Fact]
    public async Task StartAsync_ReturnsWhileConsumeLoopIsStillRunning()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Unroutable broker: the consumer never becomes usable, so the
                // consume loop keeps running for as long as the worker lives.
                ["Kafka:BootstrapServers"] = "127.0.0.1:1",
                ["Kafka:Topics:AuditEvents"] = "sw.iso.audit",
                ["Kafka:ConsumerGroup"] = $"iso-audit-test-{Guid.NewGuid():N}"
            })
            .Build();
        var readiness = new IsoAuditReadinessState();
        var worker = new IsoAuditConsumerWorker(
            NullLogger<IsoAuditConsumerWorker>.Instance,
            services,
            configuration,
            readiness);

        var startTask = worker.StartAsync(CancellationToken.None);

        // BackgroundService.StartAsync hands ExecuteAsync the stopping token and
        // returns Task.CompletedTask the moment ExecuteAsync yields. Only when
        // ExecuteAsync runs to completion synchronously does StartAsync return the
        // execute task itself. So an already-finished ExecuteTask - or a start task
        // that IS the execute task - means startup blocked on the consume loop.
        //
        // This asserts the contract directly rather than timing startup: a wall
        // clock threshold here measures JIT of ExecuteAsync, the first load of
        // Confluent.Kafka plus native librdkafka, and thread-pool scheduling
        // latency under a parallel suite - none of which is the behaviour at stake,
        // and all of which make the assertion fail on a cold or loaded machine.
        Assert.True(startTask.IsCompletedSuccessfully);
        Assert.NotNull(worker.ExecuteTask);
        Assert.NotSame(worker.ExecuteTask, startTask);
        Assert.False(worker.ExecuteTask!.IsCompleted);

        await worker.StopAsync(CancellationToken.None);
    }
}
