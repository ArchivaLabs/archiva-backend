using Archiva.Application.Common.Interfaces;
using Archiva.Domain.Enums;
using Archiva.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Archiva.Web.Services;

public sealed class DocumentAnalysisReconciler
{
    private static readonly TimeSpan StaleQueuePublication = TimeSpan.FromHours(12);
    private readonly ApplicationDbContext _context;
    private readonly IDocumentAnalysisQueue _queue;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DocumentAnalysisReconciler> _logger;

    public DocumentAnalysisReconciler(
        ApplicationDbContext context,
        IDocumentAnalysisQueue queue,
        TimeProvider timeProvider,
        ILogger<DocumentAnalysisReconciler> logger
    )
    {
        _context = context;
        _queue = queue;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<int> ReconcileAsync(
        int batchSize = 500,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow();
        var currentMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var retryBefore = now - StaleQueuePublication;
        var modelRetryBefore = now.AddDays(-1);

        var documentIds = await _context
            .Documents.Where(document =>
                (
                    document.AnalysisStatus == DocumentAnalysisStatus.Pending
                    || document.AnalysisStatus == DocumentAnalysisStatus.Extracted
                ) && (document.AnalysisQueuedAt == null || document.AnalysisQueuedAt < retryBefore)
                || document.AnalysisStatus == DocumentAnalysisStatus.Processing
                    && (document.AnalysisLeaseUntil == null || document.AnalysisLeaseUntil < now)
                || (
                    document.AnalysisStatus == DocumentAnalysisStatus.DeferredMonthlyUnitLimit
                    || document.AnalysisStatus == DocumentAnalysisStatus.SummaryDeferredMonthlyLimit
                )
                    && (
                        document.AnalysisLastAttemptAt == null
                        || document.AnalysisLastAttemptAt < currentMonth
                    )
                || document.AnalysisStatus == DocumentAnalysisStatus.SummaryDeferredModelUnavailable
                    && (
                        document.AnalysisLastAttemptAt == null
                        || document.AnalysisLastAttemptAt < modelRetryBefore
                    )
            )
            .OrderBy(document => document.AnalysisLastAttemptAt)
            .ThenBy(document => document.Id)
            .Select(document => document.Id)
            .Take(Math.Clamp(batchSize, 1, 2_000))
            .ToListAsync(cancellationToken);

        var queuedCount = 0;
        foreach (var documentId in documentIds)
        {
            try
            {
                await _queue.EnqueueAsync(documentId, cancellationToken);
                queuedCount++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    "Document analysis reconciliation could not queue {DocumentId}; error type {ErrorType}",
                    documentId,
                    exception.GetType().Name
                );
            }
        }

        _logger.LogInformation(
            "Document analysis reconciliation queued {QueuedCount} of {CandidateCount} documents",
            queuedCount,
            documentIds.Count
        );
        return queuedCount;
    }
}
