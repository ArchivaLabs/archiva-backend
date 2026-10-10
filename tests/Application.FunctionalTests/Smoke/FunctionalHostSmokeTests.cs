using System.Net;
using Microsoft.EntityFrameworkCore;

namespace Archiva.Application.FunctionalTests.Smoke;

public sealed class FunctionalHostSmokeTests : TestBase
{
    [Test]
    public async Task Authenticated_request_uses_seeded_membership_and_real_database()
    {
        using var client = TestApp.ClientFor(TestSeed.FirstAdmin);

        var response = await client.GetAsync("/api/dashboard/stats");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var membershipCount = await TestApp.WithDbContextAsync(db =>
            db.OrganizationUsers.CountAsync()
        );
        membershipCount.ShouldBe(3);
    }

    [Test]
    public async Task Anonymous_request_is_rejected()
    {
        using var client = TestApp.AnonymousClient();

        var response = await client.GetAsync("/api/dashboard/stats");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
