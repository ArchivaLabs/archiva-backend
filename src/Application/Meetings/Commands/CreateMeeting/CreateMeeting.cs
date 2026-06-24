using Archiva.Application.Common.Interfaces;
using Archiva.Domain.Entities;

namespace Archiva.Application.Meetings.Commands.CreateMeeting;

// The command describes what you want to do and the kind of data it carries.
public record CreateMeetingCommand : IRequest<int>
{
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTime MeetingDate { get; init; }
    public TimeSpan MeetingTime { get; init; }
    public List<string> Tags { get; init; } = new();
}

// The handler is what actually does the work.
public class CreateMeetingCommandHandler : IRequestHandler<CreateMeetingCommand, int>
{
    private readonly IApplicationDbContext _context;

    public CreateMeetingCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<int> Handle(CreateMeetingCommand request, CancellationToken cancellationToken)
    {
        // Create the new meeting info.
        var meeting = new Meeting
        {
            Title = request.Title,
            Description = request.Description,
            MeetingDate = request.MeetingDate,
            MeetingTime = request.MeetingTime,
        };

        // Handle each tag from the user separately.
        foreach (var tagName in request.Tags)
        {
            // Check if tag already exists in the org.
            var existingTag = await _context.Tags.FirstOrDefaultAsync(t => t.Name == tagName && t.OrganizationId == request.OrganizationId)
        }
    }
}
