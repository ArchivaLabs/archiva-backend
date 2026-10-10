using Archiva.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Archiva.Application.FunctionalTests;

[SetUpFixture]
public sealed class FunctionalTestSetup
{
    internal static WebApiFactory Factory { get; private set; } = null!;
    internal static DatabaseResetter DbResetter { get; private set; } = null!;

    private static DistributedApplication? _app;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.TestAppHost>(
            args: [],
            configureBuilder: (options, _) => options.DisableDashboard = true
        );
        builder.Configuration["ASPIRE_ALLOW_UNSECURED_TRANSPORT"] = "true";

        _app = await builder.BuildAsync(timeout.Token);
        await _app.StartAsync(timeout.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync(
            Services.Database,
            timeout.Token
        );

        var database =
            await _app.GetConnectionStringAsync(Services.Database)
            ?? throw new InvalidOperationException("Test SQL connection string is missing.");
        var blobs =
            await _app.GetConnectionStringAsync(Services.BlobStorage)
            ?? throw new InvalidOperationException("Test blob connection string is missing.");
        var queues =
            await _app.GetConnectionStringAsync(Services.AnalysisQueues)
            ?? throw new InvalidOperationException("Test queue connection string is missing.");

        Factory = new WebApiFactory(database, blobs, queues);
        using var client = Factory.CreateClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }
        );
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .Database.MigrateAsync(timeout.Token);
        }
        DbResetter = await DatabaseResetter.CreateAsync(database);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (DbResetter is not null)
            await DbResetter.DisposeAsync();
        if (Factory is not null)
            await Factory.DisposeAsync();
        if (_app is not null)
            await _app.DisposeAsync();
    }
}
