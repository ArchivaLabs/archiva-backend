using Archiva.Application.Common.Interfaces;
using Archiva.Application.Common.Models;
using Archiva.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Archiva.Application.FunctionalTests.Analysis;

public sealed class ProcessDocumentAnalysisTests : TestBase
{
    [Test]
    public async Task Processes_document_once_and_records_result_and_reservations()
    {
        var id = await AnalysisTestData.AddDocumentAsync(TestApp.Seed.FirstOrganizationId);
        var fakes = new ProcessFakes();

        await fakes.ProcessAsync(id);
        await fakes.ProcessAsync(id);

        var document = await AnalysisTestData.GetDocumentAsync(id);
        document.AnalysisStatus.ShouldBe(DocumentAnalysisStatus.Completed);
        document.ExtractedText.ShouldBe("Meeting notes");
        document.Summary.ShouldBe("Meeting summary");
        document.BillableUnitCount.ShouldBe(1);
        document.SummaryInputCharacters.ShouldBe("Meeting notes".Length);
        document.AnalysisAttemptCount.ShouldBe(1);
        document.AnalysisCompletedAt.ShouldNotBeNull();
        document.AnalysisLeaseUntil.ShouldBeNull();
        fakes.Storage.Verify(
            storage => storage.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once
        );
        fakes.Extractor.Verify(
            extractor =>
                extractor.ExtractAsync(It.IsAny<byte[]>(), "TXT", It.IsAny<CancellationToken>()),
            Times.Once
        );
        fakes.Budget.Verify(
            budget => budget.TryReserveBillableUnitsAsync(id, 1, 20, It.IsAny<CancellationToken>()),
            Times.Once
        );
        fakes.Budget.Verify(
            budget =>
                budget.TryReserveSummaryCharactersAsync(
                    id,
                    "Meeting notes".Length,
                    100_000,
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Test]
    public async Task Active_lease_is_not_claimed_but_expired_lease_is_recovered()
    {
        var id = await AnalysisTestData.AddDocumentAsync(
            TestApp.Seed.FirstOrganizationId,
            DocumentAnalysisStatus.Processing,
            document => document.AnalysisLeaseUntil = DateTimeOffset.UtcNow.AddHours(1)
        );
        var fakes = new ProcessFakes();

        await fakes.ProcessAsync(id);

        (await AnalysisTestData.GetDocumentAsync(id)).AnalysisAttemptCount.ShouldBe(0);
        fakes.Storage.Verify(
            storage => storage.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never
        );

        await TestApp.WithDbContextAsync(async db =>
        {
            await db
                .Documents.Where(document => document.Id == id)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(
                        document => document.AnalysisLeaseUntil,
                        DateTimeOffset.UtcNow.AddMinutes(-1)
                    )
                );
        });
        await fakes.ProcessAsync(id);

        var recovered = await AnalysisTestData.GetDocumentAsync(id);
        recovered.AnalysisStatus.ShouldBe(DocumentAnalysisStatus.Completed);
        recovered.AnalysisAttemptCount.ShouldBe(1);
    }

    [Test]
    public async Task Document_unit_limit_defers_before_budget_reservation_or_extraction()
    {
        var id = await AnalysisTestData.AddDocumentAsync(
            TestApp.Seed.FirstOrganizationId,
            configure: document => document.AnalysisUnitLimit = 1
        );
        var fakes = new ProcessFakes();
        fakes
            .Extractor.Setup(extractor =>
                extractor.Estimate(It.IsAny<byte[]>(), It.IsAny<string>())
            )
            .Returns(new DocumentAnalysisEstimate(2, 4));

        await fakes.ProcessAsync(id);

        var document = await AnalysisTestData.GetDocumentAsync(id);
        document.AnalysisStatus.ShouldBe(DocumentAnalysisStatus.DeferredDocumentUnitLimit);
        document.AnalysisErrorCode.ShouldBe("document_unit_limit");
        document.AnalysisLeaseUntil.ShouldBeNull();
        fakes.Budget.Verify(
            budget =>
                budget.TryReserveBillableUnitsAsync(
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
        fakes.Extractor.Verify(
            extractor =>
                extractor.ExtractAsync(
                    It.IsAny<byte[]>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Test]
    public async Task Monthly_unit_budget_denial_defers_document()
    {
        var id = await AnalysisTestData.AddDocumentAsync(TestApp.Seed.FirstOrganizationId);
        var fakes = new ProcessFakes();
        fakes
            .Budget.Setup(budget =>
                budget.TryReserveBillableUnitsAsync(
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(false);

        await fakes.ProcessAsync(id);

        var document = await AnalysisTestData.GetDocumentAsync(id);
        document.AnalysisStatus.ShouldBe(DocumentAnalysisStatus.DeferredMonthlyUnitLimit);
        document.AnalysisErrorCode.ShouldBe("monthly_unit_limit");
        document.ExtractedText.ShouldBeNull();
    }

    [Test]
    public async Task Monthly_summary_budget_denial_keeps_extracted_text_for_later_retry()
    {
        var id = await AnalysisTestData.AddDocumentAsync(TestApp.Seed.FirstOrganizationId);
        var fakes = new ProcessFakes();
        fakes
            .Budget.Setup(budget =>
                budget.TryReserveSummaryCharactersAsync(
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(false);

        await fakes.ProcessAsync(id);

        var document = await AnalysisTestData.GetDocumentAsync(id);
        document.AnalysisStatus.ShouldBe(DocumentAnalysisStatus.SummaryDeferredMonthlyLimit);
        document.AnalysisErrorCode.ShouldBe("summary_monthly_limit");
        document.ExtractedText.ShouldBe("Meeting notes");
        fakes.Summarizer.Verify(
            summarizer =>
                summarizer.SummarizeAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Test]
    public async Task Extracted_text_resumes_summary_without_downloading_again()
    {
        var id = await AnalysisTestData.AddDocumentAsync(
            TestApp.Seed.FirstOrganizationId,
            DocumentAnalysisStatus.Extracted,
            document => document.ExtractedText = "Already extracted"
        );
        var fakes = new ProcessFakes();

        await fakes.ProcessAsync(id);

        var document = await AnalysisTestData.GetDocumentAsync(id);
        document.AnalysisStatus.ShouldBe(DocumentAnalysisStatus.Completed);
        document.Summary.ShouldBe("Meeting summary");
        fakes.Storage.Verify(
            storage => storage.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
        fakes.Extractor.Verify(
            extractor =>
                extractor.ExtractAsync(
                    It.IsAny<byte[]>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Test]
    public async Task Summary_limit_defers_after_persisting_extracted_text()
    {
        var id = await AnalysisTestData.AddDocumentAsync(
            TestApp.Seed.FirstOrganizationId,
            configure: document => document.SummaryInputCharacterLimit = 5
        );
        var fakes = new ProcessFakes();

        await fakes.ProcessAsync(id);

        var document = await AnalysisTestData.GetDocumentAsync(id);
        document.AnalysisStatus.ShouldBe(DocumentAnalysisStatus.SummaryDeferredDocumentLimit);
        document.AnalysisErrorCode.ShouldBe("summary_document_limit");
        document.ExtractedText.ShouldBe("Meeting notes");
        fakes.Summarizer.Verify(
            summarizer =>
                summarizer.SummarizeAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Test]
    public async Task Missing_summary_model_defers_with_extracted_text_available()
    {
        var id = await AnalysisTestData.AddDocumentAsync(TestApp.Seed.FirstOrganizationId);
        var fakes = new ProcessFakes();
        fakes.Summarizer.SetupGet(summarizer => summarizer.IsConfigured).Returns(false);

        await fakes.ProcessAsync(id);

        var document = await AnalysisTestData.GetDocumentAsync(id);
        document.AnalysisStatus.ShouldBe(DocumentAnalysisStatus.SummaryDeferredModelUnavailable);
        document.AnalysisErrorCode.ShouldBe("summary_model_unavailable");
        document.ExtractedText.ShouldBe("Meeting notes");
    }

    [Test]
    public async Task Invalid_document_finishes_as_extraction_failed_without_leaving_lease()
    {
        var id = await AnalysisTestData.AddDocumentAsync(TestApp.Seed.FirstOrganizationId);
        var fakes = new ProcessFakes();
        fakes
            .Extractor.Setup(extractor =>
                extractor.Estimate(It.IsAny<byte[]>(), It.IsAny<string>())
            )
            .Throws(new InvalidDataException("Malformed content"));

        await fakes.ProcessAsync(id);

        var document = await AnalysisTestData.GetDocumentAsync(id);
        document.AnalysisStatus.ShouldBe(DocumentAnalysisStatus.ExtractionFailed);
        document.AnalysisErrorCode.ShouldBe("invalid_document");
        document.AnalysisLeaseUntil.ShouldBeNull();
    }

    [Test]
    public async Task Transient_extraction_error_releases_lease_for_queue_retry()
    {
        var id = await AnalysisTestData.AddDocumentAsync(TestApp.Seed.FirstOrganizationId);
        var fakes = new ProcessFakes();
        fakes
            .Extractor.Setup(extractor =>
                extractor.ExtractAsync(
                    It.IsAny<byte[]>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(
                new DocumentAnalysisProviderException("provider_unavailable", "429", true)
            );

        await Should.ThrowAsync<DocumentAnalysisProviderException>(() => fakes.ProcessAsync(id));

        var document = await AnalysisTestData.GetDocumentAsync(id);
        document.AnalysisStatus.ShouldBe(DocumentAnalysisStatus.Pending);
        document.AnalysisLeaseUntil.ShouldBeNull();
        document.AnalysisAttemptCount.ShouldBe(1);
    }

    [Test]
    public async Task Permanent_summary_provider_error_records_failure_without_losing_text()
    {
        var id = await AnalysisTestData.AddDocumentAsync(TestApp.Seed.FirstOrganizationId);
        var fakes = new ProcessFakes();
        fakes
            .Summarizer.Setup(summarizer =>
                summarizer.SummarizeAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new DocumentAnalysisProviderException("provider_failed", "400"));

        await fakes.ProcessAsync(id);

        var document = await AnalysisTestData.GetDocumentAsync(id);
        document.AnalysisStatus.ShouldBe(DocumentAnalysisStatus.SummaryFailed);
        document.AnalysisErrorCode.ShouldBe("summary_provider_failed");
        document.ExtractedText.ShouldBe("Meeting notes");
        document.AnalysisLeaseUntil.ShouldBeNull();
    }

    [Test]
    public async Task Transient_summary_error_releases_lease_and_retries_from_extracted_text()
    {
        var id = await AnalysisTestData.AddDocumentAsync(TestApp.Seed.FirstOrganizationId);
        var fakes = new ProcessFakes();
        fakes
            .Summarizer.Setup(summarizer =>
                summarizer.SummarizeAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(
                new DocumentAnalysisProviderException("provider_unavailable", "429", true)
            );

        await Should.ThrowAsync<DocumentAnalysisProviderException>(() => fakes.ProcessAsync(id));

        var document = await AnalysisTestData.GetDocumentAsync(id);
        document.AnalysisStatus.ShouldBe(DocumentAnalysisStatus.Extracted);
        document.ExtractedText.ShouldBe("Meeting notes");
        document.AnalysisLeaseUntil.ShouldBeNull();
    }

    [Test]
    public async Task Concurrent_delivery_does_not_extract_or_reserve_twice()
    {
        var id = await AnalysisTestData.AddDocumentAsync(TestApp.Seed.FirstOrganizationId);
        var fakes = new ProcessFakes();
        var extractionStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var releaseExtraction = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        fakes
            .Extractor.Setup(extractor =>
                extractor.ExtractAsync(
                    It.IsAny<byte[]>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(async () =>
            {
                extractionStarted.TrySetResult();
                await releaseExtraction.Task;
                return "Meeting notes";
            });

        var first = fakes.ProcessAsync(id);
        try
        {
            await extractionStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await fakes.ProcessAsync(id);
        }
        finally
        {
            releaseExtraction.TrySetResult();
        }
        await first;

        (await AnalysisTestData.GetDocumentAsync(id)).AnalysisAttemptCount.ShouldBe(1);
        fakes.Storage.Verify(
            storage => storage.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once
        );
        fakes.Budget.Verify(
            budget =>
                budget.TryReserveBillableUnitsAsync(
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }
}
