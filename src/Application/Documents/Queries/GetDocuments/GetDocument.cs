using Archiva.Application.Common.Interfaces;
using Archiva.Application.Documents.Dtos;
using Archiva.Domain.Entities;

namespace Archiva.Application.Documents.Queries.GetDocuments;

public record GetDocumentQuery : IRequest<List<DocumentDto>>
{
    public int MeetingId { get; init; }
}

public class GetDocumentQueryHandler : IRequestHandler<GetDocumentQuery, List<DocumentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IStorageService _storageService;
    private readonly IUser _currentUser;

    public GetDocumentQueryHandler(
        IApplicationDbContext context,
        IStorageService storageService,
        IUser currentUser
    )
    {
        _context = context;
        _storageService = storageService;
        _currentUser = currentUser;
    }

    public async Task<List<DocumentDto>> Handle(
        GetDocumentQuery request,
        CancellationToken cancellationToken
    )
    {
        var member =
            await _context.OrganizationUsers.FirstOrDefaultAsync(
                u => u.UserId == _currentUser.Id,
                cancellationToken
            ) ?? throw new UnauthorizedAccessException("User is not a member of any organization.");

        var meetingExists = await _context.Meetings.AnyAsync(
            m => m.Id == request.MeetingId && m.OrganizationId == member.OrganizationId,
            cancellationToken
        );

        if (!meetingExists)
            throw new NotFoundException(request.MeetingId.ToString(), nameof(Meeting));

        // Project to a carrier that holds BlobName — never BlobUrl.
        // SAS URLs are minted after materialisation so they are always fresh
        // and only issued for rows that have already passed the org filter.
        var documents = await _context
            .Documents.Where(d =>
                d.MeetingId == request.MeetingId && d.OrganizationId == member.OrganizationId
            )
            .OrderByDescending(d => d.Created)
            .Select(d => new
            {
                d.Id,
                d.FileName,
                d.FileType,
                d.FileSizeInBytes,
                d.BlobName,
                d.Description,
                UploadedBy = d.CreatedBy,
                d.Created,
            })
            .ToListAsync(cancellationToken);

        // Mint SAS URLs in parallel — each call is a local crypto operation
        // under Azurite and a cached-key signing operation in Azure.
        var dtos = await Task.WhenAll(
            documents.Select(async d => new DocumentDto
            {
                Id = d.Id,
                FileName = d.FileName,
                FileType = d.FileType,
                FileSizeInBytes = d.FileSizeInBytes,
                BlobUrl = await _storageService.GetReadUrlAsync(d.BlobName, cancellationToken),
                Description = d.Description,
                UploadedBy = d.UploadedBy,
                Created = d.Created,
            })
        );

        return [.. dtos];
    }
}
