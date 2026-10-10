using System.Text.Json;
using Archiva.Application.Common.Models;
using Archiva.Application.Documents.Commands.ProcessDocumentAnalysis;
using Archiva.Domain.Enums;
using Archiva.Infrastructure.Data;
using Archiva.Shared;
using Azure;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Archiva.Web.Services;

public sealed class DocumentAnalysisQueueWorker : BackgroundService
{
    private static readonly TimeSpan MessageVisibilityTimeout =
        DocumentAnalysisTiming.LeaseDuration;
    private readonly QueueClient _queueClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentAnalysisQueueWorker> _logger;
    private readonly int _maximumAttempts;

    public DocumentAnalysisQueueWorker(
        QueueServiceClient queueServiceClient,
        IServiceScopeFactory scopeFactory,
        ILogger<DocumentAnalysisQueueWorker> logger,
        IOptions<DocumentAnalysisOptions> options
    )
    {
        _queueClient = queueServiceClient.GetQueueClient(Archiva.Shared.Services.AnalysisQueueName);
        _scopeFactory = scopeFactory;
        _logger = logger;
        _maximumAttempts = options.Value.MaximumQueueAttempts;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _queueClient.CreateIfNotExistsAsync(cancellationToken: stoppingToken);
                var response = await _queueClient.ReceiveMessagesAsync(
                    maxMessages: 1,
                    visibilityTimeout: MessageVisibilityTimeout,
                    cancellationToken: stoppingToken
                );
                var message = response.Value.FirstOrDefault();

                if (message is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                await ProcessMessageAsync(message, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (RequestFailedException exception)
            {
                _logger.LogWarning(
                    "Document analysis queue poll failed with provider error {ProviderErrorCode}; retrying",
                    exception.ErrorCode ?? exception.Status.ToString()
                );
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    "Document analysis queue worker failed with {ErrorType}; retrying",
                    exception.GetType().Name
                );
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }
    }

    internal async Task ProcessMessageAsync(
        QueueMessage message,
        CancellationToken cancellationToken
    )
    {
        var payload = TryParse(message.MessageText);
        if (payload is null || payload.DocumentId < 1)
        {
            await _queueClient.DeleteMessageAsync(
                message.MessageId,
                message.PopReceipt,
                cancellationToken
            );
            _logger.LogWarning("Discarded an invalid document analysis queue message");
            return;
        }

        var lease = new QueueMessageLease(message.MessageId, message.PopReceipt);
        using var renewalCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        var renewalTask = RenewLeaseAsync(lease, payload.DocumentId, renewalCancellation.Token);

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(
                new ProcessDocumentAnalysisCommand(payload.DocumentId),
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await StopRenewalAsync(renewalCancellation, renewalTask);
            throw;
        }
        catch (Exception exception)
        {
            await StopRenewalAsync(renewalCancellation, renewalTask);
            if (message.DequeueCount >= _maximumAttempts)
            {
                await MarkTerminalFailureAsync(payload.DocumentId, cancellationToken);
                await DeleteMessageAsync(lease, cancellationToken);
                _logger.LogError(
                    "Document analysis stopped after {AttemptCount} attempts for {DocumentId}; error type {ErrorType}",
                    message.DequeueCount,
                    payload.DocumentId,
                    exception.GetType().Name
                );
                return;
            }

            var retryDelaySeconds = Math.Min(300, 5 * (int)Math.Pow(2, message.DequeueCount - 1));
            await _queueClient.UpdateMessageAsync(
                lease.MessageId,
                lease.PopReceipt,
                visibilityTimeout: TimeSpan.FromSeconds(retryDelaySeconds),
                cancellationToken: cancellationToken
            );
            _logger.LogWarning(
                "Document analysis attempt {AttemptCount} failed for {DocumentId}; retrying after {RetryDelaySeconds} seconds with error type {ErrorType}",
                message.DequeueCount,
                payload.DocumentId,
                retryDelaySeconds,
                exception.GetType().Name
            );
            return;
        }

        await StopRenewalAsync(renewalCancellation, renewalTask);
        await DeleteMessageAsync(lease, cancellationToken);
    }

    private async Task RenewLeaseAsync(
        QueueMessageLease lease,
        int documentId,
        CancellationToken cancellationToken
    )
    {
        using var timer = new PeriodicTimer(DocumentAnalysisTiming.LeaseRenewalInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    var response = await _queueClient.UpdateMessageAsync(
                        lease.MessageId,
                        lease.PopReceipt,
                        visibilityTimeout: MessageVisibilityTimeout,
                        cancellationToken: cancellationToken
                    );
                    lease.UpdatePopReceipt(response.Value.PopReceipt);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        "Document analysis queue visibility renewal failed for {DocumentId}; error type {ErrorType}",
                        documentId,
                        exception.GetType().Name
                    );
                }

                try
                {
                    await RenewDocumentLeaseAsync(documentId, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        "Document analysis database lease renewal failed for {DocumentId}; error type {ErrorType}",
                        documentId,
                        exception.GetType().Name
                    );
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown or message completion stopped the renewal loop.
        }
    }

