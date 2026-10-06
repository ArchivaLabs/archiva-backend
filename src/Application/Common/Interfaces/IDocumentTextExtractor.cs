namespace Archiva.Application.Common.Interfaces;

public record DocumentAnalysisEstimate(int BillableUnits, int TextCharacters);

public interface IDocumentTextExtractor
{
    DocumentAnalysisEstimate Estimate(byte[] content, string fileType);

    Task<string> ExtractAsync(
        byte[] content,
        string fileType,
        CancellationToken cancellationToken = default
    );
}
