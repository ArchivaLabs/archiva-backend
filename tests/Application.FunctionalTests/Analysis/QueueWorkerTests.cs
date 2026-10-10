using Archiva.Application.Common.Models;
using Archiva.Application.Documents.Commands.ProcessDocumentAnalysis;
using Archiva.Domain.Enums;
using Archiva.Web.Services;
using Azure.Storage.Queues;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Archiva.Application.FunctionalTests.Analysis;

public sealed class QueueWorkerTests : TestBase
{
    [Test]
    public async Task Malformed_payload_is_discarded_without_dispatching_command()
    {
        var sender = new Mock<ISender>();
        var count = await ProcessOnceAsync("{not-json", sender, maximumAttempts: 2);

        count.ShouldBe(0);
        sender.Verify(
            item =>
                item.Send(
                    It.IsAny<ProcessDocumentAnalysisCommand>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Test]
    public async Task Retryable_dispatch_error_preserves_document_and_queue_message()
    {
        var id = await AnalysisTestData.AddDocumentAsync(TestApp.Seed.FirstOrganizationId);
        var sender = FailingSender();

        var count = await ProcessOnceAsync($"{{\"DocumentId\":{id}}}", sender, maximumAttempts: 2);

        count.ShouldBe(1);
        var document = await AnalysisTestData.GetDocumentAsync(id);
        document.AnalysisStatus.ShouldBe(DocumentAnalysisStatus.Pending);
        document.AnalysisErrorCode.ShouldBeNull();
        sender.Verify(
            item =>
                item.Send(
                    It.IsAny<ProcessDocumentAnalysisCommand>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Test]
    public async Task Final_dispatch_error_marks_extraction_failure_and_deletes_message()
    {
        var id = await AnalysisTestData.AddDocumentAsync(TestApp.Seed.FirstOrganizationId);
        var sender = FailingSender();

        var count = await ProcessOnceAsync($"{{\"DocumentId\":{id}}}", sender, maximumAttempts: 1);

        count.ShouldBe(0);
        var document = await AnalysisTestData.GetDocumentAsync(id);
        document.AnalysisStatus.ShouldBe(DocumentAnalysisStatus.ExtractionFailed);
        document.AnalysisErrorCode.ShouldBe("processing_attempts_exceeded");
        document.AnalysisLeaseUntil.ShouldBeNull();
    }

    [Test]
    public async Task Final_dispatch_error_after_extraction_marks_summary_failure()
    {
        var id = await AnalysisTestData.AddDocumentAsync(
            TestApp.Seed.FirstOrganizationId,
            DocumentAnalysisStatus.Extracted,
            document => document.ExtractedText = "Already extracted"
        );

        var count = await ProcessOnceAsync(
            $"{{\"DocumentId\":{id}}}",
            FailingSender(),
            maximumAttempts: 1
        );

        count.ShouldBe(0);
        var document = await AnalysisTestData.GetDocumentAsync(id);
        document.AnalysisStatus.ShouldBe(DocumentAnalysisStatus.SummaryFailed);
        document.AnalysisErrorCode.ShouldBe("processing_attempts_exceeded");
        document.ExtractedText.ShouldBe("Already extracted");
    }

    [Test]
    public async Task Final_dispatch_error_does_not_change_completed_document()
    {
        var id = await AnalysisTestData.AddDocumentAsync(
            TestApp.Seed.FirstOrganizationId,
            DocumentAnalysisStatus.Completed,
            document =>
            {
                document.ExtractedText = "Already extracted";
                document.Summary = "Completed summary";
            }
        );

        var count = await ProcessOnceAsync(
            $"{{\"DocumentId\":{id}}}",
            FailingSender(),
            maximumAttempts: 1
        );

        count.ShouldBe(0);
        var document = await AnalysisTestData.GetDocumentAsync(id);
        document.AnalysisStatus.ShouldBe(DocumentAnalysisStatus.Completed);
        document.AnalysisErrorCode.ShouldBeNull();
        document.Summary.ShouldBe("Completed summary");
    }

    private static Mock<ISender> FailingSender()
    {
        var sender = new Mock<ISender>();
        sender
            .Setup(item =>
                item.Send(It.IsAny<ProcessDocumentAnalysisCommand>(), It.IsAny<CancellationToken>())
            )
            .ThrowsAsync(new InvalidOperationException("Processing unavailable"));
        return sender;
    }

    private static async Task<int> ProcessOnceAsync(
        string payload,
        Mock<ISender> sender,
        int maximumAttempts
    )
    {
        var queueService =
            FunctionalTestSetup.Factory.Services.GetRequiredService<QueueServiceClient>();
        var queue = queueService.GetQueueClient(Services.AnalysisQueueName);
        await queue.CreateIfNotExistsAsync();
        await queue.SendMessageAsync(payload);
        var received = await queue.ReceiveMessagesAsync(
            maxMessages: 1,
            visibilityTimeout: TimeSpan.FromMinutes(1)
        );
        var message = received.Value.Single();

        await TestApp.WithDbContextAsync(async db =>
        {
            using var provider = new ServiceCollection()
                .AddSingleton(db)
                .AddSingleton(sender.Object)
                .BuildServiceProvider();
            var scopeFactory = new Mock<IServiceScopeFactory>();
            scopeFactory
                .Setup(factory => factory.CreateScope())
                .Returns(() => new NoopScope(provider));
            using var worker = new DocumentAnalysisQueueWorker(
                queueService,
                scopeFactory.Object,
                NullLogger<DocumentAnalysisQueueWorker>.Instance,
                Options.Create(
                    new DocumentAnalysisOptions { MaximumQueueAttempts = maximumAttempts }
                )
            );
            await worker.ProcessMessageAsync(message, CancellationToken.None);
        });

        var properties = await queue.GetPropertiesAsync();
        return properties.Value.ApproximateMessagesCount;
    }

    private sealed class NoopScope(IServiceProvider serviceProvider) : IServiceScope
    {
        public IServiceProvider ServiceProvider => serviceProvider;

        public void Dispose() { }
    }
}
