using Archiva.Application.Common.Interfaces;

namespace Archiva.Application.Meetings.Queries.GetMeetings;

public record GetMeetingsQuery : IRequest<GetMeetingsResult>
{
    public int? Page { get; init; } = 1;
    public int? PageSize { get; init; } = 10;
}

public record GetMeetingsResult
{
    public List<MeetingDto> Meetings { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages { get; init; }
    public bool HasNextPage { get; init; }
    public bool HasPreviousPage { get; init; }
}

// Handler
public class GetMeetingsQueryHandler : IRequestHandler<GetMeetingsQuery, GetMeetingsResult>
{
    private const int MaxPageSize = 50;
    private readonly IApplicationDbContext _context;
    private readonly IUser _currentUser;

    public GetMeetingsQueryHandler(IApplicationDbContext context, IUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    // Handle Method
    public async Task<GetMeetingsResult> Handle(
        GetMeetingsQuery request,
        CancellationToken cancellationToken
    )
    {
        // Get the user organization from OrganizationUsers
        var member =
            await _context.OrganizationUsers.FirstOrDefaultAsync(
                u => u.UserId == _currentUser.Id,
                cancellationToken
            ) ?? throw new UnauthorizedAccessException("User is not a member of any organization");

        var organizationId = member.OrganizationId;

        // Clamp pageSize between 1 and MaxPageSize regardless of the amount requested.
        var pageSize = Math.Clamp(request.PageSize ?? 10, 1, MaxPageSize);
        var page = Math.Max(request.Page ?? 1, 1);

        var query = _context
            .Meetings.Where(m => m.OrganizationId == organizationId)
            .OrderByDescending(m => m.MeetingDate)
            .ThenByDescending(m => m.Created);

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var meetings = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MeetingDto
            {
                Id = m.Id,
                Title = m.Title!,
                Description = m.Description,
                MeetingDate = m.MeetingDate,
                MeetingTime = m.MeetingTime,
                CreatedBy = m.CreatedBy,
                CreatedByAvatar = m.CreatedByAvatar,
                Tags = m.Tags.Select(mt => mt.Tag.Name).ToList(),
                DocumentCount = m.Documents.Count,
                Created = m.Created,
            })
            .ToListAsync(cancellationToken);

        return new GetMeetingsResult
        {
            Meetings = meetings,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages,
            HasNextPage = page < totalPages,
            HasPreviousPage = page > 1,
        };
    }
}
