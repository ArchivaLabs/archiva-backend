using Archiva.Infrastructure.Data;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.AddServiceDefaults();

// builder.AddKeyVaultIfConfigured();
builder.AddApplicationServices();
builder.AddInfrastructureServices();
builder.AddWebServices();

var app = builder.Build();

// Phase 1.4 — Forwarded headers must be first in the pipeline.
// Azure Container Apps terminates TLS at ingress and forwards HTTP internally.
// Without this, UseHttpsRedirection causes redirect loops and URLs generated
// by the app use http:// instead of https://.
app.UseForwardedHeaders(
    new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    }
);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    await app.InitialiseDatabaseAsync();
}
else
{
    app.UseHsts();
}

// Phase 1.8 — Storage CORS must be configured in all environments.
// Azurite doesn't persist CORS rules across restarts (dev), and Azure
// Storage needs the rule so the frontend can fetch blobs cross-origin (prod).
await app.ConfigureStorageAsync();

// Phase 1.1 — CORS origins from config so dev and prod differ without code changes.
var allowedOrigins =
    builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"];

app.UseCors(policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod());

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Phase 1.5 — OpenAPI and Scalar are dev-only.
// In production these expose the full API surface to anonymous visitors.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
    app.Map("/", () => Results.Redirect("/scalar"));
}

app.UseExceptionHandler(options => { });

app.MapDefaultEndpoints();
app.MapEndpoints(typeof(Program).Assembly);

app.Run();
