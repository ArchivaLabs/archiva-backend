using Archiva.Application.Common.Interfaces;
using Archiva.Infrastructure.Analysis;
using Archiva.Infrastructure.Data;
using Archiva.Infrastructure.Data.Interceptors;
using Archiva.Infrastructure.Search;
using Archiva.Infrastructure.Storage;
using Archiva.Shared;
using Microsoft.Data.SqlClient;
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

        // Azure SQL serverless auto-pauses after 60 idle minutes and takes 30-60s
        // to resume. The connection string azd generates for managed-identity auth
        // carries no Connect Timeout, so SqlClient applies its 15s default — which
        // expires long before the resume completes. Every first request after a
        // pause then failed with "Connection Timeout Expired ... post-login phase"
        // (Application Insights, 2026-09-17: 500s lasting 18-32s).
        //
        // The SQL-auth string this replaced carried Connection Timeout=120; that
        // was lost when the app moved to managed identity. Setting it here rather
        // than in the connection string keeps it safe from azd regeneration.
        var sqlConnection = new SqlConnectionStringBuilder(connectionString)
        {
            ConnectTimeout = 120,
        };

        builder.Services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();
        builder.Services.AddScoped<ISaveChangesInterceptor, DispatchDomainEventsInterceptor>();

        builder.Services.AddDbContext<ApplicationDbContext>(
            (sp, options) =>
            {
                options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());

                // Backstop for a resume that outlasts even the widened timeout.
                //
                // -2 is SqlClient's timeout code, added explicitly because EF's
                // transient-error detector inspects SqlException.Number, and a
                // post-login connection timeout surfaces as a Win32Exception it
                // does not recognise. Without it the observed failures were not
                // retried at all, whatever the retry count said.
                options.UseSqlServer(
                    sqlConnection.ConnectionString,
                    sql =>
                        sql.EnableRetryOnFailure(
                            maxRetryCount: 6,
                            maxRetryDelay: TimeSpan.FromSeconds(15),
                            errorNumbersToAdd: [-2]
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

        builder.Services.Configure<Archiva.Application.Common.Models.DocumentAnalysisOptions>(
            builder.Configuration.GetSection(
                Archiva.Application.Common.Models.DocumentAnalysisOptions.SectionName
            )
        );
        builder.Services.AddSingleton<IDocumentTextExtractor, AzureDocumentTextExtractor>();
        builder.Services.AddSingleton<IDocumentSummarizer, AzureOpenAISummarizer>();
        builder.Services.AddScoped<IDocumentAnalysisBudget, DocumentAnalysisBudget>();
        builder.Services.AddScoped<IDocumentSearchService, SqlDocumentSearchService>();

        builder.Services.AddScoped<ApplicationDbContextInitialiser>();

        builder.Services.AddSingleton(TimeProvider.System);

        // BlobServiceClient is registered by AddAzureBlobServiceClient and injected
        // into both BlobStorageService (scoped) and UserDelegationKeyProvider (singleton).
        builder.AddAzureBlobServiceClient(Services.BlobStorage);
        builder.AddAzureQueueServiceClient(Services.AnalysisQueues);
        builder.Services.AddScoped<IDocumentAnalysisQueue, AzureDocumentAnalysisQueue>();

        // Singleton: caches the Azure user delegation key across requests.
        // Must be singleton because BlobStorageService is scoped and cannot
        // hold cross-request state itself.
        builder.Services.AddSingleton<UserDelegationKeyProvider>();
        builder.Services.AddScoped<IStorageService, BlobStorageService>();
    }
}
