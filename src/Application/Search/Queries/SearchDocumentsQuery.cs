using Archiva.Application.Common.Interfaces;
using Archiva.Application.Search.Dtos;

namespace Archiva.Application.Search.Queries;

public record SearchDocumentsQuery : IRequest<SearchResultsPageDto>
{
    public string SearchTerm { get; init; } = string.Empty;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string SortBy { get; init; } = "relevance";
    public bool IncludeMeetings { get; init; } = true;
    public bool IncludeDocuments { get; init; } = true;
    public bool SearchTitles { get; init; } = true;
    public bool SearchContent { get; init; } = true;
    public DateOnly? DateFrom { get; init; }
    public DateOnly? DateTo { get; init; }
    public List<string> Tags { get; init; } = [];
}

public sealed class SearchDocumentsQueryHandler
    : IRequestHandler<SearchDocumentsQuery, SearchResultsPageDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUser _currentUser;
    private readonly IDocumentSearchService _searchService;

    public SearchDocumentsQueryHandler(
        IApplicationDbContext context,
        IUser currentUser,
        IDocumentSearchService searchService
    )
    {
        _context = context;
        _currentUser = currentUser;
        _searchService = searchService;
    }

    public async Task<SearchResultsPageDto> Handle(
        SearchDocumentsQuery request,
        CancellationToken cancellationToken
    )
    {
        var member =
            await _context.OrganizationUsers.FirstOrDefaultAsync(
                user => user.UserId == _currentUser.Id,
                cancellationToken
            ) ?? throw new UnauthorizedAccessException("User is not a member of any organization");

        return await _searchService.SearchAsync(request, member.OrganizationId, cancellationToken);
    }
}

public sealed class SearchDocumentsQueryValidator : AbstractValidator<SearchDocumentsQuery>
{
    public SearchDocumentsQueryValidator()
    {
        RuleFor(query => query.SearchTerm).NotNull().MaximumLength(200);
        RuleFor(query => query.Page).InclusiveBetween(1, 100_000);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 50);
        RuleFor(query => query.SortBy)
            .Must(sortBy => sortBy is "relevance" or "date_desc" or "date_asc");
        RuleFor(query => query.Tags)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(tags => tags.Count <= 20);
        RuleForEach(query => query.Tags).NotEmpty().MaximumLength(100);
        RuleFor(query => query)
            .Must(query =>
                query.DateFrom is null || query.DateTo is null || query.DateFrom <= query.DateTo
            )
            .WithMessage("DateFrom must be on or before DateTo.");
        RuleFor(query => query.DateTo)
            .Must(date => date is null || date < DateOnly.MaxValue)
            .WithMessage("DateTo must be before 9999-12-31.");
    }
}
