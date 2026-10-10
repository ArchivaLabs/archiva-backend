using System.Net;
using System.Net.Http.Json;
using Archiva.Application.Meetings.Commands.CreateMeeting;
using Archiva.Application.Meetings.Commands.UpdateMeeting;
using Archiva.Application.Meetings.Queries;
using Archiva.Application.Meetings.Queries.GetMeetings;
using Microsoft.EntityFrameworkCore;

namespace Archiva.Application.FunctionalTests.Meetings;

public sealed class MeetingEndpointTests : TestBase
{
    [Test]
    public async Task Create_deduplicates_tags_and_lists_only_current_organization_meetings()
    {
        using var firstClient = TestApp.ClientFor(TestSeed.FirstAdmin);
        var command = NewMeeting("First meeting") with
        {
            Tags = [" Governance ", "governance", "Legal"],
        };

        var createdResponse = await firstClient.PostAsJsonAsync("/api/meetings", command);

        createdResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var created = await createdResponse.Content.ReadFromJsonAsync<CreateMeetingResult>();
        created.ShouldNotBeNull();
        created.OrganizationId.ShouldBe(TestApp.Seed.FirstOrganizationId);
        created.Tags.OrderBy(tag => tag).ShouldBe(["Governance", "Legal"]);

        await TestApp.SendAsync(NewMeeting("Second meeting"), TestSeed.SecondAdmin);
        var listedResponse = await firstClient.GetAsync("/api/meetings/");
        listedResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var listed = await listedResponse.Content.ReadFromJsonAsync<GetMeetingsResult>();
        listed.ShouldNotBeNull();
        listed.TotalCount.ShouldBe(1);
        listed.Meetings.Single().Id.ShouldBe(created.Id);

        var storedTagCount = await TestApp.WithDbContextAsync(db =>
            db.MeetingTags.CountAsync(tag => tag.MeetingId == created.Id)
        );
        storedTagCount.ShouldBe(2);
    }

    [Test]
    public async Task Meeting_list_paginates_in_descending_meeting_date_order()
    {
        await TestApp.SendAsync(NewMeeting("Earlier", 2), TestSeed.FirstAdmin);
        await TestApp.SendAsync(NewMeeting("Middle", 3), TestSeed.FirstAdmin);
        await TestApp.SendAsync(NewMeeting("Later", 4), TestSeed.FirstAdmin);
        using var client = TestApp.ClientFor(TestSeed.FirstAdmin);

        var response = await client.GetAsync("/api/meetings/?page=1&pageSize=2");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<GetMeetingsResult>();
        result.ShouldNotBeNull();
        result.TotalCount.ShouldBe(3);
        result.TotalPages.ShouldBe(2);
        result.HasNextPage.ShouldBeTrue();
        result.Meetings.Select(meeting => meeting.Title).ShouldBe(["Later", "Middle"]);
    }

    [Test]
    public async Task Get_detail_returns_owner_permissions_and_tags()
    {
        var created = await TestApp.SendAsync(
            NewMeeting("Owned meeting") with
            {
                Tags = ["Legal"],
            },
            TestSeed.FirstMember
        );
        using var ownerClient = TestApp.ClientFor(TestSeed.FirstMember);
        using var adminClient = TestApp.ClientFor(TestSeed.FirstAdmin);

        var ownerResponse = await ownerClient.GetAsync($"/api/meetings/{created.Id}");
        var adminResponse = await adminClient.GetAsync($"/api/meetings/{created.Id}");

        ownerResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        adminResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var ownerDetail = await ownerResponse.Content.ReadFromJsonAsync<MeetingDetailDto>();
        var adminDetail = await adminResponse.Content.ReadFromJsonAsync<MeetingDetailDto>();
        ownerDetail.ShouldNotBeNull();
        adminDetail.ShouldNotBeNull();
        ownerDetail.CreatedById.ShouldBe(TestSeed.FirstMember.Id);
        ownerDetail.CanDelete.ShouldBeTrue();
        adminDetail.CanDelete.ShouldBeTrue();
        ownerDetail.Tags.ShouldContain("Legal");
    }

