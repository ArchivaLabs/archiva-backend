using Archiva.Application.Common.Interfaces;
using Archiva.Application.Common.Models;
using Archiva.Application.Documents.Dtos;
using Archiva.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Archiva.Application.Documents.Commands.UploadDocument;

public record UploadDocumentCommand : IRequest<DocumentDto>
{
    public int MeetingId { get; init; }
    public Stream FileStream { get; init; } = null!;
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long FileSizeInBytes { get; init; }
    public string? Description { get; init; }
}

public class UploadDocumentCommandHandler : IRequestHandler<UploadDocumentCommand, DocumentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IStorageService _storageService;
    private readonly IUser _currentUser;
    private readonly IDocumentAnalysisQueue _analysisQueue;
    private readonly DocumentAnalysisOptions _analysisOptions;
    private readonly ILogger<UploadDocumentCommandHandler> _logger;

    public UploadDocumentCommandHandler(
        IApplicationDbContext context,
        IStorageService storageService,
        IUser currentUser,
        IDocumentAnalysisQueue analysisQueue,
        IOptions<DocumentAnalysisOptions> analysisOptions,
        ILogger<UploadDocumentCommandHandler> logger
    )
    {
        _context = context;
        _storageService = storageService;
        _currentUser = currentUser;
        _analysisQueue = analysisQueue;
        _analysisOptions = analysisOptions.Value;
        _logger = logger;
    }

    public async Task<DocumentDto> Handle(
        UploadDocumentCommand request,
        CancellationToken cancellationToken
    )
    {
        var member =
            await _context.OrganizationUsers.FirstOrDefaultAsync(
                u => u.UserId == _currentUser.Id,
                cancellationToken
            ) ?? throw new UnauthorizedAccessException("User is not a member of any organization");

        var meeting =
            await _context.Meetings.FirstOrDefaultAsync(
                m => m.Id == request.MeetingId && m.OrganizationId == member.OrganizationId,
                cancellationToken
            ) ?? throw new NotFoundException(request.MeetingId.ToString(), nameof(Meeting));

        var fileType = Path.GetExtension(request.FileName).TrimStart('.').ToUpperInvariant();

        // UploadAsync now returns only the blob name — not a URL.
        var blobName = await _storageService.UploadAsync(
            request.FileStream,
            request.FileName,
            request.ContentType,
            cancellationToken
        );

        var document = new Document
        {
            FileName = request.FileName,
            BlobUrl = blobName, // Store the blob name in BlobUrl for now;
            BlobName = blobName, // BlobName is the canonical reference used for deletes/SAS.
            FileType = fileType,
            FileSizeInBytes = request.FileSizeInBytes,
            Description = request.Description,
            MeetingId = meeting.Id,
            OrganizationId = member.OrganizationId,
            AnalysisUnitLimit = _analysisOptions.DefaultDocumentBillableUnitLimit,
            SummaryInputCharacterLimit = _analysisOptions.DefaultDocumentSummaryCharacterLimit,
        };

        _context.Documents.Add(document);
        await _context.SaveChangesAsync(cancellationToken);

        try
        {
            await _analysisQueue.EnqueueAsync(document.Id, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Document analysis queue publication failed for {DocumentId} in organisation {OrganizationId}; it will be reconciled. Error type: {ErrorType}",
                document.Id,
                member.OrganizationId,
                exception.GetType().Name
            );
        }

        // Mint a fresh SAS URL for the response — the client gets a
        // short-lived signed URL, never a permanent public blob URL.
        var sasUrl = await _storageService.GetReadUrlAsync(blobName, cancellationToken);

        return new DocumentDto
        {
            Id = document.Id,
            FileName = document.FileName,
            FileType = document.FileType,
            FileSizeInBytes = document.FileSizeInBytes,
            BlobUrl = sasUrl,
            Description = document.Description,
            UploadedBy = _currentUser.Name,
            Created = document.Created,
            AnalysisStatus = document.AnalysisStatus,
            AnalysisErrorCode = document.AnalysisErrorCode,
            Summary = document.Summary,
            AnalysisCompletedAt = document.AnalysisCompletedAt,
        };
    }
}
