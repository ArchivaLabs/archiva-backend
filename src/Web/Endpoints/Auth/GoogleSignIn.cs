using System.Security.Claims;
using Archiva.Web.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;

namespace Archiva.Web.Endpoints.Auth;

public sealed class GoogleSignIn : IEndpointGroup
{
    public static string? RoutePrefix => "api/auth/google";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.RequireAuthorization();
        groupBuilder.MapGet(Start, "/start").AllowAnonymous();
        groupBuilder.MapGet(Session, "/session");
        groupBuilder.MapPost(Logout, "/logout");
    }

    [EndpointSummary("Start Google sign-in")]
    public static IResult Start(HttpContext context, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("GoogleAuth:Enabled"))
            return TypedResults.NotFound();

        GoogleAuthentication
            .Log(context)
            .LogInformation("External sign-in {Provider} {Outcome}", "Google", "Started");
        return TypedResults.Challenge(
            new AuthenticationProperties { RedirectUri = "/auth/google/complete" },
            [GoogleAuthentication.Oidc]
        );
    }

    [EndpointSummary("Restore a Google session and obtain its request token")]
    public static IResult Session(HttpContext context, IAntiforgery antiforgery)
    {
        if (!GoogleAuthentication.IsGoogle(context.User))
            return TypedResults.Unauthorized();

        var csrf = antiforgery.GetAndStoreTokens(context).RequestToken;
        var session = context.User.FindFirstValue(GoogleAuthentication.SessionClaim);
        return TypedResults.Ok(
            new GoogleSessionResponse(
                session!,
                csrf!,
                context.User.FindFirstValue(ClaimTypes.NameIdentifier)!
            )
        );
    }

    [EndpointSummary("End a Google session")]
    public static async Task<IResult> Logout(HttpContext context)
    {
        if (!GoogleAuthentication.IsGoogle(context.User))
            return TypedResults.Unauthorized();

        await context.SignOutAsync(GoogleAuthentication.Cookie);
        GoogleAuthentication
            .Log(context)
            .LogInformation("External session {Provider} {Outcome}", "Google", "Revoked");
        return TypedResults.NoContent();
    }

    public sealed record GoogleSessionResponse(string Session, string Csrf, string UserId);
}
