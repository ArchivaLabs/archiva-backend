using System.Net;
using System.Net.Http.Json;
using Archiva.Application.Organizations.Commands.CreateOrganization;
using Archiva.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Archiva.Application.FunctionalTests.Organizations;

public sealed class CreateOrganizationTests : TestBase
{
    [Test]
    public async Task Anonymous_creation_is_rejected()
    {
        using var client = TestApp.AnonymousClient();

        var response = await client.PostAsJsonAsync(
            "/api/organizations",
            new CreateOrganizationCommand { Name = "New Organization" }
        );

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task New_user_creates_organization_admin_membership_and_default_tags()
    {
        using var client = TestApp.ClientFor(TestSeed.Outsider);

        var response = await client.PostAsJsonAsync(
            "/api/organizations",
            new CreateOrganizationCommand { Name = "New Organization" }
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CreateOrganizationResult>();
        result.ShouldNotBeNull();
        result.OrganizationName.ShouldBe("New Organization");
        result.UserId.ShouldBe(TestSeed.Outsider.Id);
        result.Role.ShouldBe(nameof(UserRole.Admin));
        var persisted = await TestApp.WithDbContextAsync(async db => new
        {
            Members = await db
                .OrganizationUsers.Where(member => member.OrganizationId == result.OrganizationId)
                .ToListAsync(),
            TagCount = await db.Tags.CountAsync(tag => tag.OrganizationId == result.OrganizationId),
        });
        persisted.Members.Count.ShouldBe(1);
        persisted.Members[0].UserId.ShouldBe(TestSeed.Outsider.Id);
        persisted.Members[0].Role.ShouldBe(UserRole.Admin);
        persisted.TagCount.ShouldBe(19);
    }

    [Test]
    public async Task Existing_member_cannot_create_another_organization()
    {
        using var client = TestApp.ClientFor(TestSeed.FirstAdmin);

        var response = await client.PostAsJsonAsync(
            "/api/organizations",
            new CreateOrganizationCommand { Name = "Duplicate Organization" }
        );

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var organizationCount = await TestApp.WithDbContextAsync(db =>
            db.Organizations.CountAsync()
        );
        organizationCount.ShouldBe(2);
    }

    [Test]
    public async Task Repeated_creation_does_not_create_second_membership_or_organization()
    {
        using var client = TestApp.ClientFor(TestSeed.Outsider);
        var command = new CreateOrganizationCommand { Name = "New Organization" };

        var first = await client.PostAsJsonAsync("/api/organizations", command);
        var second = await client.PostAsJsonAsync("/api/organizations", command);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var counts = await TestApp.WithDbContextAsync(async db => new
        {
            Organizations = await db.Organizations.CountAsync(),
            Memberships = await db.OrganizationUsers.CountAsync(member =>
                member.UserId == TestSeed.Outsider.Id
            ),
        });
        counts.Organizations.ShouldBe(3);
        counts.Memberships.ShouldBe(1);
    }

    [Test]
    public async Task Invalid_name_is_rejected_without_database_changes()
    {
        using var client = TestApp.ClientFor(TestSeed.Outsider);

        var response = await client.PostAsJsonAsync(
            "/api/organizations",
            new CreateOrganizationCommand { Name = "" }
        );

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var organizationCount = await TestApp.WithDbContextAsync(db =>
            db.Organizations.CountAsync()
        );
        organizationCount.ShouldBe(2);
    }
}
