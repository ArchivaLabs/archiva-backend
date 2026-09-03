@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

resource webapi_identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: take('webapi_identity-${uniqueString(resourceGroup().id)}', 128)
  location: location
}

output id string = webapi_identity.id

output clientId string = webapi_identity.properties.clientId

output principalId string = webapi_identity.properties.principalId

output principalName string = webapi_identity.name

output name string = webapi_identity.name