namespace Archiva.Application.Documents.Commands.BackfillDocumentAnalysis;

public sealed class BackfillDocumentAnalysisCommandValidator
    : AbstractValidator<BackfillDocumentAnalysisCommand>
{
    public BackfillDocumentAnalysisCommandValidator()
    {
        RuleFor(command => command.AfterDocumentId).GreaterThanOrEqualTo(0);
        RuleFor(command => command.BatchSize).InclusiveBetween(1, 100);
    }
}
