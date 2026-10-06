using System.Diagnostics;
using Archiva.Application.Common.Interfaces;
using Archiva.Application.Common.Models;
using Archiva.Domain.Entities;
using Archiva.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Archiva.Application.Documents.Commands.ProcessDocumentAnalysis;

public record ProcessDocumentAnalysisCommand(int DocumentId) : IRequest;

public sealed class ProcessDocumentAnalysisCommandHandler
    : IRequestHandler<ProcessDocumentAnalysisCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IStorageService _storageService;
    private readonly IDocumentTextExtractor _textExtractor;
    private readonly IDocumentSummarizer _summarizer;
    private readonly IDocumentAnalysisBudget _budget;
    private readonly TimeProvider _timeProvider;
    private readonly DocumentAnalysisOptions _options;
    private readonly ILogger<ProcessDocumentAnalysisCommandHandler> _logger;

    public ProcessDocumentAnalysisCommandHandler(
        IApplicationDbContext context,
        IStorageService storageService,
        IDocumentTextExtractor textExtractor,
        IDocumentSummarizer summarizer,
        IDocumentAnalysisBudget budget,
        TimeProvider timeProvider,
        IOptions<DocumentAnalysisOptions> options,
        ILogger<ProcessDocumentAnalysisCommandHandler> logger
    )
    {
        _context = context;
        _storageService = storageService;
        _textExtractor = textExtractor;
        _summarizer = summarizer;
        _budget = budget;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task Handle(
        ProcessDocumentAnalysisCommand request,
        CancellationToken cancellationToken
    )
    {
        var now = _timeProvider.GetUtcNow();
        var claimed = await _context
            .Documents.Where(document =>
                document.Id == request.DocumentId
                && document.AnalysisStatus != DocumentAnalysisStatus.Completed
                && document.AnalysisStatus != DocumentAnalysisStatus.DeferredDocumentUnitLimit
                && document.AnalysisStatus != DocumentAnalysisStatus.SummaryDeferredDocumentLimit
                && (
                    document.AnalysisStatus != DocumentAnalysisStatus.Processing
                    || document.AnalysisLeaseUntil == null
                    || document.AnalysisLeaseUntil <= now
                )
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(
                            document => document.AnalysisStatus,
                            DocumentAnalysisStatus.Processing
                        )
                        .SetProperty(
                            document => document.AnalysisLeaseUntil,
                            now + DocumentAnalysisTiming.LeaseDuration
                        )
                        .SetProperty(document => document.AnalysisQueuedAt, (DateTimeOffset?)null)
                        .SetProperty(
                            document => document.AnalysisStartedAt,
                            document => document.AnalysisStartedAt ?? now
                        )
                        .SetProperty(document => document.AnalysisLastAttemptAt, now)
                        .SetProperty(
                            document => document.AnalysisAttemptCount,
                            document => document.AnalysisAttemptCount + 1
                        ),
                cancellationToken
            );

        if (claimed == 0)
            return;

        var document = await _context.Documents.FirstOrDefaultAsync(
            item => item.Id == request.DocumentId,
            cancellationToken
        );

        if (document is null)
            return;

        var stopwatch = Stopwatch.StartNew();
        if (document.ExtractedText is null)
        {
            byte[] content;
            DocumentAnalysisEstimate estimate;
            string extractedText;

            try
            {
                content = await _storageService.DownloadAsync(document.BlobName, cancellationToken);
                estimate = _textExtractor.Estimate(content, document.FileType);

                if (estimate.BillableUnits > document.AnalysisUnitLimit)
                {
                    await SetStatusAsync(
                        document,
                        DocumentAnalysisStatus.DeferredDocumentUnitLimit,
                        "document_unit_limit",
                        cancellationToken
                    );
                    LogStatus(document, stopwatch.Elapsed, null);
                    return;
                }

                if (
                    !await _budget.TryReserveBillableUnitsAsync(
                        document.Id,
                        estimate.BillableUnits,
                        document.AnalysisUnitLimit,
                        cancellationToken
                    )
                )
                {
                    await SetStatusAsync(
                        document,
                        estimate.BillableUnits > document.AnalysisUnitLimit
                            ? DocumentAnalysisStatus.DeferredDocumentUnitLimit
                            : DocumentAnalysisStatus.DeferredMonthlyUnitLimit,
                        estimate.BillableUnits > document.AnalysisUnitLimit
                            ? "document_unit_limit"
                            : "monthly_unit_limit",
                        cancellationToken
                    );
                    LogStatus(document, stopwatch.Elapsed, null);
                    return;
                }

                extractedText = await _textExtractor.ExtractAsync(
                    content,
                    document.FileType,
                    cancellationToken
                );
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (DocumentAnalysisProviderException exception) when (exception.IsTransient)
            {
                await ReleaseForRetryAsync(document, cancellationToken);
                throw;
            }
            catch (Exception exception)
                when (exception
                        is InvalidDataException
                            or InvalidOperationException
                            or DocumentAnalysisProviderException
                )
            {
                await SetStatusAsync(
                    document,
                    DocumentAnalysisStatus.ExtractionFailed,
                    GetExtractionErrorCode(exception),
                    cancellationToken
                );
                LogStatus(document, stopwatch.Elapsed, GetProviderErrorCode(exception));
                return;
            }
            catch (Exception)
            {
                await ReleaseForRetryAsync(document, cancellationToken);
                throw;
            }

            document.ExtractedText = extractedText;
            document.BillableUnitCount = estimate.BillableUnits;
            document.AnalysisErrorCode = null;
            document.AnalysisStatus = DocumentAnalysisStatus.Extracted;
            document.AnalysisLeaseUntil =
                _timeProvider.GetUtcNow() + DocumentAnalysisTiming.LeaseDuration;
            await _context.SaveChangesAsync(cancellationToken);
        }

        var text = document.ExtractedText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            await SetStatusAsync(
                document,
                DocumentAnalysisStatus.Completed,
                null,
                cancellationToken
            );
            LogStatus(document, stopwatch.Elapsed, null);
            return;
        }

        var summaryCharacterCount = text.Length;
        if (
            summaryCharacterCount > document.SummaryInputCharacterLimit
            || summaryCharacterCount > _options.MaximumSummaryInputCharacters
        )
        {
            await SetStatusAsync(
                document,
                DocumentAnalysisStatus.SummaryDeferredDocumentLimit,
                "summary_document_limit",
                cancellationToken
            );
            LogStatus(document, stopwatch.Elapsed, null);
            return;
        }

        if (!_summarizer.IsConfigured)
        {
            await SetStatusAsync(
                document,
                DocumentAnalysisStatus.SummaryDeferredModelUnavailable,
                "summary_model_unavailable",
                cancellationToken
            );
            LogStatus(document, stopwatch.Elapsed, null);
            return;
        }

        if (
            !await _budget.TryReserveSummaryCharactersAsync(
                document.Id,
                summaryCharacterCount,
                document.SummaryInputCharacterLimit,
                cancellationToken
            )
        )
        {
            await SetStatusAsync(
                document,
                DocumentAnalysisStatus.SummaryDeferredMonthlyLimit,
                summaryCharacterCount > document.SummaryInputCharacterLimit
                    ? "summary_document_limit"
                    : "summary_monthly_limit",
                cancellationToken
            );
            LogStatus(document, stopwatch.Elapsed, null);
            return;
        }

        try
        {
            document.Summary = await _summarizer.SummarizeAsync(
                document.FileName,
                text,
                cancellationToken
            );
            document.SummaryInputCharacters = summaryCharacterCount;
            await SetStatusAsync(
                document,
                DocumentAnalysisStatus.Completed,
                null,
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DocumentAnalysisProviderException exception) when (exception.IsTransient)
        {
            await ReleaseForRetryAsync(document, cancellationToken);
            throw;
        }
        catch (DocumentAnalysisProviderException exception)
        {
            await SetStatusAsync(
                document,
                DocumentAnalysisStatus.SummaryFailed,
                "summary_provider_failed",
                cancellationToken
            );
            LogStatus(document, stopwatch.Elapsed, GetProviderErrorCode(exception));
            return;
        }
        catch (Exception)
        {
            await ReleaseForRetryAsync(document, cancellationToken);
            throw;
        }

        LogStatus(document, stopwatch.Elapsed, null);
    }

    private async Task SetStatusAsync(
        Document document,
        DocumentAnalysisStatus status,
        string? errorCode,
        CancellationToken cancellationToken
    )
    {
        document.AnalysisStatus = status;
        document.AnalysisErrorCode = errorCode;
        document.AnalysisLeaseUntil = null;
        document.AnalysisQueuedAt = null;
        document.AnalysisCompletedAt =
            status == DocumentAnalysisStatus.Completed ? _timeProvider.GetUtcNow() : null;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task ReleaseForRetryAsync(Document document, CancellationToken cancellationToken)
    {
        document.AnalysisStatus = document.ExtractedText is null
            ? DocumentAnalysisStatus.Pending
            : DocumentAnalysisStatus.Extracted;
        document.AnalysisErrorCode = null;
        document.AnalysisLeaseUntil = null;
        document.AnalysisQueuedAt = null;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private void LogStatus(Document document, TimeSpan duration, string? providerErrorCode)
    {
        _logger.LogInformation(
            "Document analysis finished for {DocumentId} in organisation {OrganizationId}: {AnalysisStatus}, {BillableUnits} units, {SummaryInputCharacters} summary characters, {DurationMilliseconds} ms, provider error {ProviderErrorCode}",
            document.Id,
            document.OrganizationId,
            document.AnalysisStatus,
            document.BillableUnitCount,
            document.SummaryInputCharacters,
            duration.TotalMilliseconds,
            providerErrorCode
        );
    }

    private static string GetExtractionErrorCode(Exception exception)
    {
        return exception switch
        {
            InvalidDataException => "invalid_document",
            DocumentAnalysisProviderException providerException =>
                providerException.PublicErrorCode,
            InvalidOperationException => "document_intelligence_unavailable",
            _ => "document_intelligence_failed",
        };
    }

    private static string? GetProviderErrorCode(Exception exception)
    {
        return exception is DocumentAnalysisProviderException providerException
            ? providerException.ProviderErrorCode
            : exception.GetType().Name;
    }
}
