using System.Text;
using Archiva.Infrastructure.Analysis;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using PdfSharpCore.Pdf;
using Shouldly;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Archiva.Application.UnitTests.Analysis;

public class AzureDocumentTextExtractorFormatTests
{
    private readonly AzureDocumentTextExtractor _extractor = new(
        new ConfigurationBuilder().Build()
    );

    [Test]
    public void EstimatesPdfPages()
    {
        using var document = new PdfDocument();
        document.AddPage();
        document.AddPage();
        using var stream = new MemoryStream();
        document.Save(stream, false);

        var estimate = _extractor.Estimate(stream.ToArray(), "pdf");

        estimate.BillableUnits.ShouldBe(2);
        estimate.TextCharacters.ShouldBe(0);
    }

    [TestCase(3000, 1)]
    [TestCase(3001, 2)]
    public void EstimatesWordCharactersAndRoundedBillableUnits(int characterCount, int units)
    {
        var content = CreateWordDocument(new string('W', characterCount));

        var estimate = _extractor.Estimate(content, "docx");

        estimate.BillableUnits.ShouldBe(units);
        estimate.TextCharacters.ShouldBe(characterCount);
    }

    [Test]
    public void EstimatesWorkbookSheetsAndTextFromInlineAndSharedStringCells()
    {
        var content = CreateWorkbook();

        var estimate = _extractor.Estimate(content, "XLSX");

        estimate.BillableUnits.ShouldBe(2);
        estimate.TextCharacters.ShouldBe("Council".Length + "Agenda".Length + "123".Length);
    }

    [TestCase("PDF")]
    [TestCase("DOCX")]
    [TestCase("XLSX")]
    public void RejectsMalformedBinaryDocuments(string fileType)
    {
        Should.Throw<InvalidDataException>(() => _extractor.Estimate([1, 2, 3, 4], fileType));
    }

    [Test]
    public void RejectsUnsupportedFormat()
    {
        Should.Throw<InvalidDataException>(() => _extractor.Estimate([1, 2, 3], "rtf"));
    }

    [Test]
    public async Task ExtractsUtf8TextWithByteOrderMark()
    {
        const string expected = "Minutes: café";
        var content = Encoding
            .UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(expected))
            .ToArray();

        var estimate = _extractor.Estimate(content, "txt");
        var extracted = await _extractor.ExtractAsync(content, "TXT");

        estimate.BillableUnits.ShouldBe(0);
        estimate.TextCharacters.ShouldBe(expected.Length);
        extracted.ShouldBe(expected);
    }

    [Test]
    public void ExtractRejectsMalformedUtf8Text()
    {
        Assert.ThrowsAsync<DecoderFallbackException>(async () =>
            await _extractor.ExtractAsync([0xC3, 0x28], "TXT")
        );
    }

    [Test]
    public void BinaryExtractionRequiresDocumentIntelligenceConfiguration()
    {
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _extractor.ExtractAsync([1, 2, 3], "PDF")
        );
    }

    private static byte[] CreateWordDocument(string text)
    {
        using var stream = new MemoryStream();
        using (
            var document = WordprocessingDocument.Create(
                stream,
                WordprocessingDocumentType.Document,
                true
            )
        )
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new W.Document(
                new W.Body(new W.Paragraph(new W.Run(new W.Text(text))))
            );
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    private static byte[] CreateWorkbook()
    {
        using var stream = new MemoryStream();
        using (
            var document = SpreadsheetDocument.Create(
                stream,
                SpreadsheetDocumentType.Workbook,
                true
            )
        )
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new S.Workbook();
            var sharedStrings = workbookPart.AddNewPart<SharedStringTablePart>();
            sharedStrings.SharedStringTable = new S.SharedStringTable(
                new S.SharedStringItem(new S.Text("Council"))
            );

            var firstSheet = workbookPart.AddNewPart<WorksheetPart>();
            firstSheet.Worksheet = new S.Worksheet(
                new S.SheetData(
                    new S.Row(
                        new S.Cell
                        {
                            DataType = S.CellValues.SharedString,
                            CellValue = new S.CellValue("0"),
                        },
                        new S.Cell
                        {
                            DataType = S.CellValues.InlineString,
                            InlineString = new S.InlineString(new S.Text("Agenda")),
                        },
                        new S.Cell
                        {
                            DataType = S.CellValues.Number,
                            CellValue = new S.CellValue("123"),
                        }
                    )
                )
            );

            var secondSheet = workbookPart.AddNewPart<WorksheetPart>();
            secondSheet.Worksheet = new S.Worksheet(new S.SheetData());
            workbookPart.Workbook.AppendChild(
                new S.Sheets(
                    new S.Sheet
                    {
                        Id = workbookPart.GetIdOfPart(firstSheet),
                        SheetId = 1U,
                        Name = "First",
                    },
                    new S.Sheet
                    {
                        Id = workbookPart.GetIdOfPart(secondSheet),
                        SheetId = 2U,
                        Name = "Second",
                    }
                )
            );
            workbookPart.Workbook.Save();
        }

        return stream.ToArray();
    }
}
