using System.Text.Json;
using Archiva.Application.Common.Interfaces;
using Archiva.Domain.Entities;
using Archiva.Infrastructure.Data;
using Archiva.Shared;
using Azure.Storage.Queues;
using Microsoft.EntityFrameworkCore;

namespace Archiva.Infrastructure.Analysis;

public sealed class AzureDocumentAnalysisQueue : IDocumentAnalysisQueue
{
    private readonly QueueClient _queueClient;
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public AzureDocumentAnalysisQueue(
        QueueServiceClient queueServiceClient,
        ApplicationDbContext context,
        TimeProvider timeProvider
    )
    {
        _queueClient = queueServiceClient.GetQueueClient(Services.AnalysisQueueName);
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task EnqueueAsync(int documentId, CancellationToken cancellationToken = default)
    {
        var document = await _context.Documents.FirstOrDefaultAsync(
            item => item.Id == documentId,
            cancellationToken
        );

        if (document is null)
            return;

        await _queueClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        await _queueClient.SendMessageAsync(
            JsonSerializer.Serialize(new AnalysisQueueMessage(documentId)),
            cancellationToken
        );

        document.AnalysisQueuedAt = _timeProvider.GetUtcNow();
        await _context.SaveChangesAsync(cancellationToken);
    }

    public sealed record AnalysisQueueMessage(int DocumentId);
}
