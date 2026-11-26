using System.Security.Claims;
using Archiva.Application.Common.Interfaces;
using Archiva.Web.Authentication;
using Microsoft.Identity.Web;

namespace Archiva.Web.Services;

public class CurrentUser : IUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    public string AuthenticationProvider =>
        GoogleAuthentication.IsGoogle(Principal) ? "Google" : "Microsoft";

    public bool CanClaimInvitation =>
        AuthenticationProvider != "Google"
        || Principal?.HasClaim(GoogleAuthentication.InvitationClaim, "true") == true;

    public string? Id =>
        AuthenticationProvider == "Google"
            ? Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            : Principal?.GetObjectId();

    public string? Name =>
        _httpContextAccessor.HttpContext?.User?.FindFirstValue("name")
        ?? _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Name);

    public string? Email =>
        _httpContextAccessor.HttpContext?.User?.FindFirstValue("preferred_username")
        ?? _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Email);

    public string? OrganizationId =>
        _httpContextAccessor.HttpContext?.User?.FindFirstValue("OrganizationId");

    public List<string>? Role =>
        _httpContextAccessor
            .HttpContext?.User?.FindAll(ClaimTypes.Role)
            .Select(x => x.Value)
            .ToList();
}
