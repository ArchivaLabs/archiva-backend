using System.Security.Claims;
using Archiva.Application.Common.Interfaces;
using Microsoft.Identity.Web;

namespace Archiva.Web.Services;

public class CurrentUser : IUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? Id => _httpContextAccessor.HttpContext?.User?.GetObjectId();

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