    private async Task RenewDocumentLeaseAsync(int documentId, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;
        var renewed = await context
            .Documents.Where(document =>
                document.Id == documentId
                && document.AnalysisStatus == DocumentAnalysisStatus.Processing
                && document.AnalysisLeaseUntil > now
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters.SetProperty(
                        document => document.AnalysisLeaseUntil,
                        now + DocumentAnalysisTiming.LeaseDuration
                    ),
                cancellationToken
            );

        if (renewed == 0)
        {
            _logger.LogWarning(
                "Document analysis database lease could not be renewed for {DocumentId}",
                documentId
            );
        }
    }

    private static async Task StopRenewalAsync(
        CancellationTokenSource renewalCancellation,
        Task renewalTask
    )
    {
        renewalCancellation.Cancel();
        await renewalTask;
    }

    private async Task MarkTerminalFailureAsync(int documentId, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var document = await context.Documents.FirstOrDefaultAsync(
            item => item.Id == documentId,
            cancellationToken
        );

        if (document is null || document.AnalysisStatus == DocumentAnalysisStatus.Completed)
            return;

        document.AnalysisStatus = document.ExtractedText is null
            ? DocumentAnalysisStatus.ExtractionFailed
            : DocumentAnalysisStatus.SummaryFailed;
        document.AnalysisErrorCode = "processing_attempts_exceeded";
        document.AnalysisLeaseUntil = null;
        document.AnalysisQueuedAt = null;
        await context.SaveChangesAsync(cancellationToken);
        _logger.LogError(
            "Document analysis ended after the retry limit for {DocumentId} in organisation {OrganizationId}: {AnalysisStatus}, {BillableUnits} units, {SummaryInputCharacters} summary characters, provider error {ProviderErrorCode}",
            document.Id,
            document.OrganizationId,
            document.AnalysisStatus,
            document.BillableUnitCount,
            document.SummaryInputCharacters,
            "processing_attempts_exceeded"
        );
    }

    private Task DeleteMessageAsync(QueueMessage message, CancellationToken cancellationToken) =>
        _queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);

    private Task DeleteMessageAsync(QueueMessageLease lease, CancellationToken cancellationToken) =>
        _queueClient.DeleteMessageAsync(lease.MessageId, lease.PopReceipt, cancellationToken);

    private sealed class QueueMessageLease
    {
        private readonly object _sync = new();
        private string _popReceipt;

        public QueueMessageLease(string messageId, string popReceipt)
        {
            MessageId = messageId;
            _popReceipt = popReceipt;
        }

        public string MessageId { get; }

        public string PopReceipt
        {
            get
            {
                lock (_sync)
                {
                    return _popReceipt;
                }
            }
        }

        public void UpdatePopReceipt(string popReceipt)
        {
            lock (_sync)
            {
                _popReceipt = popReceipt;
            }
        }
    }

    private static QueuePayload? TryParse(string messageText)
    {
        try
        {
            return JsonSerializer.Deserialize<QueuePayload>(messageText);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record QueuePayload(int DocumentId);
}
