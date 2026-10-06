namespace Archiva.Application.Common.Models;

public sealed class DocumentAnalysisOptions
{
    public const string SectionName = "DocumentAnalysis";

    public int MonthlyBillableUnitLimit { get; set; } = 100;
    public int MonthlySummaryCharacterLimit { get; set; } = 500_000;
    public int DefaultDocumentBillableUnitLimit { get; set; } = 20;
    public int DefaultDocumentSummaryCharacterLimit { get; set; } = 100_000;
    public int MaximumDocumentBillableUnitLimit { get; set; } = 2_000;
    public int MaximumSummaryOutputTokens { get; set; } = 500;
    public int MaximumSummaryInputCharacters { get; set; } = 500_000;
    public int MaximumQueueAttempts { get; set; } = 5;
}
