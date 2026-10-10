using System.Text;
using System.Text.Json;
using Archiva.Domain.Entities;
using Archiva.Infrastructure.Analysis;
using Archiva.Infrastructure.Data;
using Archiva.Infrastructure.Storage;
using Archiva.Shared;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Archiva.Infrastructure.IntegrationTests;

public sealed class AzuriteStorageTests : IntegrationTestBase
{
    [Test]
    public async Task Blob_service_round_trips_private_content_and_read_sas_then_deletes_it()
    {
        var blobClient = IntegrationHost.Services.GetRequiredService<BlobServiceClient>();
        var service = new BlobStorageService(
            blobClient,
            new UserDelegationKeyProvider(blobClient, IntegrationHost.Clock)
        );
        var payload = Encoding.UTF8.GetBytes("Archive minutes");
        await using var stream = new MemoryStream(payload);

        var blobName = await service.UploadAsync(stream, "minutes.txt", "text/plain");
        var readUrl = await service.GetReadUrlAsync(blobName);
        var sasContent = await new BlobClient(new Uri(readUrl)).DownloadContentAsync();

        blobName.ShouldEndWith("/minutes.txt");
        (await service.DownloadAsync(blobName)).ShouldBe(payload);
        sasContent.Value.Content.ToArray().ShouldBe(payload);
        await service.DeleteAsync(blobName);
        (
            await blobClient
                .GetBlobContainerClient("documents")
                .GetBlobClient(blobName)
                .ExistsAsync()
        ).Value.ShouldBeFalse();
    }

    [Test]
    public async Task Queue_publish_writes_document_id_and_persists_queued_time()
    {
        var documentId = await IntegrationHost.WithDbAsync(async db =>
        {
            var meeting = new Meeting
            {
                Title = "Queue test",
                MeetingDate = new DateTime(2026, 10, 15),
                OrganizationId = IntegrationHost.FirstOrganizationId,
            };
            db.Meetings.Add(meeting);
            await db.SaveChangesAsync();
            var document = new Document
            {
                FileName = "minutes.txt",
                FileType = ".txt",
                BlobName = "queue-test",
                BlobUrl = "queue-test",
                FileSizeInBytes = 1,
                MeetingId = meeting.Id,
                OrganizationId = IntegrationHost.FirstOrganizationId,
            };
            db.Documents.Add(document);
            await db.SaveChangesAsync();
            return document.Id;
        });
        await using var scope = IntegrationHost.Services.CreateAsyncScope();
        var queueService = scope.ServiceProvider.GetRequiredService<QueueServiceClient>();
        var queue = new AzureDocumentAnalysisQueue(
            queueService,
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            IntegrationHost.Clock
        );

        await queue.EnqueueAsync(documentId);

        var message = (
            await queueService.GetQueueClient(Services.AnalysisQueueName).ReceiveMessageAsync()
        ).Value;
        var payload = JsonSerializer.Deserialize<AzureDocumentAnalysisQueue.AnalysisQueueMessage>(
            message.MessageText
        );
        payload!.DocumentId.ShouldBe(documentId);
        var queuedAt = await IntegrationHost.WithDbAsync(db =>
            db.Documents.Where(item => item.Id == documentId)
                .Select(item => item.AnalysisQueuedAt)
                .SingleAsync()
        );
        queuedAt.ShouldBe(IntegrationHost.Clock.GetUtcNow());
    }

    [Test]
    public async Task Queue_publish_for_missing_document_creates_no_message()
    {
        await using var scope = IntegrationHost.Services.CreateAsyncScope();
        var queueService = scope.ServiceProvider.GetRequiredService<QueueServiceClient>();
        var queue = new AzureDocumentAnalysisQueue(
            queueService,
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            IntegrationHost.Clock
        );

        await queue.EnqueueAsync(999_999);

        (
            await queueService.GetQueueClient(Services.AnalysisQueueName).ExistsAsync()
        ).Value.ShouldBeFalse();
    }
}
