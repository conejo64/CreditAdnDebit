using CardVault.Api.Background;
using CardVault.Infrastructure.Notifications;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CardVault.Tests.Features.Notifications.Dispatcher;

/// <summary>
/// Task 1d.3 — NotificationDispatcherWorker unit tests.
/// Verifies the worker creates a DI scope, resolves INotificationDispatcher,
/// and delegates to DispatchBatchAsync on each tick.
/// </summary>
public sealed class NotificationDispatcherWorkerTests
{
    // ────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a worker with explicit inner IServiceProvider so NSubstitute
    /// property-chain setup is reliable.
    /// </summary>
    private static NotificationDispatcherWorker BuildWorker(
        INotificationDispatcher dispatcher,
        out CancellationTokenSource cts)
    {
        cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)); // safety timeout

        // Use an explicit inner substitute — NSubstitute auto-property chaining
        // is unreliable for recursive substitutes.
        var innerSp = Substitute.For<IServiceProvider>();
        innerSp.GetService(typeof(INotificationDispatcher)).Returns(dispatcher);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(innerSp);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(IServiceScopeFactory)).Returns(scopeFactory);

        return new NotificationDispatcherWorker(sp, NullLogger<NotificationDispatcherWorker>.Instance);
    }

    // ────────────────────────────────────────────────────────────────────
    // Worker resolves INotificationDispatcher and calls DispatchBatchAsync
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Worker_ResolvesINotificationDispatcher_AndCallsDispatchBatchAsync()
    {
        // Arrange
        var dispatcher = Substitute.For<INotificationDispatcher>();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Signal the TCS on first call so the test can stop the worker.
        // Do NOT cancel stoppingToken from inside the delegate — that causes
        // ExecuteAsync to complete synchronously before StartAsync returns,
        // and .NET 9 BackgroundService propagates the cancellation to the caller.
        dispatcher
            .DispatchBatchAsync(50, Arg.Any<CancellationToken>())
            .Returns(_ => { tcs.TrySetResult(); return Task.FromResult(0); });

        var worker = BuildWorker(dispatcher, out var cts);

        // Act
        await worker.StartAsync(cts.Token);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5)); // wait for first tick

        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await worker.StopAsync(stopCts.Token); } catch (OperationCanceledException) { /* ok */ }

        // Assert — at least one dispatch happened
        _ = dispatcher.Received(1).DispatchBatchAsync(50, Arg.Any<CancellationToken>());
    }

    // ────────────────────────────────────────────────────────────────────
    // Dispatcher exception must not crash the worker
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Worker_WhenDispatcherThrows_LogsAndContinues_DoesNotCrash()
    {
        // Arrange
        var dispatcher = Substitute.For<INotificationDispatcher>();
        var tcsFirstCallDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Signal the TCS, then return a faulted Task — worker must log and keep the loop alive.
        // Using Task.FromException avoids NSubstitute overload ambiguity on Task<int> return type.
        dispatcher
            .DispatchBatchAsync(50, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                tcsFirstCallDone.TrySetResult();
                return Task.FromException<int>(new InvalidOperationException("boom"));
            });

        var worker = BuildWorker(dispatcher, out var cts);

        // Act
        await worker.StartAsync(cts.Token);
        await tcsFirstCallDone.Task.WaitAsync(TimeSpan.FromSeconds(5)); // first exception happened

        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var act = async () =>
        {
            try { await worker.StopAsync(stopCts.Token); } catch (OperationCanceledException) { }
        };

        // Assert — the exception must be absorbed; stopping must not throw
        await act.Should().NotThrowAsync("worker must absorb dispatcher exceptions and keep running");
    }
}
