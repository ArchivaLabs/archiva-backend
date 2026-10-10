using Archiva.Application.Common.Models;
using Archiva.Domain.Entities;
using Archiva.Infrastructure.Data;
using Archiva.Shared;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Respawn;
using Respawn.Graph;

namespace Archiva.Infrastructure.IntegrationTests;

[SetUpFixture]
public sealed class IntegrationHost
{
    private static DistributedApplication? _app;
    private static ServiceProvider? _services;
    private static SqlConnection? _resetConnection;
    private static Respawner? _respawner;

    internal static IServiceProvider Services => _services!;
    internal static TestClock Clock { get; } = new();
    internal static int FirstOrganizationId { get; private set; }
    internal static int SecondOrganizationId { get; private set; }

    [OneTimeSetUp]
    public async Task StartAsync()
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
            ServicesName.Database,
            timeout.Token
        );

        var database =
            await _app.GetConnectionStringAsync(ServicesName.Database)
            ?? throw new InvalidOperationException("Test database connection is missing.");
        var blobs =
            await _app.GetConnectionStringAsync(ServicesName.BlobStorage)
            ?? throw new InvalidOperationException("Test blob connection is missing.");
        var queues =
            await _app.GetConnectionStringAsync(ServicesName.AnalysisQueues)
            ?? throw new InvalidOperationException("Test queue connection is missing.");

        var registrations = new ServiceCollection();
        registrations.AddLogging();
        registrations.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(database, sql => sql.EnableRetryOnFailure())
        );
        registrations.AddSingleton<TimeProvider>(Clock);
        registrations.Configure<DocumentAnalysisOptions>(options =>
        {
            options.MonthlyBillableUnitLimit = 5;
            options.MonthlySummaryCharacterLimit = 100;
        });
        registrations.AddSingleton(new BlobServiceClient(blobs));
        registrations.AddSingleton(new QueueServiceClient(queues));
        _services = registrations.BuildServiceProvider();

        await using (var scope = _services.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .Database.MigrateAsync(timeout.Token);
        }

        _resetConnection = new SqlConnection(database);
        await _resetConnection.OpenAsync(timeout.Token);
        _respawner = await Respawner.CreateAsync(
            _resetConnection,
            new RespawnerOptions
            {
                DbAdapter = DbAdapter.SqlServer,
                TablesToIgnore = [new Table("__EFMigrationsHistory")],
            }
        );
        await _resetConnection.CloseAsync();
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (_resetConnection is not null)
            await _resetConnection.DisposeAsync();
        if (_services is not null)
            await _services.DisposeAsync();
        if (_app is not null)
            await _app.DisposeAsync();
    }

    internal static async Task ResetAsync()
    {
        await _resetConnection!.OpenAsync();
        try
        {
            await _respawner!.ResetAsync(_resetConnection);
        }
        finally
        {
            await _resetConnection.CloseAsync();
        }

        var blobs = Services.GetRequiredService<BlobServiceClient>();
        var queues = Services.GetRequiredService<QueueServiceClient>();
        await blobs.GetBlobContainerClient("documents").DeleteIfExistsAsync();
        await queues.GetQueueClient(ServicesName.AnalysisQueueName).DeleteIfExistsAsync();
        Clock.SetUtcNow(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

        await WithDbAsync(async db =>
        {
            var first = new Organization { Name = "First" };
            var second = new Organization { Name = "Second" };
            db.Organizations.AddRange(first, second);
            await db.SaveChangesAsync();
            FirstOrganizationId = first.Id;
            SecondOrganizationId = second.Id;
        });
    }

    internal static async Task<T> WithDbAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    internal static async Task WithDbAsync(Func<ApplicationDbContext, Task> action)
    {
        await WithDbAsync(async db =>
        {
            await action(db);
            return true;
        });
    }
}

[NonParallelizable]
public abstract class IntegrationTestBase
{
    [SetUp]
    public Task SetUp() => IntegrationHost.ResetAsync();
}

public sealed class TestClock : TimeProvider
{
    private DateTimeOffset _utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
}

file static class ServicesName
{
    public const string Database = Services.Database;
    public const string BlobStorage = Services.BlobStorage;
    public const string AnalysisQueues = Services.AnalysisQueues;
    public const string AnalysisQueueName = Services.AnalysisQueueName;
}
