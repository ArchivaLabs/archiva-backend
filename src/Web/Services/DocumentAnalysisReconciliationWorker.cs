namespace Archiva.Web.Services;

public sealed class DocumentAnalysisReconciliationWorker : BackgroundService
{
    private static readonly TimeSpan ReconciliationInterval = TimeSpan.FromDays(1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentAnalysisReconciliationWorker> _logger;

    public DocumentAnalysisReconciliationWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<DocumentAnalysisReconciliationWorker> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var reconciler =
                    scope.ServiceProvider.GetRequiredService<DocumentAnalysisReconciler>();
                await reconciler.ReconcileAsync(cancellationToken: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    "Document analysis reconciliation failed with {ErrorType}",
                    exception.GetType().Name
                );
            }

            await Task.Delay(ReconciliationInterval, stoppingToken);
        }
    }
}
