@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param aca_env_outputs_azure_container_apps_environment_id string
param webapi_containerimage string
param webapi_identity_outputs_id string
param webapi_containerport string
param dbserver_outputs_sqlserverfqdn string
param storage_outputs_blobendpoint string
param webapi_identity_outputs_clientid string
param aca_env_outputs_azure_container_registry_endpoint string
param aca_env_outputs_azure_container_registry_managed_identity_id string

@secure()
param sql_admin_password string

resource webapi 'Microsoft.App/containerApps@2025-10-02-preview' = {
  name: 'webapi'
  location: location
  properties: {
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: int(webapi_containerport)
        transport: 'http'
      }
      registries: [
        {
          server: aca_env_outputs_azure_container_registry_endpoint
          identity: aca_env_outputs_azure_container_registry_managed_identity_id
        }
      ]
      runtime: {
        dotnet: {
          autoConfigureDataProtection: true
        }
      }
    }
    environmentId: aca_env_outputs_azure_container_apps_environment_id
    template: {
      containers: [
        {
          image: webapi_containerimage
          name: 'webapi'
          env: [
            {
              name: 'OTEL_DOTNET_EXPERIMENTAL_OTLP_RETRY'
              value: 'in_memory'
            }
            {
              name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED'
              value: 'true'
            }
            {
              name: 'HTTP_PORTS'
              value: webapi_containerport
            }
            {
              name: 'ConnectionStrings__ArchivaDb'
              value: 'Server=tcp:${dbserver_outputs_sqlserverfqdn},1433;Initial Catalog=ArchivaDb;User ID=archiva-admin;Password=${sql_admin_password};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
            }
            {
              name: 'ConnectionStrings__blobs'
              value: storage_outputs_blobendpoint
            }
            {
              name: 'BLOBS_URI'
              value: storage_outputs_blobendpoint
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: webapi_identity_outputs_clientid
            }
            {
              name: 'AZURE_TOKEN_CREDENTIALS'
              value: 'ManagedIdentityCredential'
            }
            {
              name: 'AzureAd__TenantId'
              value: 'bbb41b2b-8961-4881-b480-2268de4887ed'
            }
            {
              name: 'AzureAd__ClientId'
              value: '1274d0e7-b545-4dcc-8c4d-005dee797414'
            }
            {
              name: 'AzureAd__Audience'
              value: '1274d0e7-b545-4dcc-8c4d-005dee797414'
            }
            {
              name: 'AllowedOrigins__0'
              value: 'https://your-static-web-app-url.azurestaticapps.net'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
      }
    }
  }
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${webapi_identity_outputs_id}': {}
      '${aca_env_outputs_azure_container_registry_managed_identity_id}': {}
    }
  }
}
