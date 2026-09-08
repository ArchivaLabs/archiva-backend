using Archiva.Shared;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureContainerAppEnvironment("aca-env");

var databaseServer = builder
    .AddAzureSqlServer(Services.DatabaseServer)
    .RunAsContainer(container => container.WithLifetime(ContainerLifetime.Persistent))
    .AddDatabase(Services.Database);

// Azure Blob Storage
var storage = builder.AddAzureStorage("storage").RunAsEmulator().AddBlobs(Services.BlobStorage);

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

builder.Build().Run();
