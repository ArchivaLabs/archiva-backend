using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Archiva.Infrastructure.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using OpenTelemetry.Trace;
using GoogleTokenValidatedContext = Microsoft.AspNetCore.Authentication.OpenIdConnect.TokenValidatedContext;

namespace Archiva.Web.Authentication;

internal static class GoogleAuthentication
{
    internal const string Oidc = "GoogleOidc";
    internal const string Cookie = "GoogleSession";
    internal const string Callback = "/api/auth/google/callback";
    internal const string SessionPath = "/api/auth/google/session";
    internal const string SessionClaim = "archiva:google-session";
    internal const string InvitationClaim = "archiva:may-claim-invitation";
    private const string CookieName = "__Host-ArchivaGoogle";
    private const string ApiScheme = "ArchivaApi";

    private sealed class GoogleCookieManager : ICookieManager
    {
        private readonly ChunkingCookieManager _inner = new();

        public string? GetRequestCookie(HttpContext context, string key) =>
            // A validated callback must allocate a fresh ticket rather than reusing
            // the key carried by an earlier cookie on the callback request.
            context.Request.Path.Equals(Callback)
                ? null
                : _inner.GetRequestCookie(context, key);

        public void AppendResponseCookie(
            HttpContext context,
            string key,
            string? value,
            CookieOptions options
        ) => _inner.AppendResponseCookie(context, key, value, options);

        public void DeleteCookie(HttpContext context, string key, CookieOptions options) =>
            _inner.DeleteCookie(context, key, options);
    }

    internal static bool IsGoogle(ClaimsPrincipal? principal) =>
        principal?.Identity?.AuthenticationType == Cookie && principal.Identity.IsAuthenticated;

