using Archiva.Application.Common.Interfaces;
using Archiva.Domain.Entities;

namespace Archiva.Application.Meetings.Commands.DeleteMeeting;

public record DeleteMeetingCommand : IRequest
{
    public int Id { get; init; }
}

// Handler
public class DeleteMeetingCommandHandler : IRequestHandler<DeleteMeetingCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IStorageService _storageService;
    private readonly IUser _currentUser;

    public DeleteMeetingCommandHandler(
        IApplicationDbContext context,
        IStorageService storageService,
        IUser currentUser
    )
    {
        _context = context;
        _storageService = storageService;
        _currentUser = currentUser;
    }

    public async Task Handle(DeleteMeetingCommand request, CancellationToken cancellationToken)
    {
        // Look up the org from OrganizationUsers — same pattern as other handlers.
        var member =
            await _context.OrganizationUsers.FirstOrDefaultAsync(
                u => u.UserId == _currentUser.Id,
                cancellationToken
            ) ?? throw new UnauthorizedAccessException("User is not a member of any organisation.");

        // Load the meeting with its documents so we can delete blobs.
        var meeting =
            await _context
                .Meetings.Include(m => m.Documents)
                .Include(m => m.Tags)
                .FirstOrDefaultAsync(
                    m => m.Id == request.Id && m.OrganizationId == member.OrganizationId,
                    cancellationToken
                )
            ?? throw new NotFoundException(request.Id.ToString(), nameof(Meeting));

        // Delete each document's blob from storage first.
        // We do this before removing DB records so if a blob deletion fails
        // we still have the DB record and can retry — a missing blob with a
        // DB record is recoverable, a missing DB record with an orphaned blob
        // is harder to clean up.
        foreach (var document in meeting.Documents)
        {
            await _storageService.DeleteAsync(document.BlobName, cancellationToken);
        }

        // EF Core will cascade-delete MeetingTags and Documents
        // when the Meeting is removed, as long as cascade delete is
        // configured on the FK relationships in EF Core conventions.
        // If not, we remove them explicitly first.
        _context.MeetingTags.RemoveRange(meeting.Tags);
        _context.Documents.RemoveRange(meeting.Documents);
        _context.Meetings.Remove(meeting);

        await _context.SaveChangesAsync(cancellationToken);
    }
}
