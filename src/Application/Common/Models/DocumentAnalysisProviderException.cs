namespace Archiva.Application.Common.Models;

public sealed class DocumentAnalysisProviderException : Exception
{
    public DocumentAnalysisProviderException(
        string publicErrorCode,
        string providerErrorCode,
        bool isTransient = false,
        Exception? innerException = null
    )
        : base("A document analysis provider request failed.", innerException)
    {
        PublicErrorCode = publicErrorCode;
        ProviderErrorCode = providerErrorCode;
        IsTransient = isTransient;
    }

    public string PublicErrorCode { get; }
    public string ProviderErrorCode { get; }
    public bool IsTransient { get; }
}
