using Archiva.Application.Common.Exceptions;
using Archiva.Application.Common.Interfaces;
using Archiva.Application.Common.Models;
using Archiva.Domain.Entities;
using Archiva.Domain.Enums;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ValidationException = Archiva.Application.Common.Exceptions.ValidationException;

namespace Archiva.Application.Documents.Commands.RetryDocumentAnalysis;

public record RetryDocumentAnalysisCommand(int DocumentId, int? UnitLimit, int? SummaryLimit)
    : IRequest<DocumentAnalysisActionResult>;

public record DocumentAnalysisActionResult(int DocumentId, DocumentAnalysisStatus AnalysisStatus);

public sealed class RetryDocumentAnalysisCommandHandler
    : IRequestHandler<RetryDocumentAnalysisCommand, DocumentAnalysisActionResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUser _currentUser;
    private readonly IDocumentAnalysisQueue _queue;
    private readonly DocumentAnalysisOptions _options;
    private readonly ILogger<RetryDocumentAnalysisCommandHandler> _logger;

    public RetryDocumentAnalysisCommandHandler(
        IApplicationDbContext context,
        IUser currentUser,
        IDocumentAnalysisQueue queue,
        IOptions<DocumentAnalysisOptions> options,
        ILogger<RetryDocumentAnalysisCommandHandler> logger
    )
    {
        _context = context;
        _currentUser = currentUser;
        _queue = queue;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DocumentAnalysisActionResult> Handle(
        RetryDocumentAnalysisCommand request,
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

        var document =
            await _context.Documents.FirstOrDefaultAsync(
                item =>
                    item.Id == request.DocumentId && item.OrganizationId == member.OrganizationId,
                cancellationToken
            ) ?? throw new NotFoundException(request.DocumentId.ToString(), nameof(Document));

        if (
            document.AnalysisStatus
            is DocumentAnalysisStatus.Completed
                or DocumentAnalysisStatus.Processing
        )
        {
            throw new ValidationException([
                new ValidationFailure(
                    nameof(document.AnalysisStatus),
                    "Only failed, deferred, pending, or extracted documents can be retried."
                ),
            ]);
        }

        if (request.UnitLimit is not null)
        {
            if (
                request.UnitLimit <= document.AnalysisUnitLimit
                || request.UnitLimit > _options.MaximumDocumentBillableUnitLimit
            )
            {
                throw new ValidationException([
                    new ValidationFailure(
                        nameof(request.UnitLimit),
                        $"The new limit must be greater than {document.AnalysisUnitLimit} and no more than {_options.MaximumDocumentBillableUnitLimit}."
                    ),
                ]);
            }

            document.AnalysisUnitLimit = request.UnitLimit.Value;
        }

        if (request.SummaryLimit is not null)
        {
            if (
                request.SummaryLimit <= document.SummaryInputCharacterLimit
                || request.SummaryLimit > _options.MaximumSummaryInputCharacters
            )
            {
                throw new ValidationException([
                    new ValidationFailure(
                        nameof(request.SummaryLimit),
                        $"The new limit must be greater than {document.SummaryInputCharacterLimit} and no more than {_options.MaximumSummaryInputCharacters}."
                    ),
                ]);
            }

            document.SummaryInputCharacterLimit = request.SummaryLimit.Value;
        }

        document.AnalysisStatus = document.ExtractedText is null
            ? DocumentAnalysisStatus.Pending
            : DocumentAnalysisStatus.Extracted;
        document.AnalysisErrorCode = null;
        document.AnalysisCompletedAt = null;
        document.AnalysisLeaseUntil = null;
        document.AnalysisQueuedAt = null;
        document.AnalysisAttemptCount = 0;
        await _context.SaveChangesAsync(cancellationToken);
        await _queue.EnqueueAsync(document.Id, cancellationToken);

        _logger.LogInformation(
            "Admin queued document analysis for {DocumentId} in organisation {OrganizationId} with status {AnalysisStatus}",
            document.Id,
            document.OrganizationId,
            document.AnalysisStatus
        );

        return new DocumentAnalysisActionResult(document.Id, document.AnalysisStatus);
    }
}
