using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Archiva.Application.Auth.Command.SyncUser;
using Archiva.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Archiva.Application.FunctionalTests.Auth;

public sealed class GoogleSignInTests : TestBase
{
    private const string Issuer = "https://accounts.google.com";
    private const string ClientId = "archiva-test";
    private const string Callback = "/api/auth/google/callback";

    [TestCase(false)]
    [TestCase(true)]
    public async Task Google_session_requires_valid_callback_and_csrf_and_survives_restart_until_revoked(
        bool localHttp
    )
    {
        using var rsa = RSA.Create(2048);
        using var certificate = new CertificateRequest(
            "CN=Archiva OAuth Test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        ).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var origin = localHttp ? "http://localhost:5173" : "https://localhost";
        using var backchannel = new GoogleTokenResponse(new RsaSecurityKey(rsa), origin);
        await using var factory = CreateFactory(certificate, backchannel, origin, localHttp);
        using var client = factory.CreateClient(ClientOptions());

        using var start = await client.GetAsync("/api/auth/google/start");
        start.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        start.Headers.CacheControl?.NoStore.ShouldBe(true);
        var redirect = start.Headers.Location!;
        var parameters = QueryHelpers.ParseQuery(redirect.Query);
        parameters["redirect_uri"].ToString().ShouldBe(origin + Callback);
        parameters["response_type"].ToString().ShouldBe("code");
        parameters["code_challenge_method"].ToString().ShouldBe("S256");
        backchannel.Nonce = parameters["nonce"].ToString();

        using var cancelled = await client.GetAsync(
            Callback + "?error=access_denied&state=invalid"
        );
        cancelled.Headers.Location!.AbsolutePath.ShouldBe("/login");

        var query = new Dictionary<string, string?>
        {
            ["state"] = parameters["state"].ToString(),
            ["code"] = "test-code",
        };
        using var callback = await client.GetAsync(QueryHelpers.AddQueryString(Callback, query));
        callback.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        callback.Headers.Location!.AbsolutePath.ShouldBe("/auth/google/complete");
        var setCookie = callback
            .Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-ArchivaGoogle="));
        setCookie.ShouldContain("secure", Case.Insensitive);
        setCookie.ShouldContain("httponly", Case.Insensitive);
        setCookie.ShouldContain("samesite=lax", Case.Insensitive);

        using var replay = await client.GetAsync(QueryHelpers.AddQueryString(Callback, query));
        replay.Headers.Location!.AbsolutePath.ShouldBe("/login");

        // Emulate a browser sending the Secure auth cookie to HTTP localhost.
        using var localClient = localHttp
            ? factory.CreateClient(ClientOptions(handleCookies: false))
            : null;
        if (localClient is not null)
        {
            localClient.BaseAddress = new Uri(origin);
            localClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "Cookie",
                setCookie.Split(';')[0]
            );
            using var bootstrap = await localClient.GetAsync("/api/auth/google/session");
            bootstrap.StatusCode.ShouldBe(HttpStatusCode.OK);
            var csrfCookie = bootstrap
                .Headers.GetValues("Set-Cookie")
                .Single(value => value.StartsWith("ArchivaAntiforgeryLocal="));
            localClient.DefaultRequestHeaders.Remove("Cookie");
            localClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "Cookie",
                setCookie.Split(';')[0] + "; " + csrfCookie.Split(';')[0]
            );
        }

        var sessionClient = localClient ?? client;
        var session = await sessionClient.GetFromJsonAsync<TestSession>("/api/auth/google/session");
        session.ShouldNotBeNull();
        session.UserId.ShouldStartWith("google:");
        sessionClient.DefaultRequestHeaders.Add("X-Archiva-Session", session.Session);
        using var withoutCsrf = await sessionClient.PostAsync("/api/auth/sync", null);
        withoutCsrf.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        sessionClient.DefaultRequestHeaders.Add("X-Archiva-CSRF", session.Csrf);

        using var sync = await sessionClient.PostAsync("/api/auth/sync", null);
        sync.StatusCode.ShouldBe(HttpStatusCode.OK);
        var profile = await sync.Content.ReadFromJsonAsync<SyncUserResult>();
        profile!.Status.ShouldBe("new");
        profile.UserId.ShouldBe(session.UserId);
        using var beforeOnboarding = await sessionClient.GetAsync("/api/meetings");
        beforeOnboarding.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var onboard = await sessionClient.PostAsJsonAsync(
            "/api/organizations",
            new { name = "OAuth test workspace" }
        );
        onboard.IsSuccessStatusCode.ShouldBeTrue();
        using var afterOnboarding = await sessionClient.GetAsync("/api/meetings");
        afterOnboarding.StatusCode.ShouldBe(HttpStatusCode.OK);

