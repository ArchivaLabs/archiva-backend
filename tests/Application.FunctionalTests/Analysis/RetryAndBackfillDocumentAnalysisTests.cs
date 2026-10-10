using Archiva.Application.Common.Exceptions;
using Archiva.Application.Common.Interfaces;
using Archiva.Application.Common.Models;
using Archiva.Application.Documents.Commands.BackfillDocumentAnalysis;
using Archiva.Application.Documents.Commands.RetryDocumentAnalysis;
using Archiva.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace Archiva.Application.FunctionalTests.Analysis;

public sealed class RetryAndBackfillDocumentAnalysisTests : TestBase
{
    [Test]
    public async Task Retry_requires_membership_and_admin_role_and_hides_foreign_document()
    {
        var id = await AnalysisTestData.AddDocumentAsync(
            TestApp.Seed.FirstOrganizationId,
            DocumentAnalysisStatus.ExtractionFailed
        );
        var queue = new Mock<IDocumentAnalysisQueue>();

        await Should.ThrowAsync<UnauthorizedAccessException>(() =>
            RetryAsync(id, TestSeed.Outsider, queue)
        );
        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            RetryAsync(id, TestSeed.FirstMember, queue)
        );
        await Should.ThrowAsync<NotFoundException>(() =>
            RetryAsync(id, TestSeed.SecondAdmin, queue)
        );

