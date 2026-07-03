using Archiva.Application.Common.Interfaces;
using Archiva.Domain.Entities;

namespace Archiva.Application.Meetings.Commands.CreateMeeting;

// The command describes what you want to do and the kind of data it carries.
public record CreateMeetingCommand : IRequest<CreateMeetingResult>
{
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTime MeetingDate { get; init; }
    public TimeSpan MeetingTime { get; init; }
    public List<string> Tags { get; init; } = [];
}

public record CreateMeetingResult
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTime MeetingDate { get; init; }
    public TimeSpan MeetingTime { get; init; }
    public int OrganizationId { get; init; }
    public List<string> Tags { get; init; } = [];
}

// The handler is what actually does the work.
public class CreateMeetingCommandHandler
    : IRequestHandler<CreateMeetingCommand, CreateMeetingResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUser _currentUser;

    public CreateMeetingCommandHandler(IApplicationDbContext context, IUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<CreateMeetingResult> Handle(
        CreateMeetingCommand request,
        CancellationToken cancellationToken
    )
    {
        // OrganizationId isn't in the Microsoft JWT, so we get it from OrganizationUsers using the authenticated user's ID
        var member =
            await _context.OrganizationUsers.FirstOrDefaultAsync(
                u => u.UserId == _currentUser.Id,
                cancellationToken
            ) ?? throw new UnauthorizedAccessException("User is not a member of any organization");

        // Get the organization id from the authenticated user
        var organizationId = member.OrganizationId;

        // Resolving the tags. Check if the tag exists in the org.
        var incomingTagNames = request
            .Tags.Select(t => t.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingTags = await _context
            .Tags.Where(t =>
                t.OrganizationId == organizationId && incomingTagNames.Contains(t.Name)
            )
            .ToListAsync(cancellationToken);

        var existingTagNames = existingTags
            .Select(t => t.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // If tag doesn't exist, create one.
        var newTags = incomingTagNames
            .Where(name => !existingTagNames.Contains(name))
            .Select(name => new Tag { Name = name, OrganizationId = organizationId })
            .ToList();

        if (newTags.Count > 0)
        {
            _context.Tags.AddRange(newTags);
            await _context.SaveChangesAsync(cancellationToken);
        }

        var allTags = existingTags.Concat(newTags).ToList();

        // Create the new meeting info.
        var meeting = new Meeting
        {
            Title = request.Title,
            Description = request.Description,
            MeetingDate = request.MeetingDate,
            MeetingTime = request.MeetingTime,
            OrganizationId = organizationId,
            CreatedByAvatar = member.AvatarUrl,
        };

        // Add the newly created meeting to the Meeting Table on the db.
        _context.Meetings.Add(meeting);
        await _context.SaveChangesAsync(cancellationToken);

        // Wire Up the MeetingTag junction records
        var meetingTags = allTags
            .Select(tag => new MeetingTag { MeetingId = meeting.Id, TagId = tag.Id })
            .ToList();

        _context.MeetingTags.AddRange(meetingTags);
        await _context.SaveChangesAsync(cancellationToken);

        return new CreateMeetingResult
        {
            Id = meeting.Id,
            Title = meeting.Title!,
            Description = meeting.Description,
            MeetingDate = meeting.MeetingDate,
            MeetingTime = meeting.MeetingTime,
            OrganizationId = meeting.OrganizationId,
            Tags = allTags.Select(t => t.Name).ToList(),
        };
    }
}
