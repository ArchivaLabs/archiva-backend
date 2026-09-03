@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

resource sqlServerAdminManagedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: take('dbserver-admin-${uniqueString(resourceGroup().id)}', 63)
  location: location
}

resource dbserver 'Microsoft.Sql/servers@2023-08-01' = {
  name: take('dbserver-${uniqueString(resourceGroup().id)}', 63)
  location: location
  properties: {
    administrators: {
      administratorType: 'ActiveDirectory'
      login: sqlServerAdminManagedIdentity.name
      sid: sqlServerAdminManagedIdentity.properties.principalId
      tenantId: subscription().tenantId
      azureADOnlyAuthentication: true
    }
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    version: '12.0'
  }
  tags: {
    'aspire-resource-name': 'dbserver'
  }
}

resource sqlFirewallRule_AllowAllAzureIps 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  name: 'AllowAllAzureIps'
  properties: {
    endIpAddress: '0.0.0.0'
    startIpAddress: '0.0.0.0'
  }
  parent: dbserver
}

resource ArchivaDb 'Microsoft.Sql/servers/databases@2023-08-01' = {
  name: 'ArchivaDb'
  location: location
  properties: {
    freeLimitExhaustionBehavior: 'AutoPause'
    useFreeLimit: true
    autoPauseDelay: 60
    minCapacity: '0.5'
  }
  sku: {
    name: 'GP_S_Gen5_1'
  }
  parent: dbserver
}

output sqlServerFqdn string = dbserver.properties.fullyQualifiedDomainName

output name string = dbserver.name

output id string = dbserver.id

output sqlServerAdminName string = dbserver.properties.administrators.login