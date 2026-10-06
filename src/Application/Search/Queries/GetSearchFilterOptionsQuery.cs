using Archiva.Application.Common.Interfaces;
using Archiva.Application.Search.Dtos;

namespace Archiva.Application.Search.Queries;

public record GetSearchFilterOptionsQuery : IRequest<SearchFilterOptionsDto>;

public sealed class GetSearchFilterOptionsQueryHandler
    : IRequestHandler<GetSearchFilterOptionsQuery, SearchFilterOptionsDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUser _currentUser;

    public GetSearchFilterOptionsQueryHandler(IApplicationDbContext context, IUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<SearchFilterOptionsDto> Handle(
        GetSearchFilterOptionsQuery request,
        CancellationToken cancellationToken
    )
    {
        var member =
            await _context.OrganizationUsers.FirstOrDefaultAsync(
                user => user.UserId == _currentUser.Id,
                cancellationToken
            ) ?? throw new UnauthorizedAccessException("User is not a member of any organization");

        var tags = await _context
            .Tags.Where(tag => tag.OrganizationId == member.OrganizationId)
            .OrderBy(tag => tag.Name)
            .Select(tag => tag.Name)
            .Distinct()
            .ToListAsync(cancellationToken);

        return new SearchFilterOptionsDto { Tags = tags };
    }
}
