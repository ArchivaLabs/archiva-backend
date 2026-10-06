namespace Archiva.Application.Common.Interfaces;

public interface IDocumentAnalysisQueue
{
    Task EnqueueAsync(int documentId, CancellationToken cancellationToken = default);
}
