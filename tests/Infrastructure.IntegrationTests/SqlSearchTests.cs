using Archiva.Application.Search.Dtos;
using Archiva.Application.Search.Queries;
using Archiva.Domain.Entities;
using Archiva.Infrastructure.Data;
using Archiva.Infrastructure.Search;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Archiva.Infrastructure.IntegrationTests;

public sealed class SqlSearchTests : IntegrationTestBase
{
    [Test]
    public async Task Search_keeps_meetings_documents_and_tags_inside_requested_organization()
    {
        var (firstMeeting, firstDocument, _) = await SeedSearchDataAsync();

        var result = await SearchAsync(
            new SearchDocumentsQuery(),
            IntegrationHost.FirstOrganizationId
        );

        result.TotalCount.ShouldBe(2);
        result
            .Results.Any(item => item.Type == "meeting" && item.Id == firstMeeting)
            .ShouldBeTrue();
        result
            .Results.Any(item => item.Type == "document" && item.Id == firstDocument)
            .ShouldBeTrue();
        result.Results.All(item => item.Tags.SequenceEqual(["Finance"])).ShouldBeTrue();
    }

    [Test]
    public async Task Search_filters_by_tag_and_inclusive_meeting_date()
    {
        await SeedSearchDataAsync();

        var matching = await SearchAsync(
            new SearchDocumentsQuery
            {
                Tags = ["Finance"],
                DateFrom = new DateOnly(2026, 10, 10),
                DateTo = new DateOnly(2026, 10, 10),
            },
            IntegrationHost.FirstOrganizationId
        );
        var excluded = await SearchAsync(
            new SearchDocumentsQuery { Tags = ["Finance"], DateFrom = new DateOnly(2026, 10, 11) },
            IntegrationHost.FirstOrganizationId
        );

        matching.TotalCount.ShouldBe(2);
        excluded.TotalCount.ShouldBe(0);
    }

    [Test]
    public async Task Search_paginates_in_stable_date_order()
    {
        await IntegrationHost.WithDbAsync(async db =>
        {
            db.Meetings.AddRange(
                Meeting("First", 1, IntegrationHost.FirstOrganizationId),
                Meeting("Second", 2, IntegrationHost.FirstOrganizationId),
                Meeting("Third", 3, IntegrationHost.FirstOrganizationId)
            );
            await db.SaveChangesAsync();
        });

        var first = await SearchAsync(
            new SearchDocumentsQuery
            {
                IncludeDocuments = false,
                SortBy = "date_asc",
                Page = 1,
                PageSize = 1,
            },
            IntegrationHost.FirstOrganizationId
        );
        var second = await SearchAsync(
            new SearchDocumentsQuery
            {
                IncludeDocuments = false,
                SortBy = "date_asc",
                Page = 2,
                PageSize = 1,
            },
            IntegrationHost.FirstOrganizationId
        );

        first.TotalCount.ShouldBe(3);
        second.TotalCount.ShouldBe(3);
        first.Results.Single().Title.ShouldBe("First");
        second.Results.Single().Title.ShouldBe("Second");
    }

    [Test]
    public async Task Search_term_uses_available_full_text_or_like_path_without_duplicate_results()
    {
        var (meetingId, documentId, _) = await SeedSearchDataAsync();
        var fullText = await IntegrationHost.WithDbAsync(async db =>
        {
            var connection = (SqlConnection)db.Database.GetDbConnection();
            await connection.OpenAsync();
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT CASE WHEN FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') = 1 AND EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'dbo.Meetings')) AND EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'dbo.Documents')) THEN 1 ELSE 0 END";
                return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
            }
            finally
            {
                await connection.CloseAsync();
            }
        });

        var deadline = DateTime.UtcNow.AddSeconds(fullText ? 30 : 1);
        SearchResultsPageDto? result = null;
        do
        {
            result = await SearchAsync(
                new SearchDocumentsQuery { SearchTerm = "Cedarbridge" },
                IntegrationHost.FirstOrganizationId
            );
            if (result.TotalCount == 2)
                break;
            await Task.Delay(250);
        } while (DateTime.UtcNow < deadline);

        result!.TotalCount.ShouldBe(
            2,
            fullText
                ? "Full-text population should include both rows."
                : "LIKE fallback should include both rows."
        );
        result.Results.Any(item => item.Type == "meeting" && item.Id == meetingId).ShouldBeTrue();
        result.Results.Any(item => item.Type == "document" && item.Id == documentId).ShouldBeTrue();
    }

    private static async Task<(
        int MeetingId,
        int DocumentId,
        int ForeignMeetingId
    )> SeedSearchDataAsync() =>
        await IntegrationHost.WithDbAsync(async db =>
        {
            var tag = new Tag
            {
                Name = "Finance",
                OrganizationId = IntegrationHost.FirstOrganizationId,
            };
            var first = Meeting("Cedarbridge Planning", 10, IntegrationHost.FirstOrganizationId);
            first.Description = "Cedarbridge budget review";
            var foreign = Meeting("Cedarbridge Private", 10, IntegrationHost.SecondOrganizationId);
            db.AddRange(tag, first, foreign);
            await db.SaveChangesAsync();
            db.MeetingTags.Add(new MeetingTag { MeetingId = first.Id, TagId = tag.Id });
            var document = new Document
            {
                FileName = "Cedarbridge minutes.pdf",
                FileType = ".pdf",
                BlobName = "search-test",
                BlobUrl = "search-test",
                FileSizeInBytes = 1,
                ExtractedText = "Cedarbridge decisions",
                MeetingId = first.Id,
                OrganizationId = IntegrationHost.FirstOrganizationId,
            };
            db.Documents.Add(document);
            await db.SaveChangesAsync();
            return (first.Id, document.Id, foreign.Id);
        });

    private static Meeting Meeting(string title, int day, int organizationId) =>
        new()
        {
            Title = title,
            MeetingDate = new DateTime(2026, 10, day, 10, 0, 0, DateTimeKind.Utc),
            MeetingTime = TimeSpan.FromHours(10),
            OrganizationId = organizationId,
        };

    private static async Task<Archiva.Application.Search.Dtos.SearchResultsPageDto> SearchAsync(
        SearchDocumentsQuery request,
        int organizationId
    )
    {
        await using var scope = IntegrationHost.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<SqlDocumentSearchService>>();
        return await new SqlDocumentSearchService(db, logger).SearchAsync(request, organizationId);
    }
}
