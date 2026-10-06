using Archiva.Application.Search.Dtos;
using Archiva.Application.Search.Queries;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Archiva.Web.Endpoints.Search;

public class Search : IEndpointGroup
{
    public static string? RoutePrefix => "api/search";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.RequireAuthorization();
        groupBuilder.MapPost(SearchDocumentsHandler);
        groupBuilder.MapGet(GetFilterOptionsHandler, "filter-options");
    }

    [EndpointSummary("Search meetings and documents")]
    [EndpointDescription(
        "Searches organisation-scoped meeting and document titles and content. "
            + "Results use the parent meeting date and are paginated."
    )]
    public static async Task<Ok<SearchResultsPageDto>> SearchDocumentsHandler(
        ISender sender,
        SearchDocumentsQuery query
    )
    {
        return TypedResults.Ok(await sender.Send(query));
    }

    [EndpointSummary("Get search filter options")]
    [EndpointDescription("Returns the authenticated organisation's available search tags.")]
    public static async Task<Ok<SearchFilterOptionsDto>> GetFilterOptionsHandler(ISender sender)
    {
        return TypedResults.Ok(await sender.Send(new GetSearchFilterOptionsQuery()));
    }
}
