using System.Text.Json.Serialization;

namespace Archiva.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DocumentAnalysisStatus
{
    Pending = 1,
    Processing = 2,
    Extracted = 3,
    SummaryDeferredMonthlyLimit = 4,
    SummaryDeferredDocumentLimit = 5,
    Completed = 6,
    ExtractionFailed = 7,
    SummaryFailed = 8,
    DeferredMonthlyUnitLimit = 9,
    DeferredDocumentUnitLimit = 10,
    SummaryDeferredModelUnavailable = 11,
}
