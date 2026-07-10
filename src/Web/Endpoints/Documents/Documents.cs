using Archiva.Application.Documents.Commands.UploadDocument;
using Archiva.Application.Documents.Dtos;
using Archiva.Application.Documents.Queries.GetDocuments;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Archiva.Web.Endpoints.Documents;

public class Documents : IEndpointGroup
{
    public static string? RoutePrefix => "api/meetings/{meetingId}/documents";

    private static readonly string[] AllowedExtensions = [".pdf", ".docx", ".xlsx", ".txt"];
    private const long MaxFileSizeInBytes = 10 * 1024 * 1024; // 10MB

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.RequireAuthorization();
        groupBuilder.DisableAntiforgery();
        groupBuilder.MapGet(GetDocumentsHandler, "");
        groupBuilder.MapPost(UploadDocumentHandler, "");
    }

    [EndpointSummary("Get documents for a meeting")]
    [EndpointDescription(
        "Returns all documents uploaded to the specified meeting, "
            + "ordered by upload date descending."
    )]
    public static async Task<Ok<List<DocumentDto>>> GetDocumentsHandler(
        ISender sender,
        int meetingId
    )
    {
        var result = await sender.Send(new GetDocumentQuery { MeetingId = meetingId });
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Upload a document to a meeting")]
    [EndpointDescription(
        "Uploads a file to Azure Blob Storage and creates a document record "
            + "linked to the specified meeting. "
            + "Accepted formats: PDF, DOCX, XLSX, TXT. Maximum size: 10 MB."
    )]
    public static async Task<Results<Ok<DocumentDto>, ValidationProblem>> UploadDocumentHandler(
        ISender sender,
        int meetingId,
        IFormFile file,
        string? description
    )
    {
        // Validate File
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (file.Length == 0)
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["file"] = ["The uploaded file cannot be empty"],
                }
            );

        if (file.Length > MaxFileSizeInBytes)
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["file"] = ["File size must not exceed 10 MB."] }
            );

        if (!AllowedExtensions.Contains(extension))
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["file"] = ["Only PDF, DOCX, XLSX and TXT files are allowed."],
                }
            );

        await using var stream = file.OpenReadStream();

        var result = await sender.Send(
            new UploadDocumentCommand
            {
                MeetingId = meetingId,
                FileStream = stream,
                FileName = file.FileName,
                ContentType = file.ContentType,
                FileSizeInBytes = file.Length,
                Description = description,
            }
        );

        return TypedResults.Ok(result);
    }
}
