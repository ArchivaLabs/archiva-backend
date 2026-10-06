namespace Archiva.Application.Common.Models;

public static class DocumentAnalysisTiming
{
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan LeaseRenewalInterval = TimeSpan.FromMinutes(5);
}
