namespace Archiva.Application.Common.Interfaces;

public interface IDocumentAnalysisBudget
{
    Task<bool> TryReserveBillableUnitsAsync(
        int documentId,
        int billableUnits,
        int perDocumentLimit,
        CancellationToken cancellationToken = default
    );

    Task<bool> TryReserveSummaryCharactersAsync(
        int documentId,
        int characterCount,
        int perDocumentLimit,
        CancellationToken cancellationToken = default
    );
}
