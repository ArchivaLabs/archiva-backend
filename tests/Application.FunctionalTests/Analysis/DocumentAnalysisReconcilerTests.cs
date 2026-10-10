using Archiva.Application.Common.Interfaces;
using Archiva.Domain.Enums;
using Archiva.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Archiva.Application.FunctionalTests.Analysis;

public sealed class DocumentAnalysisReconcilerTests : TestBase
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Requeues_stale_and_recoverable_documents_but_skips_recent_and_final_states()
    {
        var stalePending = await AddAsync(
            DocumentAnalysisStatus.Pending,
            document => document.AnalysisQueuedAt = Now.AddHours(-13)
        );
        var expiredLease = await AddAsync(
            DocumentAnalysisStatus.Processing,
            document => document.AnalysisLeaseUntil = Now.AddMinutes(-1)
        );
        var nextMonthRetry = await AddAsync(
            DocumentAnalysisStatus.DeferredMonthlyUnitLimit,
            document => document.AnalysisLastAttemptAt = Now.AddMonths(-1)
        );
        var modelRetry = await AddAsync(
            DocumentAnalysisStatus.SummaryDeferredModelUnavailable,
            document => document.AnalysisLastAttemptAt = Now.AddDays(-2)
        );
        await AddAsync(
            DocumentAnalysisStatus.Pending,
            document => document.AnalysisQueuedAt = Now.AddHours(-1)
        );
        await AddAsync(
            DocumentAnalysisStatus.Processing,
            document => document.AnalysisLeaseUntil = Now.AddMinutes(1)
        );
        await AddAsync(
            DocumentAnalysisStatus.DeferredMonthlyUnitLimit,
            document => document.AnalysisLastAttemptAt = Now.AddDays(-1)
        );
        await AddAsync(
            DocumentAnalysisStatus.SummaryDeferredModelUnavailable,
            document => document.AnalysisLastAttemptAt = Now.AddHours(-1)
        );
        await AddAsync(DocumentAnalysisStatus.DeferredDocumentUnitLimit);
        await AddAsync(DocumentAnalysisStatus.Completed);
        var queued = new List<int>();
        var queue = new Mock<IDocumentAnalysisQueue>();
        queue
            .Setup(item => item.EnqueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, CancellationToken>((id, _) => queued.Add(id))
            .Returns(Task.CompletedTask);

        var count = await ReconcileAsync(queue);

        count.ShouldBe(4);
        queued
            .OrderBy(id => id)
            .ShouldBe(
                new[] { stalePending, expiredLease, nextMonthRetry, modelRetry }.OrderBy(id => id)
            );
    }

    [Test]
    public async Task Batch_size_limits_reconciliation_and_queue_failure_does_not_stop_next_document()
    {
        var first = await AddAsync(DocumentAnalysisStatus.Pending);
        var second = await AddAsync(DocumentAnalysisStatus.Pending);
        await AddAsync(DocumentAnalysisStatus.Pending);
        var queue = new Mock<IDocumentAnalysisQueue>();
        queue
            .Setup(item => item.EnqueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns<int, CancellationToken>(
                (id, _) =>
                    id == first
                        ? Task.FromException(new InvalidOperationException("Queue unavailable"))
                        : Task.CompletedTask
            );

        var count = await ReconcileAsync(queue, 2);

        count.ShouldBe(1);
        queue.Verify(item => item.EnqueueAsync(first, It.IsAny<CancellationToken>()), Times.Once);
        queue.Verify(item => item.EnqueueAsync(second, It.IsAny<CancellationToken>()), Times.Once);
        queue.Verify(
            item => item.EnqueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2)
        );
    }

    private static Task<int> AddAsync(
        DocumentAnalysisStatus status,
        Action<Archiva.Domain.Entities.Document>? configure = null
    ) => AnalysisTestData.AddDocumentAsync(TestApp.Seed.FirstOrganizationId, status, configure);

    private static Task<int> ReconcileAsync(
        Mock<IDocumentAnalysisQueue> queue,
        int batchSize = 500
    ) =>
        TestApp.WithDbContextAsync(async db =>
        {
            var reconciler = new DocumentAnalysisReconciler(
                db,
                queue.Object,
                new FixedTimeProvider(Now),
                NullLogger<DocumentAnalysisReconciler>.Instance
            );
            return await reconciler.ReconcileAsync(batchSize);
        });

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
