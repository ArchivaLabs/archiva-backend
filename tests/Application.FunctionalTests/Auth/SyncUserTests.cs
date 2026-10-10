using System.Net;
using System.Net.Http.Json;
using Archiva.Application.Auth.Command.SyncUser;
using Archiva.Domain.Entities;
using Archiva.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Archiva.Application.FunctionalTests.Auth;

public sealed class SyncUserTests : TestBase
{
    [Test]
    public async Task Anonymous_sync_is_rejected()
    {
        using var client = TestApp.AnonymousClient();

        var response = await client.PostAsync("/api/auth/sync", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Existing_member_is_resolved_by_entra_object_id()
    {
        using var client = TestApp.ClientFor(TestSeed.FirstAdmin);

        var response = await client.PostAsync("/api/auth/sync", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<SyncUserResult>();
        result.ShouldNotBeNull();
        result.Status.ShouldBe("existing");
        result.UserId.ShouldBe(TestSeed.FirstAdmin.Id);
        result.OrganizationId.ShouldBe(TestApp.Seed.FirstOrganizationId);
        result.Role.ShouldBe(nameof(UserRole.Admin));
    }

    [Test]
    public async Task Matching_email_with_different_object_id_does_not_grant_membership()
    {
        var separateIdentity = new TestIdentity(
            "55555555-5555-5555-5555-555555555555",
            "Separate User",
            TestSeed.FirstAdmin.Email
        );
        using var client = TestApp.ClientFor(separateIdentity);

        var response = await client.PostAsJsonAsync(
            "/api/auth/sync",
            new { UserId = TestSeed.FirstAdmin.Id, Email = TestSeed.FirstAdmin.Email }
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<SyncUserResult>();
        result.ShouldNotBeNull();
        result.Status.ShouldBe("new");
        result.UserId.ShouldBe(separateIdentity.Id);
        result.OrganizationId.ShouldBeNull();
    }

    [Test]
    public async Task Valid_invitation_binds_email_to_authenticated_object_id_once()
    {
        var invited = new TestIdentity(
            "66666666-6666-6666-6666-666666666666",
            "Invited User",
            "invitee@example.invalid"
        );
        await TestApp.WithDbContextAsync(async db =>
        {
            db.UserInvitations.Add(
                new UserInvitation
                {
                    Email = invited.Email,
                    OrganizationId = TestApp.Seed.FirstOrganizationId,
                    Role = UserRole.User,
                    ExpiresAt = DateTime.UtcNow.AddDays(1),
                }
            );
            await db.SaveChangesAsync();
        });
        using var client = TestApp.ClientFor(invited);

        var firstResponse = await client.PostAsync("/api/auth/sync", null);
        var secondResponse = await client.PostAsync("/api/auth/sync", null);

        firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        secondResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await firstResponse.Content.ReadFromJsonAsync<SyncUserResult>())?.Status.ShouldBe(
            "invited"
        );
        (await secondResponse.Content.ReadFromJsonAsync<SyncUserResult>())?.Status.ShouldBe(
            "existing"
        );
        var membershipCount = await TestApp.WithDbContextAsync(db =>
            db.OrganizationUsers.CountAsync(member => member.UserId == invited.Id)
        );
        membershipCount.ShouldBe(1);
        var invitationAccepted = await TestApp.WithDbContextAsync(db =>
            db.UserInvitations.SingleAsync(invitation => invitation.Email == invited.Email)
        );
        invitationAccepted.IsAccepted.ShouldBeTrue();
    }

    [Test]
    public async Task Expired_invitation_does_not_create_membership()
    {
        await TestApp.WithDbContextAsync(async db =>
        {
            db.UserInvitations.Add(
                new UserInvitation
                {
                    Email = TestSeed.Outsider.Email,
                    OrganizationId = TestApp.Seed.FirstOrganizationId,
                    ExpiresAt = DateTime.UtcNow.AddDays(-1),
                }
            );
            await db.SaveChangesAsync();
        });
        using var client = TestApp.ClientFor(TestSeed.Outsider);

        var response = await client.PostAsync("/api/auth/sync", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<SyncUserResult>())?.Status.ShouldBe("new");
        var membershipCount = await TestApp.WithDbContextAsync(db =>
            db.OrganizationUsers.CountAsync(member => member.UserId == TestSeed.Outsider.Id)
        );
        membershipCount.ShouldBe(0);
    }
}
