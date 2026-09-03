using Archiva.Application.Common.Interfaces;
using Archiva.Application.Documents.Dtos;
using Archiva.Domain.Enums;

namespace Archiva.Application.Meetings.Queries.GetMeetings;

public record GetMeetingByIdQuery : IRequest<MeetingDetailDto>
{
    public int Id { get; init; }
}

public class GetMeetingByIdQueryHandler : IRequestHandler<GetMeetingByIdQuery, MeetingDetailDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IStorageService _storageService;
    private readonly IUser _currentUser;

    public GetMeetingByIdQueryHandler(
        IApplicationDbContext context,
        IStorageService storageService,
        IUser currentUser
    )
    {
        _context = context;
        _storageService = storageService;
        _currentUser = currentUser;
    }

    public async Task<MeetingDetailDto> Handle(
        GetMeetingByIdQuery request,
        CancellationToken cancellationToken
    )
    {
        var member =
            await _context.OrganizationUsers.FirstOrDefaultAsync(
                u => u.UserId == _currentUser.Id,
                cancellationToken
            ) ?? throw new UnauthorizedAccessException("User is not a member of any organization.");

        var isAdmin = member.Role == UserRole.Admin;

        // Project to an anonymous carrier that holds BlobName, not BlobUrl.
        var meeting =
            await _context
                .Meetings.Where(m =>
                    m.Id == request.Id && m.OrganizationId == member.OrganizationId
                )
                .Select(m => new
                {
                    m.Id,
                    Title = m.Title!,
                    m.Description,
                    m.MeetingDate,
                    m.MeetingTime,
                    m.Location,
                    m.CreatedBy,
                    m.CreatedByAvatar,
                    m.CreatedById,
                    CanDelete = isAdmin || m.CreatedById == member.UserId,
                    Tags = m.Tags.Select(mt => mt.Tag.Name).ToList(),
                    Documents = m
                        .Documents.Select(d => new
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
                        .ToList(),
                    m.Created,
                })
                .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(request.Id.ToString(), "Meeting");

        // Mint SAS URLs after materialisation — only for documents that
        // cleared the org filter above.
        var documents = await Task.WhenAll(
            meeting.Documents.Select(async d => new DocumentDto
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

        return new MeetingDetailDto
        {
            Id = meeting.Id,
            Title = meeting.Title,
            Description = meeting.Description,
            MeetingDate = meeting.MeetingDate,
            MeetingTime = meeting.MeetingTime,
            Location = meeting.Location,
            CreatedBy = meeting.CreatedBy,
            CreatedByAvatar = meeting.CreatedByAvatar,
            CreatedById = meeting.CreatedById,
            CanDelete = meeting.CanDelete,
            Tags = meeting.Tags,
            Documents = [.. documents],
            Created = meeting.Created,
        };
    }
}
