using System.Net;
using System.Net.Http.Json;
using Archiva.Application.Auth.Command.SyncUser;
using Archiva.Application.Common.Interfaces;
using Archiva.Application.Organizations.Commands.CreateOrganization;
using Archiva.Domain.Entities;
using Archiva.Domain.Enums;
using Archiva.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Archiva.Application.FunctionalTests.Auth;

public sealed class GoogleMembershipSyncTests : TestBase
{
    private static readonly GoogleUser Google = new("google:12345678", "guest@gmail.com");

    [Test]
    public async Task Normalized_invitation_returns_stored_admin_role_and_repeated_sync_is_existing()
    {
        var invitationId = await Invite("  GUEST@GMAIL.COM  ", TestApp.Seed.SecondOrganizationId);

        var first = await SyncGoogle();
        var second = await SyncGoogle();

        first.Status.ShouldBe("invited");
        first.UserId.ShouldBe(Google.Id);
        first.OrganizationId.ShouldBe(TestApp.Seed.SecondOrganizationId);
        first.Role.ShouldBe(nameof(UserRole.Admin));
        second.ShouldBe(first with { Status = "existing" });
        await AssertState(invitationId, accepted: true, googleMembershipCount: 1);
    }

    [Test]
    public async Task Google_identity_with_microsoft_members_email_has_no_membership()
    {
        await TestApp.WithDbContextAsync(async db =>
        {
            var microsoft = await db.OrganizationUsers.SingleAsync(u =>
                u.UserId == TestSeed.FirstAdmin.Id
            );
            microsoft.Email = Google.Email;
            await db.SaveChangesAsync();
        });

        var result = await SyncGoogle();

        result.Status.ShouldBe("new");
        result.UserId.ShouldBe(Google.Id);
        result.OrganizationId.ShouldBeNull();
        result.Role.ShouldBeNull();
    }

    [Test]
    public async Task Ineligible_google_identity_leaves_invitation_pending()
    {
        var google = Google with
        {
            Email = "unverified@example.invalid",
            CanClaimInvitation = false,
        };
        var invitationId = await Invite(google.Email, TestApp.Seed.FirstOrganizationId);

        var result = await SyncGoogle(google);

        result.Status.ShouldBe("new");
        result.OrganizationId.ShouldBeNull();
        await AssertState(invitationId, accepted: false, googleMembershipCount: 0);
    }

    [Test]
    public async Task Ambiguous_normalized_invitations_remain_pending_for_both_providers()
    {
        var first = await Invite(Google.Email, TestApp.Seed.FirstOrganizationId);
        var second = await Invite(" GUEST@GMAIL.COM ", TestApp.Seed.SecondOrganizationId);

        (await SyncGoogle()).Status.ShouldBe("invitationReview");
        (await SyncMicrosoft()).Status.ShouldBe("invitationReview");

        await AssertState(first, accepted: false, googleMembershipCount: 0);
        await AssertState(second, accepted: false, googleMembershipCount: 0);
        var microsoftMembershipCount = await TestApp.WithDbContextAsync(db =>
            db.OrganizationUsers.CountAsync(user => user.UserId == TestSeed.Outsider.Id)
        );
        microsoftMembershipCount.ShouldBe(0);
    }

    [Test]
    public async Task Expired_and_claimed_invitations_do_not_make_a_current_invitation_ambiguous()
    {
        var orgId = TestApp.Seed.FirstOrganizationId;
        var expired = await Invite(Google.Email, orgId, expiresAt: DateTime.UtcNow.AddDays(-1));
        await Invite(Google.Email, orgId, accepted: true);
        var current = await Invite(Google.Email, TestApp.Seed.SecondOrganizationId);

        var result = await SyncGoogle();

        result.Status.ShouldBe("invited");
        result.OrganizationId.ShouldBe(TestApp.Seed.SecondOrganizationId);
        await AssertState(expired, accepted: false, googleMembershipCount: 1);
        await AssertState(current, accepted: true, googleMembershipCount: 1);
    }

    [Test]
    public async Task Google_and_microsoft_cannot_both_claim_the_same_invitation()
    {
        var invitation = await Invite(Google.Email, TestApp.Seed.FirstOrganizationId);
        SyncUserResult? microsoft = null;

        var google = await SyncAfterCompetitor(async () => microsoft = await SyncMicrosoft());

        microsoft.ShouldNotBeNull();
        microsoft.Status.ShouldBe("invited");
        microsoft.Role.ShouldBe(nameof(UserRole.Admin));
        google.Status.ShouldBe("new");
        google.OrganizationId.ShouldBeNull();
        await AssertState(invitation, accepted: true, googleMembershipCount: 0);
    }

