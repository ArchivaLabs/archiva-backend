namespace Archiva.Application.Common.Interfaces;

public interface IDocumentSummarizer
{
    bool IsConfigured { get; }

    Task<string> SummarizeAsync(
        string fileName,
        string extractedText,
        CancellationToken cancellationToken = default
    );
}
