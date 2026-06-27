using Archiva.Application.Auth.Command.SyncUser;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Archiva.Web.Endpoints.Auth;

public class SyncUser : IEndpointGroup
{
    public static string? RoutePrefix => "api/auth";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.AllowAnonymous();
        groupBuilder.MapPost(SyncUserHandler, "/sync");
    }

    [EndpointSummary("Sync Microsoft user with Archiva")]
    [EndpointDescription(
        "Called after Microsoft login to sync the user with the Archiva database. "
            + "Returns the user's status: 'new', 'invited', or 'existing'."
    )]
    public static async Task<Ok<SyncUserResult>> SyncUserHandler(
        ISender sender,
        SyncUserCommand command
    )
    {
        var result = await sender.Send(command);
        return TypedResults.Ok(result);
    }
}
