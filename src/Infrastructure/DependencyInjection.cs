using Archiva.Application.Common.Interfaces;
using Archiva.Infrastructure.Data;
using Archiva.Infrastructure.Data.Interceptors;
using Archiva.Infrastructure.Storage;
using Archiva.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static void AddInfrastructureServices(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString(Services.Database);
        Guard.Against.Null(
            connectionString,
            message: $"Connection string '{Services.Database}' not found."
        );

        builder.Services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();
        builder.Services.AddScoped<ISaveChangesInterceptor, DispatchDomainEventsInterceptor>();

        builder.Services.AddDbContext<ApplicationDbContext>(
            (sp, options) =>
            {
                options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());

                // Azure SQL serverless auto-pauses when idle and takes 30-60s to
                // resume, answering with transient error 40613 meanwhile. The
                // default retry budget gives up in roughly 30s, so the first
                // request after an idle period would 500 even though the database
                // is on its way up. Ten attempts backing off to 15s comfortably
                // outlasts a resume.
                options.UseSqlServer(
                    connectionString,
                    sql =>
                        sql.EnableRetryOnFailure(
                            maxRetryCount: 10,
                            maxRetryDelay: TimeSpan.FromSeconds(15),
                            errorNumbersToAdd: null
                        )
                );
            }
        );

        // Retry is configured explicitly above; without DisableRetry the Aspire
        // component replaces it with its own defaults.
        builder.EnrichSqlServerDbContext<ApplicationDbContext>(settings =>
            settings.DisableRetry = true
        );

        builder.Services.AddScoped<IApplicationDbContext>(provider =>
            provider.GetRequiredService<ApplicationDbContext>()
        );

        builder.Services.AddScoped<ApplicationDbContextInitialiser>();

        builder.Services.AddSingleton(TimeProvider.System);

        // BlobServiceClient is registered by AddAzureBlobServiceClient and injected
        // into both BlobStorageService (scoped) and UserDelegationKeyProvider (singleton).
        builder.AddAzureBlobServiceClient(Services.BlobStorage);

        // Singleton: caches the Azure user delegation key across requests.
        // Must be singleton because BlobStorageService is scoped and cannot
        // hold cross-request state itself.
        builder.Services.AddSingleton<UserDelegationKeyProvider>();
        builder.Services.AddScoped<IStorageService, BlobStorageService>();
    }
}
