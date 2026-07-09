using Archiva.Application.Common.Interfaces;
using Archiva.Application.Documents.Dtos;
using Archiva.Domain.Entities;

namespace Archiva.Application.Documents.Queries.GetDocuments;

public record GetDocumentQuery : IRequest<List<DocumentDto>>
{
    public int MeetingId { get; init; }
}

public class GetDocumentsQueryHandler : IRequestHandler<GetDocumentQuery, List<DocumentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUser _currentUser;

    public GetDocumentsQueryHandler(IApplicationDbContext context, IUser currentUser)
    {
        _context = context;
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

        return await _context
            .Documents.Where(d =>
                d.MeetingId == request.MeetingId && d.OrganizationId == member.OrganizationId
            )
            .OrderByDescending(d => d.Created)
            .Select(d => new DocumentDto
            {
                Id = d.Id,
                FileName = d.FileName,
                FileType = d.FileType,
                FileSizeInBytes = d.FileSizeInBytes,
                BlobUrl = d.BlobUrl,
                Description = d.Description,
                UploadedBy = d.CreatedBy,
                Created = d.Created,
            })
            .ToListAsync(cancellationToken);
    }
}
