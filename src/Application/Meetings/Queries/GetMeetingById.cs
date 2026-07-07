using Archiva.Application.Common.Interfaces;
using Archiva.Application.Documents.Queries;

namespace Archiva.Application.Meetings.Queries.GetMeetingById;

public record GetMeetingByIdQuery : IRequest<MeetingDetailDto>
{
    public int Id { get; init; }
}

public class GetMeetingByIdQueryHandler : IRequestHandler<GetMeetingByIdQuery, MeetingDetailDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUser _currentUser;

    public GetMeetingByIdQueryHandler(IApplicationDbContext context, IUser currentUser)
    {
        _context = context;
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
            )
            ?? throw new UnauthorizedAccessException("Users is not a member of any organization.");

        var meeting =
            await _context
                .Meetings.Where(m =>
                    m.Id == request.Id && m.OrganizationId == member.OrganizationId
                )
                .Select(m => new MeetingDetailDto
                {
                    Id = m.Id,
                    Title = m.Title!,
                    Description = m.Description,
                    MeetingDate = m.MeetingDate,
                    MeetingTime = m.MeetingTime,
                    CreatedBy = m.CreatedBy,
                    CreatedByAvatar = m.CreatedByAvatar,
                    Tags = m.Tags.Select(mt => mt.Tag.Name).ToList(),
                    Documents = m
                        .Documents.Select(d => new DocumentDto
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
                        .ToList(),
                    Created = m.Created,
                })
                .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(request.Id.ToString(), "Meeting");

        return meeting;
    }
}
