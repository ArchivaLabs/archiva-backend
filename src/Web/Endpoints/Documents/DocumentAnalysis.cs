using Archiva.Application.Documents.Commands.BackfillDocumentAnalysis;
using Archiva.Application.Documents.Commands.RetryDocumentAnalysis;
using Archiva.Application.Documents.Dtos;
using Archiva.Application.Documents.Queries.GetDocumentContent;
using Archiva.Application.Documents.Queries.GetDocumentDetail;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Archiva.Web.Endpoints.Documents;

public sealed record RetryDocumentAnalysisRequest(int? UnitLimit, int? SummaryLimit);

public sealed record BackfillDocumentAnalysisRequest(int AfterDocumentId = 0, int BatchSize = 100);

public class DocumentAnalysis : IEndpointGroup
{
    public static string? RoutePrefix => "api/documents";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.RequireAuthorization();
        groupBuilder.MapGet(GetDocumentDetailHandler, "{documentId:int}");
        groupBuilder.MapGet(GetDocumentContentHandler, "{documentId:int}/content");
        groupBuilder.MapPost(RetryDocumentAnalysisHandler, "{documentId:int}/analysis/retry");
        groupBuilder.MapPost(BackfillDocumentAnalysisHandler, "analysis/backfill");
    }

    [EndpointSummary("Preview document content")]
    [EndpointDescription(
        "Returns organisation-scoped document bytes for the authenticated previewer."
    )]
    public static async Task<FileContentHttpResult> GetDocumentContentHandler(
        ISender sender,
        int documentId,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await sender.Send(new GetDocumentContentQuery(documentId), cancellationToken);
        httpContext.Response.Headers.CacheControl = "private, no-store";
        httpContext.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.File(result.Content, result.ContentType);
    }

    [EndpointSummary("Get a document and its analysis status")]
    [EndpointDescription(
        "Returns an organisation-scoped document detail and a short-lived blob URL."
    )]
    public static async Task<Ok<DocumentDetailDto>> GetDocumentDetailHandler(
        ISender sender,
        int documentId,
        CancellationToken cancellationToken
    )
    {
        var result = await sender.Send(new GetDocumentDetailQuery(documentId), cancellationToken);
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Retry document analysis")]
    [EndpointDescription(
        "Queues a failed or deferred document for analysis. Admins can raise its processing limits."
    )]
    public static async Task<Ok<DocumentAnalysisActionResult>> RetryDocumentAnalysisHandler(
        ISender sender,
        int documentId,
        RetryDocumentAnalysisRequest request,
        CancellationToken cancellationToken
    )
    {
        var result = await sender.Send(
            new RetryDocumentAnalysisCommand(documentId, request.UnitLimit, request.SummaryLimit),
            cancellationToken
        );
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Backfill document analysis")]
    [EndpointDescription(
        "Queues one organisation-scoped batch of eligible older documents for analysis."
    )]
    public static async Task<Ok<BackfillDocumentAnalysisResult>> BackfillDocumentAnalysisHandler(
        ISender sender,
        BackfillDocumentAnalysisRequest request,
        CancellationToken cancellationToken
    )
    {
        var result = await sender.Send(
            new BackfillDocumentAnalysisCommand(request.AfterDocumentId, request.BatchSize),
            cancellationToken
        );
        return TypedResults.Ok(result);
    }
}