        queue.Verify(
            item => item.EnqueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Test]
    public async Task Retry_rejects_completed_and_processing_documents()
    {
        var completed = await AnalysisTestData.AddDocumentAsync(
            TestApp.Seed.FirstOrganizationId,
            DocumentAnalysisStatus.Completed
        );
        var processing = await AnalysisTestData.AddDocumentAsync(
            TestApp.Seed.FirstOrganizationId,
            DocumentAnalysisStatus.Processing
        );
        var queue = new Mock<IDocumentAnalysisQueue>();

        await Should.ThrowAsync<Archiva.Application.Common.Exceptions.ValidationException>(() =>
            RetryAsync(completed, TestSeed.FirstAdmin, queue)
        );
        await Should.ThrowAsync<Archiva.Application.Common.Exceptions.ValidationException>(() =>
            RetryAsync(processing, TestSeed.FirstAdmin, queue)
        );

        queue.Verify(
            item => item.EnqueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Test]
    public async Task Retry_resets_failure_fields_raises_limits_and_queues_once()
    {
        var id = await AnalysisTestData.AddDocumentAsync(
            TestApp.Seed.FirstOrganizationId,
            DocumentAnalysisStatus.SummaryFailed,
            document =>
            {
                document.ExtractedText = "Already extracted";
                document.AnalysisErrorCode = "summary_provider_failed";
                document.AnalysisAttemptCount = 3;
                document.AnalysisLeaseUntil = DateTimeOffset.UtcNow.AddMinutes(10);
                document.AnalysisCompletedAt = DateTimeOffset.UtcNow;
            }
        );
        var queue = new Mock<IDocumentAnalysisQueue>();
        queue
            .Setup(item => item.EnqueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await RetryAsync(id, TestSeed.FirstAdmin, queue, 21, 100_001);

        result.AnalysisStatus.ShouldBe(DocumentAnalysisStatus.Extracted);
        var document = await AnalysisTestData.GetDocumentAsync(id);
        document.AnalysisUnitLimit.ShouldBe(21);
        document.SummaryInputCharacterLimit.ShouldBe(100_001);
        document.AnalysisStatus.ShouldBe(DocumentAnalysisStatus.Extracted);
        document.AnalysisErrorCode.ShouldBeNull();
        document.AnalysisAttemptCount.ShouldBe(0);
        document.AnalysisLeaseUntil.ShouldBeNull();
        document.AnalysisCompletedAt.ShouldBeNull();
        queue.Verify(item => item.EnqueueAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Retry_rejects_limit_that_is_not_higher_than_current_limit()
    {
        var id = await AnalysisTestData.AddDocumentAsync(
            TestApp.Seed.FirstOrganizationId,
            DocumentAnalysisStatus.DeferredDocumentUnitLimit
        );
        var queue = new Mock<IDocumentAnalysisQueue>();

        await Should.ThrowAsync<Archiva.Application.Common.Exceptions.ValidationException>(() =>
            RetryAsync(id, TestSeed.FirstAdmin, queue, 20)
        );

        queue.Verify(
            item => item.EnqueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Test]
    public async Task Retry_queue_failure_leaves_document_eligible_for_reconciliation()
    {
        var id = await AnalysisTestData.AddDocumentAsync(
            TestApp.Seed.FirstOrganizationId,
            DocumentAnalysisStatus.ExtractionFailed,
            document => document.AnalysisQueuedAt = DateTimeOffset.UtcNow
        );
        var queue = new Mock<IDocumentAnalysisQueue>();
        queue
            .Setup(item => item.EnqueueAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Queue unavailable"));

        await Should.ThrowAsync<InvalidOperationException>(() =>
            RetryAsync(id, TestSeed.FirstAdmin, queue)
        );

        var document = await AnalysisTestData.GetDocumentAsync(id);
        document.AnalysisStatus.ShouldBe(DocumentAnalysisStatus.Pending);
        document.AnalysisQueuedAt.ShouldBeNull();
        document.AnalysisLeaseUntil.ShouldBeNull();
    }

    [Test]
    public async Task Backfill_requires_admin_and_filters_ineligible_or_foreign_documents()
    {
        var eligible = await AnalysisTestData.AddDocumentAsync(
            TestApp.Seed.FirstOrganizationId,
            DocumentAnalysisStatus.ExtractionFailed
        );
        await AnalysisTestData.AddDocumentAsync(
            TestApp.Seed.FirstOrganizationId,
            DocumentAnalysisStatus.Completed
        );
        await AnalysisTestData.AddDocumentAsync(
            TestApp.Seed.SecondOrganizationId,
            DocumentAnalysisStatus.Pending
        );
        var queue = new Mock<IDocumentAnalysisQueue>();
        queue
            .Setup(item => item.EnqueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await Should.ThrowAsync<UnauthorizedAccessException>(() =>
            BackfillAsync(TestSeed.Outsider, queue)
        );
        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            BackfillAsync(TestSeed.FirstMember, queue)
        );
        var result = await BackfillAsync(TestSeed.FirstAdmin, queue);

        result.QueuedCount.ShouldBe(1);
        result.NextAfterDocumentId.ShouldBeNull();
        queue.Verify(
            item => item.EnqueueAsync(eligible, It.IsAny<CancellationToken>()),
            Times.Once
        );
        queue.Verify(
            item => item.EnqueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once
        );
        (await AnalysisTestData.GetDocumentAsync(eligible)).AnalysisStatus.ShouldBe(
            DocumentAnalysisStatus.Pending
        );
    }

    [Test]
    public async Task Backfill_cursor_and_batch_size_return_continuation()
    {
        var first = await AnalysisTestData.AddDocumentAsync(TestApp.Seed.FirstOrganizationId);
        var second = await AnalysisTestData.AddDocumentAsync(TestApp.Seed.FirstOrganizationId);
        var third = await AnalysisTestData.AddDocumentAsync(TestApp.Seed.FirstOrganizationId);
        var queue = new Mock<IDocumentAnalysisQueue>();
        queue
            .Setup(item => item.EnqueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var page = await BackfillAsync(TestSeed.FirstAdmin, queue, first, 1);
        var nextPage = await BackfillAsync(
            TestSeed.FirstAdmin,
            queue,
            page.NextAfterDocumentId!.Value,
            1
        );

        page.QueuedCount.ShouldBe(1);
        page.NextAfterDocumentId.ShouldBe(second);
        nextPage.QueuedCount.ShouldBe(1);
        nextPage.NextAfterDocumentId.ShouldBe(third);
        queue.Verify(item => item.EnqueueAsync(first, It.IsAny<CancellationToken>()), Times.Never);
        queue.Verify(item => item.EnqueueAsync(second, It.IsAny<CancellationToken>()), Times.Once);
        queue.Verify(item => item.EnqueueAsync(third, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Task<DocumentAnalysisActionResult> RetryAsync(
        int id,
        TestIdentity identity,
        Mock<IDocumentAnalysisQueue> queue,
        int? unitLimit = null,
        int? summaryLimit = null
    ) =>
        TestApp.WithDbContextAsync(async db =>
        {
            var user = new Mock<IUser>();
            user.SetupGet(current => current.Id).Returns(identity.Id);
            var handler = new RetryDocumentAnalysisCommandHandler(
                db,
                user.Object,
                queue.Object,
                Microsoft.Extensions.Options.Options.Create(new DocumentAnalysisOptions()),
                NullLogger<RetryDocumentAnalysisCommandHandler>.Instance
            );
            return await handler.Handle(
                new RetryDocumentAnalysisCommand(id, unitLimit, summaryLimit),
                CancellationToken.None
            );
        });

    private static Task<BackfillDocumentAnalysisResult> BackfillAsync(
        TestIdentity identity,
        Mock<IDocumentAnalysisQueue> queue,
        int afterId = 0,
        int batchSize = 100
    ) =>
        TestApp.WithDbContextAsync(async db =>
        {
            var user = new Mock<IUser>();
            user.SetupGet(current => current.Id).Returns(identity.Id);
            var handler = new BackfillDocumentAnalysisCommandHandler(
                db,
                user.Object,
                queue.Object,
                NullLogger<BackfillDocumentAnalysisCommandHandler>.Instance
            );
            return await handler.Handle(
                new BackfillDocumentAnalysisCommand(afterId, batchSize),
                CancellationToken.None
            );
        });
}
