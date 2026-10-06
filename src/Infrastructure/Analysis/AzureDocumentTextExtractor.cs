using System.Text;
using Archiva.Application.Common.Interfaces;
using Archiva.Application.Common.Models;
using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Identity;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Configuration;
using PdfSharpCore.Pdf.IO;

namespace Archiva.Infrastructure.Analysis;

public sealed class AzureDocumentTextExtractor : IDocumentTextExtractor
{
    private const int WordCharactersPerBillableUnit = 3_000;
    private readonly DocumentIntelligenceClient? _client;

    public AzureDocumentTextExtractor(IConfiguration configuration)
    {
        var endpoint = configuration["DocumentIntelligence:Endpoint"];
        if (!string.IsNullOrWhiteSpace(endpoint))
            _client = new DocumentIntelligenceClient(
                new Uri(endpoint),
                new DefaultAzureCredential()
            );
    }

    public DocumentAnalysisEstimate Estimate(byte[] content, string fileType)
    {
        try
        {
            return fileType.ToUpperInvariant() switch
            {
                "PDF" => EstimatePdf(content),
                "DOCX" => EstimateWord(content),
                "XLSX" => EstimateWorkbook(content),
                "TXT" => new DocumentAnalysisEstimate(0, DecodeText(content).Length),
                _ => throw new InvalidDataException("Unsupported document format."),
            };
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new InvalidDataException("The document could not be read.", exception);
        }
    }

    public async Task<string> ExtractAsync(
        byte[] content,
        string fileType,
        CancellationToken cancellationToken = default
    )
    {
        if (fileType.Equals("TXT", StringComparison.OrdinalIgnoreCase))
            return DecodeText(content);

        if (_client is null)
            throw new InvalidOperationException("Document Intelligence is not configured.");

        try
        {
            var operation = await _client.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                "prebuilt-read",
                BinaryData.FromBytes(content),
                cancellationToken: cancellationToken
            );

            return operation.Value.Content;
        }
        catch (RequestFailedException exception)
        {
            var code = exception.Status switch
            {
                429 => "document_intelligence_throttled",
                401 or 403 => "document_intelligence_access_denied",
                _ => "document_intelligence_failed",
            };
            throw new DocumentAnalysisProviderException(
                code,
                exception.ErrorCode ?? exception.Status.ToString(),
                exception.Status == 408 || exception.Status == 429 || exception.Status >= 500,
                exception
            );
        }
    }

    private static DocumentAnalysisEstimate EstimatePdf(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        return new DocumentAnalysisEstimate(document.PageCount, 0);
    }

    private static DocumentAnalysisEstimate EstimateWord(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var document = WordprocessingDocument.Open(stream, false);
        var body = document.MainDocumentPart?.Document?.Body;
        var characters = body is null
            ? 0
            : string.Concat(
                body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>()
                    .Select(text => text.Text)
            ).Length;
        var units =
            characters == 0
                ? 0
                : (int)Math.Ceiling(characters / (double)WordCharactersPerBillableUnit);
        return new DocumentAnalysisEstimate(units, characters);
    }

    private static DocumentAnalysisEstimate EstimateWorkbook(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbookPart =
            document.WorkbookPart ?? throw new InvalidDataException("Invalid workbook.");
        var sheetCount = workbookPart.WorksheetParts.Count();
        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        var characters = workbookPart
            .WorksheetParts.SelectMany(part =>
                part.Worksheet?.Descendants<DocumentFormat.OpenXml.Spreadsheet.Cell>() ?? []
            )
            .Sum(cell => GetCellText(cell, sharedStrings).Length);
        return new DocumentAnalysisEstimate(sheetCount, characters);
    }

    private static string GetCellText(
        DocumentFormat.OpenXml.Spreadsheet.Cell cell,
        DocumentFormat.OpenXml.Spreadsheet.SharedStringTable? sharedStrings
    )
    {
        if (
            cell.DataType?.Value == DocumentFormat.OpenXml.Spreadsheet.CellValues.SharedString
            && int.TryParse(cell.CellValue?.Text, out var sharedStringIndex)
            && sharedStrings is not null
        )
        {
            return sharedStrings.ElementAtOrDefault(sharedStringIndex)?.InnerText ?? string.Empty;
        }

        return cell.InlineString?.InnerText ?? cell.CellValue?.Text ?? string.Empty;
    }

    private static string DecodeText(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true
        );
        return reader.ReadToEnd();
    }
}
