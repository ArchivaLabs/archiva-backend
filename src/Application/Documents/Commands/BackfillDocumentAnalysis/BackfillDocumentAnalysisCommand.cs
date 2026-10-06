using Archiva.Application.Common.Exceptions;
using Archiva.Application.Common.Interfaces;
using Archiva.Application.Common.Models;
using Archiva.Domain.Entities;
using Archiva.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Archiva.Application.Documents.Commands.BackfillDocumentAnalysis;

public record BackfillDocumentAnalysisCommand(int AfterDocumentId = 0, int BatchSize = 100)
    : IRequest<BackfillDocumentAnalysisResult>;

public record BackfillDocumentAnalysisResult(int QueuedCount, int? NextAfterDocumentId);

public sealed class BackfillDocumentAnalysisCommandHandler
    : IRequestHandler<BackfillDocumentAnalysisCommand, BackfillDocumentAnalysisResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUser _currentUser;
    private readonly IDocumentAnalysisQueue _queue;
    private readonly ILogger<BackfillDocumentAnalysisCommandHandler> _logger;

    public BackfillDocumentAnalysisCommandHandler(
        IApplicationDbContext context,
        IUser currentUser,
        IDocumentAnalysisQueue queue,
        ILogger<BackfillDocumentAnalysisCommandHandler> logger
    )
    {
        _context = context;
        _currentUser = currentUser;
        _queue = queue;
        _logger = logger;
    }

    public async Task<BackfillDocumentAnalysisResult> Handle(
        BackfillDocumentAnalysisCommand request,
        CancellationToken cancellationToken
    )
    {
        var member =
            await _context.OrganizationUsers.FirstOrDefaultAsync(
                user => user.UserId == _currentUser.Id,
                cancellationToken
            ) ?? throw new UnauthorizedAccessException("User is not a member of any organization.");

        if (member.Role != UserRole.Admin)
            throw new ForbiddenAccessException();

        var documents = await _context
            .Documents.Where(document =>
                document.OrganizationId == member.OrganizationId
                && document.Id > request.AfterDocumentId
                && (
                    document.AnalysisStatus == DocumentAnalysisStatus.Pending
                    || document.AnalysisStatus == DocumentAnalysisStatus.Extracted
                    || document.AnalysisStatus == DocumentAnalysisStatus.ExtractionFailed
                    || document.AnalysisStatus == DocumentAnalysisStatus.SummaryFailed
                    || document.AnalysisStatus == DocumentAnalysisStatus.DeferredMonthlyUnitLimit
                    || document.AnalysisStatus == DocumentAnalysisStatus.SummaryDeferredMonthlyLimit
                    || document.AnalysisStatus
                        == DocumentAnalysisStatus.SummaryDeferredModelUnavailable
                )
            )
            .OrderBy(document => document.Id)
            .Take(Math.Clamp(request.BatchSize, 1, 100))
            .ToListAsync(cancellationToken);

        foreach (var document in documents)
        {
            document.AnalysisStatus = document.ExtractedText is null
                ? DocumentAnalysisStatus.Pending
                : DocumentAnalysisStatus.Extracted;
            document.AnalysisErrorCode = null;
            document.AnalysisCompletedAt = null;
            document.AnalysisLeaseUntil = null;
            document.AnalysisQueuedAt = null;
            document.AnalysisAttemptCount = 0;
        }

        await _context.SaveChangesAsync(cancellationToken);

        var queuedCount = 0;
        foreach (var document in documents)
        {
            await _queue.EnqueueAsync(document.Id, cancellationToken);
            queuedCount++;
        }

        var lastId = documents.Count == 0 ? request.AfterDocumentId : documents[^1].Id;
        _logger.LogInformation(
            "Admin backfill queued {QueuedCount} documents for organisation {OrganizationId} through document {LastDocumentId}",
            queuedCount,
            member.OrganizationId,
            lastId
        );

        return new BackfillDocumentAnalysisResult(
            queuedCount,
            documents.Count < Math.Clamp(request.BatchSize, 1, 100) ? null : lastId
        );
    }
}
