using Archiva.Application.Common.Interfaces;
using Archiva.Application.Documents.Dtos;
using Archiva.Application.Documents.Queries;
using Archiva.Domain.Entities;

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

    public UploadDocumentCommandHandler(
        IApplicationDbContext context,
        IStorageService storageService,
        IUser currentUser
    )
    {
        _context = context;
        _storageService = storageService;
        _currentUser = currentUser;
    }

    // Handle
    public async Task<DocumentDto> Handle(
        UploadDocumentCommand request,
        CancellationToken cancellationToken
    )
    {
        // Look up the organization
        var member =
            await _context.OrganizationUsers.FirstOrDefaultAsync(
                u => u.UserId == _currentUser.Id,
                cancellationToken
            ) ?? throw new UnauthorizedAccessException("User is not a member of any organization");

        // Confirm the meeting exists and belongs to the organization
        var meeting =
            await _context.Meetings.FirstOrDefaultAsync(
                m => m.Id == request.MeetingId && m.OrganizationId == member.OrganizationId,
                cancellationToken
            ) ?? throw new NotFoundException(request.MeetingId.ToString(), nameof(Meeting));

        // Derive the file type from the extension.
        var fileType = Path.GetExtension(request.FileName).TrimStart('.').ToUpperInvariant();

        // Uplead to the blob storage
        var (blobUrl, blobName) = await _storageService.UploadAsync(
            request.FileStream,
            request.FileName,
            request.ContentType,
            cancellationToken
        );

        // Save the document record
        var document = new Document
        {
            FileName = request.FileName,
            BlobUrl = blobUrl,
            BlobName = blobName,
            FileType = fileType,
            FileSizeInBytes = request.FileSizeInBytes,
            Description = request.Description,
            MeetingId = meeting.Id,
            OrganizationId = member.OrganizationId,
        };

        _context.Documents.Add(document);
        await _context.SaveChangesAsync(cancellationToken);

        return new DocumentDto
        {
            Id = document.Id,
            FileName = document.FileName,
            FileType = document.FileType,
            FileSizeInBytes = document.FileSizeInBytes,
            BlobUrl = document.BlobUrl,
            Description = document.Description,
            UploadedBy = _currentUser.Name,
            Created = document.Created,
        };
    }
}
