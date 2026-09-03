using Archiva.Application.Auth.Command.SyncUser;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Archiva.Web.Endpoints.Auth;

public class SyncUser : IEndpointGroup
{
    public static string? RoutePrefix => "api/auth";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        // RequireAuthorization — the caller must hold a valid Microsoft JWT.
        // The endpoint runs after Microsoft login so a token is always present.
        // AllowAnonymous was never necessary and exposed org-membership data
        // to unauthenticated callers.
        groupBuilder.RequireAuthorization();
        groupBuilder.MapPost(SyncUserHandler, "/sync");
    }

    [EndpointSummary("Sync Microsoft user with Archiva")]
    [EndpointDescription(
        "Called after Microsoft login to sync the user with the Archiva database. "
            + "Returns the user's status: 'new', 'invited', or 'existing'. "
            + "All identity fields (userId, email, displayName) are read from the "
            + "validated JWT — the request body is empty."
    )]
    public static async Task<Ok<SyncUserResult>> SyncUserHandler(ISender sender)
    {
        var result = await sender.Send(new SyncUserCommand());
        return TypedResults.Ok(result);
    }
}
