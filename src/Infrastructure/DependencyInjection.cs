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
                options.UseSqlServer(connectionString);
            }
        );

        builder.EnrichSqlServerDbContext<ApplicationDbContext>();

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
