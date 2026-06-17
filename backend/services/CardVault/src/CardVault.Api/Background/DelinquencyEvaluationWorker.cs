using CardVault.Application.Features.Delinquency.Commands;
using MediatR;

namespace CardVault.Api.Background;

/// <summary>
/// Runs once per day and dispatches <see cref="EvaluateDelinquencyCommand"/>
/// to identify credit accounts that have not met their minimum payment.
///
/// Uses a dedicated DI scope per execution so the DbContext is created
/// and disposed cleanly for each batch (prevents stale tracking issues).
/// </summary>
public sealed class DelinquencyEvaluationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DelinquencyEvaluationWorker> _logger;

    // How often the worker checks for delinquent accounts.
    // Using 24 hours in production; can be overridden via IConfiguration if needed.
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    public DelinquencyEvaluationWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<DelinquencyEvaluationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DelinquencyEvaluationWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            await EvaluateAsync(stoppingToken);
            await Task.Delay(Interval, stoppingToken);
        }

        _logger.LogInformation("DelinquencyEvaluationWorker stopping.");
    }

    private async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting daily delinquency evaluation. Reference date: {Date:yyyy-MM-dd}",
            DateTime.UtcNow.Date);

        try
        {
            await using var scope    = _scopeFactory.CreateAsyncScope();
            var mediator             = scope.ServiceProvider.GetRequiredService<IMediator>();

            await mediator.Send(new EvaluateDelinquencyCommand(DateTime.UtcNow.Date), cancellationToken);

            _logger.LogInformation("Delinquency evaluation completed successfully.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Graceful shutdown — not an error
        }
        catch (Exception ex)
        {
            // Log and keep worker alive — a single failure should not stop the job.
            _logger.LogError(ex, "Delinquency evaluation failed.");
        }
    }
}
