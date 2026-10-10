using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Archiva.Application.Auth.Command.SyncUser;
using Archiva.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Archiva.Application.FunctionalTests.Security;

public sealed class JwtBearerValidationTests : TestBase
{
    private const string Issuer = "https://issuer.archiva.test";
    private const string Audience = "archiva-api-test";
    private static readonly SymmetricSecurityKey SigningKey = new(
        Encoding.UTF8.GetBytes("archiva-local-jwt-validation-test-signing-key-2026")
    );

    private WebApplicationFactory<Program> _factory = null!;
    private readonly StructuredLogProvider _logs = new();

    [OneTimeSetUp]
    public void ConfigureBearerHost()
    {
        _factory = FunctionalTestSetup.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ILoggerProvider>(_logs);
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                });
                services.PostConfigure<JwtBearerOptions>(
                    JwtBearerDefaults.AuthenticationScheme,
                    options =>
                    {
                        var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                        configuration.SigningKeys.Add(SigningKey);
                        options.ConfigurationManager =
                            new StaticConfigurationManager<OpenIdConnectConfiguration>(
                                configuration
                            );
                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidIssuer = Issuer,
                            ValidateIssuer = true,
                            ValidAudience = Audience,
                            ValidateAudience = true,
                            IssuerSigningKey = SigningKey,
                            ValidateIssuerSigningKey = true,
                            RequireSignedTokens = true,
                            ValidateLifetime = true,
                            ClockSkew = TimeSpan.Zero,
                        };
                        options.Events = new JwtBearerEvents();
                    }
                );
            })
        );
    }

    [OneTimeTearDown]
    public async Task DisposeBearerHost() => await _factory.DisposeAsync();

    [Test]
    public async Task Valid_signed_token_uses_oid_for_membership()
    {
        _logs.Clear();
        using var client = CreateClient(CreateToken());

        using var response = await client.PostAsync("/api/auth/sync", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<SyncUserResult>();
        result.ShouldNotBeNull();
        result.Status.ShouldBe("existing");
        result.UserId.ShouldBe(TestSeed.FirstAdmin.Id);
        result.OrganizationId.ShouldBe(TestApp.Seed.FirstOrganizationId);

        var syncLog = _logs.Entries.Single(entry =>
            entry["{OriginalFormat}"]?.ToString()
            == "Auth sync resolved existing membership for user {UserId} in organization {OrganizationId}"
        );
        syncLog["UserId"].ShouldBe(TestSeed.FirstAdmin.Id);
        syncLog["OrganizationId"].ShouldBe(TestApp.Seed.FirstOrganizationId);
        syncLog.ShouldNotContainKey("Email");
        syncLog.ShouldNotContainKey("DisplayName");
    }

    [TestCase(TokenFault.BadSignature)]
    [TestCase(TokenFault.BadIssuer)]
    [TestCase(TokenFault.BadAudience)]
    [TestCase(TokenFault.Expired)]
    [TestCase(TokenFault.Unsigned)]
    public async Task Invalid_token_is_rejected(TokenFault fault)
    {
        using var client = CreateClient(CreateToken(fault));

        using var response = await client.PostAsync("/api/auth/sync", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Anonymous_request_is_rejected_by_bearer_scheme()
    {
        using var client = CreateClient();

        using var response = await client.PostAsync("/api/auth/sync", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Signed_token_without_oid_cannot_sync_or_accept_invitation()
    {
        await SeedInvitationAsync(TestSeed.Outsider.Email);
        _logs.Clear();
        using var client = CreateClient(CreateToken(TokenFault.MissingObjectId, TestSeed.Outsider));

        using var response = await client.PostAsync("/api/auth/sync", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("First Organization");
        await AssertInvitationUnchangedAsync(TestSeed.Outsider.Email);
        var deniedLog = _logs.Entries.Single(entry =>
            entry["{OriginalFormat}"]?.ToString() == "Auth sync denied: {Reason}"
        );
        deniedLog["Reason"].ShouldBe("MissingObjectId");
        deniedLog.ShouldNotContainKey("Email");
    }

    [Test]
    public async Task Signed_token_without_email_cannot_bind_invitation()
    {
        await SeedInvitationAsync(TestSeed.Outsider.Email);
        _logs.Clear();
        using var client = CreateClient(CreateToken(TokenFault.MissingEmail, TestSeed.Outsider));

        using var response = await client.PostAsync("/api/auth/sync", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await AssertInvitationUnchangedAsync(TestSeed.Outsider.Email);
        var deniedLog = _logs.Entries.Single(entry =>
            entry["{OriginalFormat}"]?.ToString() == "Auth sync denied: {Reason} for user {UserId}"
        );
        deniedLog["Reason"].ShouldBe("MissingEmail");
        deniedLog["UserId"].ShouldBe(TestSeed.Outsider.Id);
        deniedLog.ShouldNotContainKey("Email");
    }

    private static async Task SeedInvitationAsync(string email) =>
        await TestApp.WithDbContextAsync(async db =>
        {
            db.UserInvitations.Add(
                new UserInvitation
                {
                    Email = email,
                    OrganizationId = TestApp.Seed.FirstOrganizationId,
                    ExpiresAt = DateTime.UtcNow.AddDays(1),
                }
            );
            await db.SaveChangesAsync();
        });

    private static async Task AssertInvitationUnchangedAsync(string email)
    {
        var state = await TestApp.WithDbContextAsync(async db => new
        {
            Accepted = await db.UserInvitations.AnyAsync(invitation =>
                invitation.Email == email && invitation.IsAccepted
            ),
            MembershipCount = await db.OrganizationUsers.CountAsync(member =>
                member.UserId == TestSeed.Outsider.Id
            ),
        });
        state.Accepted.ShouldBeFalse();
        state.MembershipCount.ShouldBe(0);
    }

    private HttpClient CreateClient(string? token = null)
    {
        var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }
        );
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                token
            );
        return client;
    }

    private static string CreateToken(
        TokenFault fault = TokenFault.None,
        TestIdentity? identity = null
    )
    {
        var now = DateTime.UtcNow;
        identity ??= TestSeed.FirstAdmin;
        var claims = new List<Claim> { new("name", identity.Name) };
        if (fault != TokenFault.MissingObjectId)
            claims.Add(new Claim("oid", identity.Id));
        if (fault != TokenFault.MissingEmail)
            claims.Add(new Claim("preferred_username", identity.Email));
        var signingKey =
            fault == TokenFault.BadSignature
                ? new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes("different-local-jwt-signing-key-2026-archiva")
                )
                : SigningKey;
        var token = new JwtSecurityToken(
            issuer: fault == TokenFault.BadIssuer ? "https://wrong-issuer.archiva.test" : Issuer,
            audience: fault == TokenFault.BadAudience ? "wrong-audience" : Audience,
            claims: claims,
            notBefore: now.AddMinutes(-10),
            expires: fault == TokenFault.Expired ? now.AddMinutes(-1) : now.AddMinutes(10),
            signingCredentials: fault == TokenFault.Unsigned
                ? null
                : new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public enum TokenFault
    {
        None,
        BadSignature,
        BadIssuer,
        BadAudience,
        Expired,
        Unsigned,
        MissingObjectId,
        MissingEmail,
    }

    private sealed class StructuredLogProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<IReadOnlyDictionary<string, object?>> _entries = new();

        public IReadOnlyCollection<IReadOnlyDictionary<string, object?>> Entries =>
            _entries.ToArray();

        public ILogger CreateLogger(string categoryName) =>
            new StructuredLogger(categoryName, _entries);

        public void Clear()
        {
            while (_entries.TryDequeue(out _)) { }
        }

        public void Dispose() { }
    }

    private sealed class StructuredLogger(
        string category,
        ConcurrentQueue<IReadOnlyDictionary<string, object?>> entries
    ) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (
                category == typeof(SyncUserCommandHandler).FullName
                && state is IEnumerable<KeyValuePair<string, object?>> values
            )
                entries.Enqueue(values.ToDictionary(entry => entry.Key, entry => entry.Value));
        }
    }
}
