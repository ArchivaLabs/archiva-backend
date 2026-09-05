using Archiva.Application.Dashboard.Queries.GetDashboardStats;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Archiva.Web.Endpoints.Dashboard;

public class Dashboard : IEndpointGroup
{
    public static string? RoutePrefix => "api/dashboard";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.RequireAuthorization();
        groupBuilder.MapGet(GetDashboardStatsHandler, "stats");
    }

    [EndpointSummary("Get dashboard statistics")]
    [EndpointDescription(
        "Returns aggregate counts for the authenticated user's organisation: "
            + "total meetings, total documents, and meetings added in the last seven days. "
            + "Counts are org-scoped and identical for Admin and User roles."
    )]
    public static async Task<Ok<GetDashboardStatsResult>> GetDashboardStatsHandler(ISender sender)
    {
        var result = await sender.Send(new GetDashboardStatsQuery());
        return TypedResults.Ok(result);
    }
}
