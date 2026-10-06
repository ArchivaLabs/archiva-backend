using Archiva.Shared;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.Resources;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureContainerAppEnvironment("aca-env");

var databaseServer = builder
    .AddAzureSqlServer(Services.DatabaseServer)
    .RunAsContainer(container => container.WithLifetime(ContainerLifetime.Persistent))
    .AddDatabase(Services.Database);

// Azure Blob Storage and Queue Storage share one storage account.
var storageResource = builder.AddAzureStorage("storage");
var storage = storageResource.RunAsEmulator().AddBlobs(Services.BlobStorage);
var analysisQueues = storageResource.AddQueues(Services.AnalysisQueues);
var webIdentityResourceId = builder.Configuration["WEBAPI_IDENTITY_ID"];
var webIdentityClientId = builder.Configuration["WEBAPI_IDENTITY_CLIENTID"];
var storageAccountName =
    builder.Configuration["STORAGE_ACCOUNT_NAME"] ?? storageResource.Resource.Name;
var documentAnalysisAi = !builder.ExecutionContext.IsPublishMode
    ? null
    : builder.AddBicepTemplate("document-analysis-ai", "document-analysis-ai.bicep");

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
    .WithReference(analysisQueues)
    .WaitFor(analysisQueues)
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

            if (string.IsNullOrWhiteSpace(webIdentityResourceId))
            {
                app.Identity.ManagedServiceIdentityType = ManagedServiceIdentityType.SystemAssigned;
            }
            else
            {
                app.Identity.ManagedServiceIdentityType = ManagedServiceIdentityType.UserAssigned;
            }

            app.Template.Scale.Rules.Add(
                new ContainerAppScaleRule
                {
                    Name = "document-analysis-queue",
                    AzureQueue = new ContainerAppQueueScaleRule
                    {
                        AccountName = storageAccountName,
                        QueueName = Services.AnalysisQueueName,
                        QueueLength = 1,
                        Identity = string.IsNullOrWhiteSpace(webIdentityResourceId)
                            ? "system"
                            : webIdentityResourceId,
                    },
                }
            );
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

if (
    !builder.ExecutionContext.IsPublishMode
    && builder.Environment.EnvironmentName == Microsoft.Extensions.Hosting.Environments.Development
)
    web.WithHttpEndpoint(port: 5150, name: "http");

// Applied conditionally so the variable is absent — not empty — when no value is
// configured, which is the state the exporter treats as "disabled".
if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
{
    web.WithEnvironment("APPLICATIONINSIGHTS_CONNECTION_STRING", appInsightsConnectionString);
}

if (!string.IsNullOrWhiteSpace(webIdentityClientId))
    web.WithEnvironment("AZURE_CLIENT_ID", webIdentityClientId);

if (documentAnalysisAi is not null)
{
    web.WithEnvironment(
        "DocumentIntelligence__Endpoint",
        documentAnalysisAi.GetOutput("DocumentIntelligence__Endpoint")
    );
    web.WithEnvironment(
        "AzureOpenAI__Endpoint",
        documentAnalysisAi.GetOutput("AzureOpenAI__Endpoint")
    );
    web.WithEnvironment(
        "AzureOpenAI__DeploymentName",
        documentAnalysisAi.GetOutput("AzureOpenAI__DeploymentName")
    );
}
else
{
    var documentIntelligenceEndpoint = builder.Configuration["DocumentIntelligence:Endpoint"];
    if (!string.IsNullOrWhiteSpace(documentIntelligenceEndpoint))
        web.WithEnvironment("DocumentIntelligence__Endpoint", documentIntelligenceEndpoint);

    var openAiEndpoint = builder.Configuration["AzureOpenAI:Endpoint"];
    if (!string.IsNullOrWhiteSpace(openAiEndpoint))
        web.WithEnvironment("AzureOpenAI__Endpoint", openAiEndpoint);

    var openAiDeployment = builder.Configuration["AzureOpenAI:DeploymentName"];
    if (!string.IsNullOrWhiteSpace(openAiDeployment))
        web.WithEnvironment("AzureOpenAI__DeploymentName", openAiDeployment);
}

var reconciliationJob = builder
    .AddProject<Projects.Web>("document-analysis-reconciliation", launchProfileName: null)
    .WithReference(databaseServer)
    .WithReference(analysisQueues)
    .WithArgs("--reconcile-document-analysis")
    .PublishAsScheduledAzureContainerAppJob(
        "0 0 * * *",
        (infra, job) =>
        {
            if (string.IsNullOrWhiteSpace(webIdentityResourceId))
            {
                job.Identity.ManagedServiceIdentityType = ManagedServiceIdentityType.SystemAssigned;
                return;
            }

            job.Identity.ManagedServiceIdentityType = ManagedServiceIdentityType.UserAssigned;
        }
    );

if (!string.IsNullOrWhiteSpace(webIdentityClientId))
    reconciliationJob.WithEnvironment("AZURE_CLIENT_ID", webIdentityClientId);

builder.Build().Run();
