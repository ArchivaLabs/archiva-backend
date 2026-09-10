using Archiva.Shared;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureContainerAppEnvironment("aca-env");

var databaseServer = builder
    .AddAzureSqlServer(Services.DatabaseServer)
    .RunAsContainer(container => container.WithLifetime(ContainerLifetime.Persistent))
    .AddDatabase(Services.Database);

// Azure Blob Storage
var storage = builder.AddAzureStorage("storage").RunAsEmulator().AddBlobs(Services.BlobStorage);

// Application Insights connection string, read at publish time from this AppHost's
// configuration (user secrets locally, or an environment variable in CI):
//
//   dotnet user-secrets set APPLICATIONINSIGHTS_CONNECTION_STRING "<value>" --project src/AppHost
//
// It is read here rather than set as a Container App env var by hand, because azd
// deploy regenerates that env block from this model and drops anything declared
// only in bicep — which is how CORS broke in production on 2026-09-08.
//
// AddParameter() would be the tidier mechanism, but azd generates the container
// app bicep with the parameter declared and no assignment for it, then fails with
// BCP258 and never prompts for a value. Reading configuration sidesteps that.
//
// Keeping it out of appsettings keeps the ingestion key out of source control.
// When unset the env var is omitted entirely and the exporter stays inert —
// ServiceDefaults only calls UseAzureMonitor when the value is present.
var appInsightsConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];

var web = builder
    .AddProject<Projects.Web>(Services.WebApi)
    .WithReference(databaseServer)
    .WaitFor(databaseServer)
    .WithReference(storage)
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

// Applied conditionally so the variable is absent — not empty — when no value is
// configured, which is the state the exporter treats as "disabled".
if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
{
    web.WithEnvironment("APPLICATIONINSIGHTS_CONNECTION_STRING", appInsightsConnectionString);
}

builder.Build().Run();
