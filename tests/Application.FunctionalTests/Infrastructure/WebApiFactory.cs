using Archiva.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Archiva.Application.FunctionalTests.Infrastructure;

public sealed class WebApiFactory(
    string databaseConnectionString,
    string blobConnectionString,
    string queueConnectionString
) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting(
            $"ConnectionStrings:{Archiva.Shared.Services.Database}",
            databaseConnectionString
        );
        builder.UseSetting(
            $"ConnectionStrings:{Archiva.Shared.Services.BlobStorage}",
            blobConnectionString
        );
        builder.UseSetting(
            $"ConnectionStrings:{Archiva.Shared.Services.AnalysisQueues}",
            queueConnectionString
        );

        builder.ConfigureTestServices(services =>
        {
            foreach (
                var worker in services
                    .Where(descriptor =>
                        descriptor.ServiceType == typeof(IHostedService)
                        && (
                            descriptor.ImplementationType == typeof(DocumentAnalysisQueueWorker)
                            || descriptor.ImplementationType
                                == typeof(DocumentAnalysisReconciliationWorker)
                        )
                    )
                    .ToArray()
            )
            {
                services.Remove(worker);
            }

            services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultScheme = TestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { }
                );
        });
    }
}
