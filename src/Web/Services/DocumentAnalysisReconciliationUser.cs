using Archiva.Application.Common.Interfaces;

namespace Archiva.Web.Services;

/// <summary>
/// Provides a neutral identity for the scheduled reconciliation process.
/// </summary>
public sealed class DocumentAnalysisReconciliationUser : IUser
{
    public string? Id => null;

    public string? Name => null;

    public string? Email => null;

    public string? OrganizationId => null;

    public List<string>? Role => null;
}