    [Test]
    public async Task Foreign_organization_meeting_is_hidden_for_get_update_and_delete()
    {
        var created = await TestApp.SendAsync(NewMeeting("Private meeting"), TestSeed.FirstAdmin);
        using var client = TestApp.ClientFor(TestSeed.SecondAdmin);

        var get = await client.GetAsync($"/api/meetings/{created.Id}");
        var update = await client.PutAsJsonAsync(
            $"/api/meetings/{created.Id}",
            NewUpdate("Changed", created.Id)
        );
        var delete = await client.DeleteAsync($"/api/meetings/{created.Id}");

        get.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        update.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        delete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var persistedTitle = await TestApp.WithDbContextAsync(db =>
            db.Meetings.Where(meeting => meeting.Id == created.Id)
                .Select(meeting => meeting.Title)
                .SingleAsync()
        );
        persistedTitle.ShouldBe("Private meeting");
    }

    [Test]
    public async Task Authenticated_nonmember_cannot_use_meeting_endpoints()
    {
        var created = await TestApp.SendAsync(NewMeeting("Private meeting"), TestSeed.FirstAdmin);
        using var client = TestApp.ClientFor(TestSeed.Outsider);

        var create = await client.PostAsJsonAsync("/api/meetings", NewMeeting("Unauthorized"));
        var list = await client.GetAsync("/api/meetings/");
        var get = await client.GetAsync($"/api/meetings/{created.Id}");
        var update = await client.PutAsJsonAsync(
            $"/api/meetings/{created.Id}",
            NewUpdate("Changed", created.Id)
        );
        var delete = await client.DeleteAsync($"/api/meetings/{created.Id}");

        create.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        list.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        get.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        update.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        delete.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Update_uses_route_id_and_replaces_meeting_tags()
    {
        var created = await TestApp.SendAsync(
            NewMeeting("Original") with
            {
                Tags = ["Finance", "Legal"],
            },
            TestSeed.FirstAdmin
        );
        using var client = TestApp.ClientFor(TestSeed.FirstAdmin);

        var response = await client.PutAsJsonAsync(
            $"/api/meetings/{created.Id}",
            NewUpdate("Updated", int.MaxValue) with
            {
                Tags = ["Legal", "Research"],
            }
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await response.Content.ReadFromJsonAsync<MeetingDetailDto>();
        detail.ShouldNotBeNull();
        detail.Id.ShouldBe(created.Id);
        detail.Title.ShouldBe("Updated");
        detail.Tags.OrderBy(tag => tag).ShouldBe(["Legal", "Research"]);
        var persistedTags = await TestApp.WithDbContextAsync(db =>
            db.MeetingTags.Where(tag => tag.MeetingId == created.Id)
                .Select(tag => tag.Tag.Name)
                .ToListAsync()
        );
        persistedTags.OrderBy(tag => tag).ShouldBe(["Legal", "Research"]);
    }

    [Test]
    public async Task Member_cannot_delete_meeting_created_by_another_member_but_admin_can()
    {
        var created = await TestApp.SendAsync(NewMeeting("Admin meeting"), TestSeed.FirstAdmin);
        using var memberClient = TestApp.ClientFor(TestSeed.FirstMember);
        using var adminClient = TestApp.ClientFor(TestSeed.FirstAdmin);

        var denied = await memberClient.DeleteAsync($"/api/meetings/{created.Id}");
        var deleted = await adminClient.DeleteAsync($"/api/meetings/{created.Id}");

        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var exists = await TestApp.WithDbContextAsync(db =>
            db.Meetings.AnyAsync(meeting => meeting.Id == created.Id)
        );
        exists.ShouldBeFalse();
    }

    [Test]
    public async Task Member_can_delete_own_meeting()
    {
        var created = await TestApp.SendAsync(NewMeeting("Member meeting"), TestSeed.FirstMember);
        using var client = TestApp.ClientFor(TestSeed.FirstMember);

        var response = await client.DeleteAsync($"/api/meetings/{created.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Past_meeting_is_rejected_without_persisting_it()
    {
        using var client = TestApp.ClientFor(TestSeed.FirstAdmin);

        var response = await client.PostAsJsonAsync("/api/meetings", NewMeeting("Past", -2));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var meetingCount = await TestApp.WithDbContextAsync(db => db.Meetings.CountAsync());
        meetingCount.ShouldBe(0);
    }

    private static CreateMeetingCommand NewMeeting(string title, int daysFromToday = 2) =>
        new()
        {
            Title = title,
            MeetingDate = DateTime.UtcNow.Date.AddDays(daysFromToday),
            MeetingTime = TimeSpan.FromHours(10),
            UtcOffsetMinutes = 0,
        };

    private static UpdateMeetingCommand NewUpdate(string title, int id) =>
        new()
        {
            Id = id,
            Title = title,
            MeetingDate = DateTime.UtcNow.Date.AddDays(3),
            MeetingTime = TimeSpan.FromHours(11),
        };
}
