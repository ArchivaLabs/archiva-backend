// HAND-TUNED — do not run `azd infra gen` (or `azd infra synth`) against this file.
// Regeneration silently reverts the Phase 2 cost work: it upgrades the SKU from
// GP_S_Gen5_1 to GP_S_Gen5_2 (double the vCores), drops autoPauseDelay and
// minCapacity so the database stops pausing when idle, and switches the server to
// azureADOnlyAuthentication with a generated managed-identity admin, which
// disables the SQL login. Verified by regenerating and diffing on 2026-09-08.
// Edit the values below by hand instead.

@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

@description('SQL Server administrator username')
param sqlAdminUsername string = 'archiva-admin'

@secure()
@description('SQL Server administrator password')
param sqlAdminPassword string

resource dbserver 'Microsoft.Sql/servers@2023-08-01' = {
  name: take('dbserver-${uniqueString(resourceGroup().id)}', 63)
  location: location
  properties: {
    administratorLogin: sqlAdminUsername
    administratorLoginPassword: sqlAdminPassword
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
    minCapacity: json('0.5')
  }
  sku: {
    name: 'GP_S_Gen5_1'
  }
  parent: dbserver
}

output sqlServerFqdn string = dbserver.properties.fullyQualifiedDomainName
output name string = dbserver.name
output id string = dbserver.id
output sqlServerAdminName string = sqlAdminUsername
