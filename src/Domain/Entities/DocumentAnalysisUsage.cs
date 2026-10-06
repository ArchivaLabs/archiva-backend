namespace Archiva.Domain.Entities;

public class DocumentAnalysisUsage
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public DateTime MonthStartUtc { get; set; }
    public int BillableUnits { get; set; }
    public int SummaryInputCharacters { get; set; }
}
