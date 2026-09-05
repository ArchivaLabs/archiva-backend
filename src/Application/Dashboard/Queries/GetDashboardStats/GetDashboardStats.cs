using Archiva.Application.Common.Interfaces;

namespace Archiva.Application.Dashboard.Queries.GetDashboardStats;

public record GetDashboardStatsQuery : IRequest<GetDashboardStatsResult>;

public record GetDashboardStatsResult
{
    public int MeetingCount { get; init; }
    public int DocumentCount { get; init; }
    public int MeetingsAddedThisWeek { get; init; }
}

public class GetDashboardStatsQueryHandler
    : IRequestHandler<GetDashboardStatsQuery, GetDashboardStatsResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUser _currentUser;

    public GetDashboardStatsQueryHandler(IApplicationDbContext context, IUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<GetDashboardStatsResult> Handle(
        GetDashboardStatsQuery request,
        CancellationToken cancellationToken
    )
    {
        var member =
            await _context.OrganizationUsers.FirstOrDefaultAsync(
                u => u.UserId == _currentUser.Id,
                cancellationToken
            ) ?? throw new UnauthorizedAccessException("User is not a member of any organization");

        var organizationId = member.OrganizationId;

        var meetingCount = await _context
            .Meetings.Where(m => m.OrganizationId == organizationId)
            .CountAsync(cancellationToken);

        // Documents carry OrganizationId directly (set on upload), so this counts
        // without joining through Meetings.
        var documentCount = await _context
            .Documents.Where(d => d.OrganizationId == organizationId)
            .CountAsync(cancellationToken);

        // A rolling 7 days, not a calendar week — a calendar week collapses this
        // number to zero every Monday, which reads as an outage rather than a quiet week.
        var since = DateTimeOffset.UtcNow.AddDays(-7);

        var meetingsAddedThisWeek = await _context
            .Meetings.Where(m => m.OrganizationId == organizationId && m.Created >= since)
            .CountAsync(cancellationToken);

        return new GetDashboardStatsResult
        {
            MeetingCount = meetingCount,
            DocumentCount = documentCount,
            MeetingsAddedThisWeek = meetingsAddedThisWeek,
        };
    }
}