    [Test]
    public async Task Concurrent_sync_for_same_identity_returns_winning_membership()
    {
        var invitation = await Invite(Google.Email, TestApp.Seed.FirstOrganizationId);
        SyncUserResult? winner = null;

        var follower = await SyncAfterCompetitor(async () => winner = await SyncGoogle());

        winner.ShouldNotBeNull();
        winner.Status.ShouldBe("invited");
        follower.ShouldBe(winner with { Status = "existing" });
        await AssertState(invitation, accepted: true, googleMembershipCount: 1);
    }

    [Test]
    public async Task Onboarding_winner_keeps_organization_and_role_and_leaves_invitation_pending()
    {
        var invitation = await Invite(
            Google.Email,
            TestApp.Seed.FirstOrganizationId,
            role: UserRole.User
        );
        CreateOrganizationResult? created = null;

        var synced = await SyncAfterCompetitor(async () =>
        {
            created = await TestApp.WithDbContextAsync(db =>
                new CreateOrganizationCommandHandler(
                    db,
                    Google,
                    NullLogger<CreateOrganizationCommandHandler>.Instance
                ).Handle(new CreateOrganizationCommand { Name = "New workspace" }, default)
            );
        });

        created.ShouldNotBeNull();
        synced.Status.ShouldBe("existing");
        synced.OrganizationId.ShouldBe(created.OrganizationId);
        synced.OrganizationName.ShouldBe(created.OrganizationName);
        synced.Role.ShouldBe(nameof(UserRole.Admin));
        await AssertState(invitation, accepted: false, googleMembershipCount: 1);
        (await SyncGoogle(Google with { CanClaimInvitation = false })).ShouldBe(synced);
    }

    private static Task<int> Invite(
        string email,
        int organizationId,
        UserRole role = UserRole.Admin,
        DateTime? expiresAt = null,
        bool accepted = false
    ) =>
        TestApp.WithDbContextAsync(async db =>
        {
            var invitation = new UserInvitation
            {
                Email = email,
                OrganizationId = organizationId,
                Role = role,
                ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(1),
                IsAccepted = accepted,
            };
            db.UserInvitations.Add(invitation);
            await db.SaveChangesAsync();
            return invitation.Id;
        });

    private static Task AssertState(int invitationId, bool accepted, int googleMembershipCount) =>
        TestApp.WithDbContextAsync(async db =>
        {
            var invitation = await db.UserInvitations.SingleAsync(i => i.Id == invitationId);
            invitation.IsAccepted.ShouldBe(accepted);
            var membershipCount = await db.OrganizationUsers.CountAsync(u => u.UserId == Google.Id);
            membershipCount.ShouldBe(googleMembershipCount);
        });

    private static Task<SyncUserResult> SyncGoogle(GoogleUser? user = null) =>
        TestApp.WithDbContextAsync(db => CreateHandler(db, user ?? Google).Handle(new(), default));

    private static async Task<SyncUserResult> SyncMicrosoft()
    {
        using var client = TestApp.ClientFor(TestSeed.Outsider with { Email = Google.Email });
        using var response = await client.PostAsync("/api/auth/sync", null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<SyncUserResult>();
        result.ShouldNotBeNull();
        return result;
    }

    private static async Task<SyncUserResult> SyncAfterCompetitor(Func<Task> competitor)
    {
        var gate = new ClaimSaveGate();
        var connection = await TestApp.WithDbContextAsync(db =>
            Task.FromResult(db.Database.GetConnectionString())
        );
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection)
            .AddInterceptors(gate)
            .Options;
        await using var db = new ApplicationDbContext(options);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var pending = CreateHandler(db, Google).Handle(new(), timeout.Token);
        try
        {
            await gate.Ready.Task.WaitAsync(timeout.Token);
            await competitor();
        }
        finally
        {
            gate.Release.SetResult();
            await pending;
        }
        return await pending;
    }

    private static SyncUserCommandHandler CreateHandler(ApplicationDbContext db, GoogleUser user) =>
        new(db, user, NullLogger<SyncUserCommandHandler>.Instance);

    private sealed record GoogleUser(string Id, string Email, bool CanClaimInvitation = true)
        : IUser
    {
        public string Name => "Google user";
        public string AuthenticationProvider => "Google";
        public string? OrganizationId => null;
        public List<string>? Role => null;
    }

    private sealed class ClaimSaveGate : SaveChangesInterceptor
    {
        public TaskCompletionSource Ready { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            Ready.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }
}
