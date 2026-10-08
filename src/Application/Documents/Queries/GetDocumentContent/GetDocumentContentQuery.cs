using Archiva.Application.Common.Interfaces;
using Archiva.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Archiva.Application.Documents.Queries.GetDocumentContent;

public record GetDocumentContentQuery(int DocumentId) : IRequest<DocumentContentResult>;

public record DocumentContentResult(byte[] Content, string ContentType);

public sealed class GetDocumentContentQueryHandler
    : IRequestHandler<GetDocumentContentQuery, DocumentContentResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IStorageService _storage;
    private readonly IUser _currentUser;
    private readonly ILogger<GetDocumentContentQueryHandler> _logger;

    public GetDocumentContentQueryHandler(
        IApplicationDbContext context,
        IStorageService storage,
        IUser currentUser,
        ILogger<GetDocumentContentQueryHandler> logger
    )
    {
        _context = context;
        _storage = storage;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<DocumentContentResult> Handle(
        GetDocumentContentQuery request,
        CancellationToken cancellationToken
    )
    {
        var member = await _context.OrganizationUsers.FirstOrDefaultAsync(
            user => user.UserId == _currentUser.Id,
            cancellationToken
        );
        if (member is null)
        {
            _logger.LogWarning("Document preview denied: user has no organisation membership");
            throw new UnauthorizedAccessException("User is not a member of any organization.");
        }

        var document = await _context
            .Documents.Where(item =>
                item.Id == request.DocumentId
                && item.OrganizationId == member.OrganizationId
                && item.Meeting.OrganizationId == member.OrganizationId
            )
            .Select(item => new { item.BlobName, item.FileType })
            .FirstOrDefaultAsync(cancellationToken);
        if (document is null)
        {
            _logger.LogWarning(
                "Document preview denied or not found for document {DocumentId}",
                request.DocumentId
            );
            throw new NotFoundException(request.DocumentId.ToString(), nameof(Document));
        }

        var contentType = document.FileType.ToUpperInvariant() switch
        {
            "PDF" => "application/pdf",
            "DOCX" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "XLSX" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "TXT" => "text/plain; charset=utf-8",
            _ => "application/octet-stream",
        };

        byte[] content;
        try
        {
            content = await _storage.DownloadAsync(document.BlobName, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "Document preview download failed for document {DocumentId}",
                request.DocumentId
            );
            throw;
        }
        _logger.LogInformation(
            "Document preview served for document {DocumentId} with {ByteCount} bytes",
            request.DocumentId,
            content.Length
        );
        return new DocumentContentResult(content, contentType);
    }
}
