using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Archiva.Application.FunctionalTests.Infrastructure;

public sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "FunctionalTest";
    public const string UserIdHeader = "X-Test-User-Id";
    public const string UserNameHeader = "X-Test-User-Name";
    public const string EmailHeader = "X-Test-User-Email";

    public TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    )
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (
            !Request.Headers.TryGetValue(UserIdHeader, out var userId)
            || string.IsNullOrWhiteSpace(userId)
        )
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var name = Request.Headers[UserNameHeader].ToString();
        var email = Request.Headers[EmailHeader].ToString();
        var claims = new[]
        {
            new Claim("oid", userId.ToString()),
            new Claim(
                "http://schemas.microsoft.com/identity/claims/objectidentifier",
                userId.ToString()
            ),
            new Claim("name", string.IsNullOrEmpty(name) ? "Test User" : name),
            new Claim(
                "preferred_username",
                string.IsNullOrEmpty(email) ? "test@example.invalid" : email
            ),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
