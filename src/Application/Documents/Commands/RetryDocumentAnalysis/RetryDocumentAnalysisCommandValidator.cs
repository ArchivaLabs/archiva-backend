namespace Archiva.Application.Documents.Commands.RetryDocumentAnalysis;

public sealed class RetryDocumentAnalysisCommandValidator
    : AbstractValidator<RetryDocumentAnalysisCommand>
{
    public RetryDocumentAnalysisCommandValidator()
    {
        RuleFor(command => command.DocumentId).GreaterThan(0);
        RuleFor(command => command.UnitLimit)
            .GreaterThan(0)
            .When(command => command.UnitLimit.HasValue);
        RuleFor(command => command.SummaryLimit)
            .GreaterThan(0)
            .When(command => command.SummaryLimit.HasValue);
    }
}
