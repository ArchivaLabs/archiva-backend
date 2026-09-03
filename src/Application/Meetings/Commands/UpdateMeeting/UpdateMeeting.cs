using Archiva.Application.Common.Interfaces;
using Archiva.Application.Documents.Dtos;
using Archiva.Application.Meetings.Queries;
using Archiva.Domain.Entities;

namespace Archiva.Application.Meetings.Commands.UpdateMeeting;

public record UpdateMeetingCommand : IRequest<MeetingDetailDto>
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTime MeetingDate { get; init; }
    public TimeSpan MeetingTime { get; init; }
    public string? Location { get; init; }
    public List<string> Tags { get; init; } = [];
}

public class UpdateMeetingCommandHandler : IRequestHandler<UpdateMeetingCommand, MeetingDetailDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IStorageService _storageService;
    private readonly IUser _currentUser;

    public UpdateMeetingCommandHandler(
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
        UpdateMeetingCommand request,
        CancellationToken cancellationToken
    )
    {
        var member =
            await _context.OrganizationUsers.FirstOrDefaultAsync(
                u => u.UserId == _currentUser.Id,
                cancellationToken
            ) ?? throw new UnauthorizedAccessException("User is not a member of any organisation.");

        var meeting =
            await _context
                .Meetings.Include(m => m.Tags)
                    .ThenInclude(mt => mt.Tag)
                .Include(m => m.Documents)
                .FirstOrDefaultAsync(
                    m => m.Id == request.Id && m.OrganizationId == member.OrganizationId,
                    cancellationToken
                )
            ?? throw new NotFoundException(request.Id.ToString(), nameof(Meeting));

        meeting.Title = request.Title;
        meeting.Description = request.Description;
        meeting.MeetingDate = request.MeetingDate;
        meeting.MeetingTime = request.MeetingTime;
        meeting.Location = request.Location;

        var incomingTagNames = request
            .Tags.Select(t => t.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingTags = await _context
            .Tags.Where(t =>
                t.OrganizationId == member.OrganizationId && incomingTagNames.Contains(t.Name)
            )
            .ToListAsync(cancellationToken);

        var existingTagNames = existingTags
            .Select(t => t.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var newTags = incomingTagNames
            .Where(name => !existingTagNames.Contains(name))
            .Select(name => new Tag { Name = name, OrganizationId = member.OrganizationId })
            .ToList();

        if (newTags.Count > 0)
        {
            _context.Tags.AddRange(newTags);
            await _context.SaveChangesAsync(cancellationToken);
        }

        var allTags = existingTags.Concat(newTags).ToList();
        var allTagIds = allTags.Select(t => t.Id).ToHashSet();

        var tagsToRemove = meeting.Tags.Where(mt => !allTagIds.Contains(mt.TagId)).ToList();
        _context.MeetingTags.RemoveRange(tagsToRemove);

        var currentTagIds = meeting.Tags.Select(mt => mt.TagId).ToHashSet();
        var tagsToAdd = allTags
            .Where(t => !currentTagIds.Contains(t.Id))
            .Select(t => new MeetingTag { MeetingId = meeting.Id, TagId = t.Id })
            .ToList();

        _context.MeetingTags.AddRange(tagsToAdd);
        await _context.SaveChangesAsync(cancellationToken);

        // Mint SAS URLs for all nested documents — documents are already
        // in memory from the Include above, so no extra DB round trip.
        var documents = await Task.WhenAll(
            meeting.Documents.Select(async d => new DocumentDto
            {
                Id = d.Id,
                FileName = d.FileName,
                FileType = d.FileType,
                FileSizeInBytes = d.FileSizeInBytes,
                BlobUrl = await _storageService.GetReadUrlAsync(d.BlobName, cancellationToken),
                Description = d.Description,
                UploadedBy = d.CreatedBy,
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
            Tags = allTags.Select(t => t.Name).ToList(),
            Documents = [.. documents],
            Created = meeting.Created,
        };
    }
}
