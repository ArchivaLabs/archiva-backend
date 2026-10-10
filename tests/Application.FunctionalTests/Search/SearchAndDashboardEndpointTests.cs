using System.Net;
using System.Net.Http.Json;
using Archiva.Application.Dashboard.Queries.GetDashboardStats;
using Archiva.Application.Search.Dtos;
using Archiva.Application.Search.Queries;
using Archiva.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Archiva.Application.FunctionalTests.Search;

public class SearchAndDashboardEndpointTests : TestBase
{
    [Test]
    public async Task SearchFiltersAndDashboardCountsAreScopedToTheSignedInOrganization()
    {
        await SeedSearchDataAsync();
        using var first = TestApp.ClientFor(TestSeed.FirstAdmin);
        using var second = TestApp.ClientFor(TestSeed.SecondAdmin);

        var firstSearch = await first.PostAsJsonAsync(
            "/api/search",
            new SearchDocumentsQuery { SearchTerm = "", PageSize = 10 }
        );
        firstSearch.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstPage = await firstSearch.Content.ReadFromJsonAsync<SearchResultsPageDto>();
        firstPage.ShouldNotBeNull();
        firstPage.TotalCount.ShouldBe(2);
        firstPage
            .Results.Select(result => result.Title)
            .ShouldContain(title => title.Contains("First", StringComparison.Ordinal));
        firstPage.Results.ShouldAllBe(result => !result.Title.Contains("Second"));

        var firstFilters = await first.GetFromJsonAsync<SearchFilterOptionsDto>(
            "/api/search/filter-options"
        );
        firstFilters.ShouldNotBeNull();
        firstFilters.Tags.ShouldBe(["Audit"]);

        var firstStats = await first.GetFromJsonAsync<GetDashboardStatsResult>(
            "/api/dashboard/stats"
        );
        firstStats.ShouldNotBeNull();
        firstStats.MeetingCount.ShouldBe(1);
        firstStats.DocumentCount.ShouldBe(1);
        firstStats.MeetingsAddedThisWeek.ShouldBe(1);

        var secondStats = await second.GetFromJsonAsync<GetDashboardStatsResult>(
            "/api/dashboard/stats"
        );
        secondStats.ShouldNotBeNull();
        secondStats.MeetingCount.ShouldBe(1);
        secondStats.DocumentCount.ShouldBe(1);
    }

    [Test]
    public async Task InvalidSearchAndMissingMembershipReturnProblemResponses()
    {
        using var member = TestApp.ClientFor(TestSeed.FirstMember);
        using var outsider = TestApp.ClientFor(TestSeed.Outsider);

        var invalid = await member.PostAsJsonAsync(
            "/api/search",
            new SearchDocumentsQuery { Page = 0 }
        );
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        invalid.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");

        (
            await outsider.PostAsJsonAsync("/api/search", new SearchDocumentsQuery())
        ).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await outsider.GetAsync("/api/search/filter-options")).StatusCode.ShouldBe(
            HttpStatusCode.Unauthorized
        );
        (await outsider.GetAsync("/api/dashboard/stats")).StatusCode.ShouldBe(
            HttpStatusCode.Unauthorized
        );
    }

    private static async Task SeedSearchDataAsync()
    {
        await TestApp.WithDbContextAsync(async db =>
        {
            var firstMeeting = new Meeting
            {
                OrganizationId = TestApp.Seed.FirstOrganizationId,
                Title = "First board meeting",
                Description = "First organisation decisions",
                MeetingDate = DateTime.UtcNow.Date,
                MeetingTime = new TimeSpan(11, 0, 0),
                CreatedById = TestSeed.FirstAdmin.Id,
            };
            var secondMeeting = new Meeting
            {
                OrganizationId = TestApp.Seed.SecondOrganizationId,
                Title = "Second board meeting",
                Description = "Second organisation decisions",
                MeetingDate = DateTime.UtcNow.Date,
                MeetingTime = new TimeSpan(12, 0, 0),
                CreatedById = TestSeed.SecondAdmin.Id,
            };
            db.Meetings.AddRange(firstMeeting, secondMeeting);
            await db.SaveChangesAsync();

            var tag = new Tag { Name = "Audit", OrganizationId = TestApp.Seed.FirstOrganizationId };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();
            db.MeetingTags.Add(new MeetingTag { MeetingId = firstMeeting.Id, TagId = tag.Id });
            db.Documents.AddRange(
                new Document
                {
                    OrganizationId = TestApp.Seed.FirstOrganizationId,
                    MeetingId = firstMeeting.Id,
                    FileName = "First notes.txt",
                    FileType = "TXT",
                    BlobName = "first/notes.txt",
                },
                new Document
                {
                    OrganizationId = TestApp.Seed.SecondOrganizationId,
                    MeetingId = secondMeeting.Id,
                    FileName = "Second notes.txt",
                    FileType = "TXT",
                    BlobName = "second/notes.txt",
                }
            );
            await db.SaveChangesAsync();
        });
    }
}
