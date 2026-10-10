using System.Net;
using System.Net.Http.Json;
using Archiva.Application.Organizations.Commands.CreateOrganization;
using Archiva.Domain.Entities;
using Archiva.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Archiva.Application.FunctionalTests.Organizations;

public sealed class CreateOrganizationConcurrencyTests : TestBase
{
    [Test]
    public async Task Concurrent_onboarding_creates_one_organization_and_one_membership()
    {
        using var firstClient = TestApp.ClientFor(TestSeed.Outsider);
        using var secondClient = TestApp.ClientFor(TestSeed.Outsider);

        var firstRequest = firstClient.PostAsJsonAsync(
            "/api/organizations",
            new CreateOrganizationCommand { Name = "Concurrent Organization A" }
        );
        var secondRequest = secondClient.PostAsJsonAsync(
            "/api/organizations",
            new CreateOrganizationCommand { Name = "Concurrent Organization B" }
        );

        var responses = await Task.WhenAll(firstRequest, secondRequest);
        using var firstResponse = responses[0];
        using var secondResponse = responses[1];

        responses.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.BadRequest).ShouldBe(1);

        var created = await TestApp.WithDbContextAsync(async db =>
        {
            var membership = await db
                .OrganizationUsers.AsNoTracking()
                .SingleAsync(user => user.UserId == TestSeed.Outsider.Id);
            return new
            {
                OrganizationCount = await db.Organizations.CountAsync(),
                MembershipCount = await db.OrganizationUsers.CountAsync(user =>
                    user.UserId == TestSeed.Outsider.Id
                ),
                TagCount = await db.Tags.CountAsync(tag =>
                    tag.OrganizationId == membership.OrganizationId
                ),
            };
        });

        created.OrganizationCount.ShouldBe(3);
        created.MembershipCount.ShouldBe(1);
        created.TagCount.ShouldBe(19);
    }

    [Test]
    public async Task User_id_unique_index_rejects_second_organization_membership()
    {
        var userId = TestSeed.FirstAdmin.Id;

        await Should.ThrowAsync<DbUpdateException>(() =>
            TestApp.WithDbContextAsync(async db =>
            {
                db.OrganizationUsers.Add(
                    new OrganizationUser
                    {
                        OrganizationId = TestApp.Seed.SecondOrganizationId,
                        UserId = userId,
                        UserName = TestSeed.FirstAdmin.Name,
                        Email = TestSeed.FirstAdmin.Email,
                        Role = UserRole.User,
                        JoinedAt = DateTime.UtcNow,
                    }
                );
                await db.SaveChangesAsync();
            })
        );

        var count = await TestApp.WithDbContextAsync(db =>
            db.OrganizationUsers.CountAsync(user => user.UserId == userId)
        );
        count.ShouldBe(1);
    }
}