        var otherOrganizationMeeting = await TestApp.WithDbContextAsync(async db =>
        {
            var meeting = new Meeting
            {
                OrganizationId = TestApp.Seed.FirstOrganizationId,
                Title = "Another organisation's meeting",
                MeetingDate = DateTime.UtcNow,
            };
            db.Meetings.Add(meeting);
            await db.SaveChangesAsync();
            return meeting.Id;
        });
        using var crossOrganization = await sessionClient.GetAsync(
            $"/api/meetings/{otherOrganizationMeeting}"
        );
        crossOrganization.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var invalidBearer = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/auth/google/session"
        );
        invalidBearer.Headers.TryAddWithoutValidation("Authorization", "Bearer malformed");
        using var bearerResponse = await sessionClient.SendAsync(invalidBearer);
        bearerResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        await using var restartedFactory = CreateFactory(
            certificate,
            backchannel,
            origin,
            localHttp
        );
        using var restarted = restartedFactory.CreateClient(ClientOptions(handleCookies: false));
        restarted.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", setCookie.Split(';')[0]);
        restarted.DefaultRequestHeaders.Add("X-Archiva-Session", session.Session);
        using var resumed = await restarted.GetAsync("/api/meetings");
        resumed.StatusCode.ShouldBe(HttpStatusCode.OK);

        await TestApp.WithDbContextAsync(async db =>
        {
            var expiredAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.GoogleSessions.ExecuteUpdateAsync(fields =>
                fields.SetProperty(ticket => ticket.ExpiresAt, expiredAt)
            );
        });
        using var expired = await restarted.GetAsync("/api/meetings");
        expired.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Restore the fixture row only to exercise explicit revocation in the same browser flow.
        await TestApp.WithDbContextAsync(async db =>
        {
            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
            await db.GoogleSessions.ExecuteUpdateAsync(fields =>
                fields.SetProperty(ticket => ticket.ExpiresAt, expiresAt)
            );
        });
        using var logout = await sessionClient.PostAsync("/api/auth/google/logout", null);
        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var revoked = await restarted.GetAsync("/api/meetings");
        revoked.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        X509Certificate2 certificate,
        GoogleTokenResponse backchannel,
        string origin,
        bool localHttp
    ) =>
        FunctionalTestSetup.Factory.WithWebHostBuilder(builder =>
        {
            if (localHttp)
                builder.UseEnvironment("Development");
            builder.UseSetting("GoogleAuth:Enabled", "true");
            builder.UseSetting("GoogleAuth:FrontendOrigin", origin);
            builder.UseSetting("GoogleAuth:ClientId", ClientId);
            builder.UseSetting("GoogleAuth:ClientSecret", "test-secret");
            builder.UseSetting(
                "GoogleAuth:KeyCertificate",
                Convert.ToBase64String(certificate.Export(X509ContentType.Pfx, "test-password"))
            );
            builder.UseSetting("GoogleAuth:KeyCertificatePassword", "test-password");
            builder.ConfigureServices(services =>
            {
                services.Configure<OpenIdConnectOptions>(
                    "GoogleOidc",
                    options =>
                    {
                        options.Configuration = new OpenIdConnectConfiguration
                        {
                            Issuer = Issuer,
                            AuthorizationEndpoint = Issuer + "/o/oauth2/v2/auth",
                            TokenEndpoint = "https://oauth2.googleapis.com/token",
                        };
                        options.Configuration.SigningKeys.Add(backchannel.SigningKey);
                        options.Backchannel = new HttpClient(backchannel, disposeHandler: false);
                    }
                );
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultScheme = "ArchivaApi";
                    options.DefaultAuthenticateScheme = "ArchivaApi";
                    options.DefaultChallengeScheme = "ArchivaApi";
                });
            });
        });

    private static WebApplicationFactoryClientOptions ClientOptions(bool handleCookies = true) =>
        new()
        {
            AllowAutoRedirect = false,
            HandleCookies = handleCookies,
            BaseAddress = new Uri("https://localhost"),
        };

    private sealed record TestSession(string Session, string Csrf, string UserId);

    private sealed class GoogleTokenResponse(SecurityKey signingKey, string origin)
        : HttpMessageHandler
    {
        public SecurityKey SigningKey { get; } = signingKey;
        public string Nonce { get; set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var form = await request.Content!.ReadAsStringAsync(cancellationToken);
            form.ShouldContain("code_verifier=");
            form.ShouldContain("redirect_uri=" + Uri.EscapeDataString(origin + Callback));
            var now = DateTime.UtcNow;
            var token = new JwtSecurityToken(
                Issuer,
                ClientId,
                [
                    new Claim("sub", "oauth-test-subject"),
                    new Claim("email", "archiva-glassbox@gmail.com"),
                    new Claim("email_verified", "true", ClaimValueTypes.Boolean),
                    new Claim("nonce", Nonce),
                    new Claim(
                        "iat",
                        EpochTime
                            .GetIntDate(now)
                            .ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ClaimValueTypes.Integer64
                    ),
                ],
                now.AddMinutes(-1),
                now.AddMinutes(5),
                new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256)
            );
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(
                    new
                    {
                        id_token = new JwtSecurityTokenHandler().WriteToken(token),
                        access_token = "unused-test-access-token",
                        token_type = "Bearer",
                    }
                ),
            };
        }
    }
}
