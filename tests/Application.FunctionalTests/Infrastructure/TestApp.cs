using System.Security.Claims;
using Archiva.Domain.Entities;
using Archiva.Domain.Enums;
using Archiva.Infrastructure.Data;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Archiva.Application.FunctionalTests.Infrastructure;

public sealed record TestIdentity(string Id, string Name, string Email);

public sealed record TestSeed(int FirstOrganizationId, int SecondOrganizationId)
{
    public static readonly TestIdentity FirstAdmin = new(
        "11111111-1111-1111-1111-111111111111",
        "First Admin",
        "first-admin@example.invalid"
    );
    public static readonly TestIdentity FirstMember = new(
        "22222222-2222-2222-2222-222222222222",
        "First Member",
        "first-member@example.invalid"
    );
    public static readonly TestIdentity SecondAdmin = new(
        "33333333-3333-3333-3333-333333333333",
        "Second Admin",
        "second-admin@example.invalid"
    );
    public static readonly TestIdentity Outsider = new(
        "44444444-4444-4444-4444-444444444444",
        "Outsider",
        "outsider@example.invalid"
    );
}

public static class TestApp
{
    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"),
    };

    public static TestSeed Seed { get; internal set; } = null!;

    public static HttpClient ClientFor(TestIdentity identity)
    {
        var client = FunctionalTestSetup.Factory.CreateClient(ClientOptions);
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserIdHeader, identity.Id);
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserNameHeader, identity.Name);
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.EmailHeader, identity.Email);
        return client;
    }

    public static HttpClient AnonymousClient() =>
        FunctionalTestSetup.Factory.CreateClient(ClientOptions);

    public static async Task<T> WithDbContextAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        await using var scope = FunctionalTestSetup.Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    public static async Task WithDbContextAsync(Func<ApplicationDbContext, Task> action)
    {
        await WithDbContextAsync(async context =>
        {
            await action(context);
            return true;
        });
    }

    public static async Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        TestIdentity identity
    ) =>
        await RunAsAsync(
            identity,
            services => services.GetRequiredService<ISender>().Send(request)
        );

    public static async Task SendAsync(IRequest request, TestIdentity identity)
    {
        await RunAsAsync(
            identity,
            async services =>
            {
                await services.GetRequiredService<ISender>().Send(request);
                return true;
            }
        );
    }

    private static async Task<T> RunAsAsync<T>(
        TestIdentity identity,
        Func<IServiceProvider, Task<T>> action
    )
    {
        await using var scope = FunctionalTestSetup.Factory.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [
                        new Claim("oid", identity.Id),
                        new Claim(
                            "http://schemas.microsoft.com/identity/claims/objectidentifier",
                            identity.Id
                        ),
                        new Claim("name", identity.Name),
                        new Claim("preferred_username", identity.Email),
                    ],
                    TestAuthenticationHandler.SchemeName
                )
            ),
        };
        try
        {
            return await action(scope.ServiceProvider);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    internal static async Task ResetAsync()
    {
        await FunctionalTestSetup.DbResetter.ResetAsync();
        await using var scope = FunctionalTestSetup.Factory.Services.CreateAsyncScope();
        var blobs = scope.ServiceProvider.GetRequiredService<BlobServiceClient>();
        var queues = scope.ServiceProvider.GetRequiredService<QueueServiceClient>();
        await blobs.GetBlobContainerClient("documents").DeleteIfExistsAsync();
        await queues.GetQueueClient(Services.AnalysisQueueName).DeleteIfExistsAsync();

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var first = new Organization { Name = "First Organization" };
        var second = new Organization { Name = "Second Organization" };
        db.Organizations.AddRange(first, second);
        await db.SaveChangesAsync();
        db.OrganizationUsers.AddRange(
            Member(first.Id, TestSeed.FirstAdmin, UserRole.Admin),
            Member(first.Id, TestSeed.FirstMember, UserRole.User),
            Member(second.Id, TestSeed.SecondAdmin, UserRole.Admin)
        );
        await db.SaveChangesAsync();
        Seed = new TestSeed(first.Id, second.Id);
    }

    private static OrganizationUser Member(
        int organizationId,
        TestIdentity identity,
        UserRole role
    ) =>
        new()
        {
            OrganizationId = organizationId,
            UserId = identity.Id,
            UserName = identity.Name,
            Email = identity.Email,
            Role = role,
            JoinedAt = DateTime.UtcNow,
        };
}