    internal static void AddGoogleAuthentication(this IHostApplicationBuilder builder)
    {
        var config = builder.Configuration.GetSection("GoogleAuth");
        if (!config.GetValue<bool>("Enabled"))
            return;

        var originValue = Guard.Against.NullOrWhiteSpace(config["FrontendOrigin"]);
        if (
            !Uri.TryCreate(originValue, UriKind.Absolute, out var origin)
            || origin.UserInfo.Length != 0
            || origin.GetLeftPart(UriPartial.Authority) != originValue
            || (
                origin.Scheme != Uri.UriSchemeHttps
                && !(
                    builder.Environment.IsDevelopment()
                    && origin.IsLoopback
                    && origin.Scheme == Uri.UriSchemeHttp
                )
            )
        )
        {
            throw new InvalidOperationException(
                "GoogleAuth:FrontendOrigin must be a canonical HTTPS origin."
            );
        }

        var protection = builder
            .Services.AddDataProtection()
            .SetApplicationName("Archiva.GoogleAuth.v1")
            .PersistKeysToDbContext<ApplicationDbContext>();
        if (!builder.Environment.IsDevelopment())
        {
            var certificate = X509CertificateLoader.LoadPkcs12(
                Convert.FromBase64String(Guard.Against.NullOrWhiteSpace(config["KeyCertificate"])),
                config["KeyCertificatePassword"],
                OperatingSystem.IsMacOS()
                    ? X509KeyStorageFlags.DefaultKeySet
                    : X509KeyStorageFlags.EphemeralKeySet
            );
            protection
                .ProtectKeysWithCertificate(certificate)
                .UnprotectKeysWithAnyCertificate(certificate);
        }

        builder.Services.AddSingleton<GoogleTicketStore>();
        builder
            .Services.AddOptions<CookieAuthenticationOptions>(Cookie)
            .Configure<GoogleTicketStore>((options, store) => options.SessionStore = store);

        var useLocalHttp =
            builder.Environment.IsDevelopment()
            && origin.IsLoopback
            && origin.Scheme == Uri.UriSchemeHttp;

        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-Archiva-CSRF";
            // ASP.NET Core rejects HTTP requests outright when the antiforgery
            // policy is Always. Only the configured local development origin uses HTTP.
            options.Cookie.Name = useLocalHttp
                ? "ArchivaAntiforgeryLocal"
                : "__Host-ArchivaAntiforgery";
            options.Cookie.Path = "/";
            options.Cookie.SecurePolicy = useLocalHttp
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });

        builder
            .Services.AddAuthentication(ApiScheme)
            .AddPolicyScheme(
                ApiScheme,
                null,
                options =>
                {
                    options.ForwardDefaultSelector = context =>
                        context.Request.Headers.ContainsKey("Authorization")
                        || !context.Request.Cookies.ContainsKey(CookieName)
                            ? JwtBearerDefaults.AuthenticationScheme
                            : Cookie;
                }
            )
            .AddCookie(
                Cookie,
                options =>
                {
                    options.CookieManager = new GoogleCookieManager();
                    options.Cookie.Name = CookieName;
                    options.Cookie.Path = "/";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                    options.Cookie.SameSite = SameSiteMode.Lax;
                    options.ExpireTimeSpan = TimeSpan.FromHours(8);
                    options.SlidingExpiration = false;
                    options.Events.OnSignedIn = context =>
                    {
                        Log(context.HttpContext)
                            .LogInformation(
                                "External session {Provider} {Outcome}",
                                "Google",
                                "Created"
                            );
                        return Task.CompletedTask;
                    };
                    options.Events.OnRedirectToLogin = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    };
                    options.Events.OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    };
                }
            )
            .AddOpenIdConnect(
                Oidc,
                options =>
                {
                    options.Authority = "https://accounts.google.com";
                    options.ClientId = Guard.Against.NullOrWhiteSpace(config["ClientId"]);
                    options.ClientSecret = Guard.Against.NullOrWhiteSpace(config["ClientSecret"]);
                    options.CallbackPath = Callback;
                    options.SignInScheme = Cookie;
                    options.ResponseType = OpenIdConnectResponseType.Code;
                    options.ResponseMode = OpenIdConnectResponseMode.Query;
                    options.UsePkce = true;
                    options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable;
                    options.MapInboundClaims = false;
                    options.SaveTokens = false;
                    options.Scope.Clear();
                    options.Scope.Add("openid");
                    options.Scope.Add("email");
                    options.Scope.Add("profile");
                    options.Events.OnRedirectToIdentityProvider = context =>
                    {
                        context.ProtocolMessage.RedirectUri = originValue + Callback;
                        return Task.CompletedTask;
                    };
                    options.Events.OnTokenValidated = ValidateIdentity;
                    options.Events.OnRemoteFailure = context =>
                    {
                        Log(context.HttpContext)
                            .LogWarning(
                                "External sign-in {Provider} {Outcome} {Reason}",
                                "Google",
                                "Denied",
                                "CallbackFailed"
                            );
                        context.HandleResponse();
                        context.Response.Redirect(originValue + "/login?google=failed");
                        return Task.CompletedTask;
                    };
                    options.Events.OnTicketReceived = context =>
                    {
                        // Return only to the configured origin and a fixed frontend path.
                        context.ReturnUri = originValue + "/auth/google/complete";
                        return Task.CompletedTask;
                    };
                }
            );

        // The OAuth callback carries a one-time code in its query string.
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
        builder.Logging.AddFilter(
            "Microsoft.AspNetCore.Authentication.OpenIdConnect",
            LogLevel.None
        );
        builder.Services.ConfigureOpenTelemetryTracerProvider(tracer =>
            tracer.AddAspNetCoreInstrumentation(options =>
                options.Filter = context => !context.Request.Path.Equals(Callback)
            )
        );
    }

    private static Task ValidateIdentity(GoogleTokenValidatedContext context)
    {
        var principal = context.Principal!;
        var subject = principal.FindFirstValue("sub");
        var email = principal.FindFirstValue("email")?.Trim();
        if (
            string.IsNullOrWhiteSpace(subject)
            || subject.Length > 255
            || subject.Any(character => !char.IsAscii(character) || char.IsControl(character))
            || string.IsNullOrWhiteSpace(email)
            || email.Length > 254
            || !System.Net.Mail.MailAddress.TryCreate(email, out var address)
            || address.Address != email
        )
        {
            context.Fail("Required Google identity claims are missing or invalid.");
            return Task.CompletedTask;
        }

        // Hash the exact case-sensitive subject so SQL collation cannot equate two subjects.
        var id = "google:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subject)));
        var verified = string.Equals(
            principal.FindFirstValue("email_verified"),
            "true",
            StringComparison.OrdinalIgnoreCase
        );
        var domain = address.Host;
        var hostedDomain = principal.FindFirstValue("hd");
        var canClaimInvitation =
            verified
            && (
                email.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase)
                || (
                    !string.IsNullOrWhiteSpace(hostedDomain)
                    && string.Equals(domain, hostedDomain, StringComparison.OrdinalIgnoreCase)
                )
            );
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, id),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Name, principal.FindFirstValue("name") ?? string.Empty),
            new(SessionClaim, Guid.NewGuid().ToString("N")),
        };
        if (canClaimInvitation)
            claims.Add(new Claim(InvitationClaim, "true"));

        context.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Cookie));
        return Task.CompletedTask;
    }

    internal static void UseGoogleRequestHeaders(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("GoogleAuth:Enabled"))
            return;

        app.Use(
            async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                    context.Response.Headers.CacheControl = "no-store";
                if (context.Request.Path.StartsWithSegments("/api/auth/google"))
                    context.Response.Headers["Referrer-Policy"] = "no-referrer";
                await next(context);
            }
        );
    }

    internal static void UseGoogleSessionProtection(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("GoogleAuth:Enabled"))
            return;

        app.Use(
            async (context, next) =>
            {
                if (!IsGoogle(context.User))
                {
                    await next(context);
                    return;
                }

                var request = context.Request;
                // Bootstrap obtains the current cookie's session marker and request token.
                var isBootstrap =
                    HttpMethods.IsGet(request.Method) && request.Path.Equals(SessionPath);
                // The remote challenge may replace an existing Google session.
                var isChallenge =
                    HttpMethods.IsGet(request.Method)
                    && request.Path.Equals("/api/auth/google/start");
                if (request.Path.StartsWithSegments("/api") && !isBootstrap && !isChallenge)
                {
                    if (
                        !string.Equals(
                            request.Headers["X-Archiva-Session"],
                            context.User.FindFirstValue(SessionClaim),
                            StringComparison.Ordinal
                        )
                    )
                    {
                        Log(context)
                            .LogWarning(
                                "External session {Provider} {Outcome} {Reason}",
                                "Google",
                                "Denied",
                                "SessionMismatch"
                            );
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return;
                    }

                    if (
                        !HttpMethods.IsGet(request.Method)
                        && !HttpMethods.IsHead(request.Method)
                        && !HttpMethods.IsOptions(request.Method)
                    )
                    {
                        try
                        {
                            await context
                                .RequestServices.GetRequiredService<IAntiforgery>()
                                .ValidateRequestAsync(context);
                        }
                        catch (AntiforgeryValidationException)
                        {
                            Log(context)
                                .LogWarning(
                                    "External session {Provider} {Outcome} {Reason}",
                                    "Google",
                                    "Denied",
                                    "Antiforgery"
                                );
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            return;
                        }
                    }
                }

                await next(context);
            }
        );
    }

    internal static ILogger Log(HttpContext context) =>
        context
            .RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Archiva.Auth.Google");
}
