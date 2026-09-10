using Archiva.Shared;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureContainerAppEnvironment("aca-env");

var databaseServer = builder
    .AddAzureSqlServer(Services.DatabaseServer)
    .RunAsContainer(container => container.WithLifetime(ContainerLifetime.Persistent))
    .AddDatabase(Services.Database);

// Azure Blob Storage
var storage = builder.AddAzureStorage("storage").RunAsEmulator().AddBlobs(Services.BlobStorage);

// Application Insights connection string. Declared as a parameter rather than set
// as a Container App env var by hand, because azd deploy regenerates that env
// block from this model and drops anything it does not know about. azd keeps the
// value in .azure/<env>/.env (gitignored) and injects it on every deploy, so the
// ingestion key never enters source control. Unset locally, which leaves the
// exporter inert — ServiceDefaults only calls UseAzureMonitor when it is present.
var appInsightsConnectionString = builder.AddParameter("appInsightsConnectionString", secret: true);

var web = builder
    .AddProject<Projects.Web>(Services.WebApi)
    .WithReference(databaseServer)
    .WaitFor(databaseServer)
    .WithReference(storage)
    .WithEnvironment("APPLICATIONINSIGHTS_CONNECTION_STRING", appInsightsConnectionString)
    .WithExternalHttpEndpoints()
    // Scale settings must live in the Aspire model, not in infra/**.bicep: azd
    // deploy regenerates the container app template from this model and would
    // otherwise restore the defaults (min 1 / max 10), keeping a replica running
    // continuously. Min 0 lets an idle app cost nothing; max 1 caps spend.
    .PublishAsAzureContainerApp(
        (infra, app) =>
        {
            app.Template.Scale.MinReplicas = 0;
            app.Template.Scale.MaxReplicas = 1;
        }
    )
    .WithUrlForEndpoint(
        "http",
        url =>
        {
            url.DisplayText = "Scalar API Reference";
            url.Url = "/scalar";
        }
    );

builder.Build().Run();
