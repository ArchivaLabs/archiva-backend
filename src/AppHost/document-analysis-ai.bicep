@description('Name of the existing Document Intelligence resource in this resource group.')
param documentIntelligenceName string = 'archiva-docintel-ma7391'

@description('Name of the existing Microsoft Foundry resource in this resource group.')
param foundryResourceName string = 'archiva-foundry-ma7391'

@description('Deployment name used by the API and Azure OpenAI client.')
param openAiDeploymentName string = 'gpt-5.4-mini'

resource documentIntelligence 'Microsoft.CognitiveServices/accounts@2024-10-01' existing = {
  name: documentIntelligenceName
}

resource foundry 'Microsoft.CognitiveServices/accounts@2024-10-01' existing = {
  name: foundryResourceName
}

resource summaryDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' existing = {
  parent: foundry
  name: openAiDeploymentName
}

output DocumentIntelligence__Endpoint string = documentIntelligence.properties.endpoint
output AzureOpenAI__Endpoint string = 'https://${foundry.name}.openai.azure.com/'
output AzureOpenAI__DeploymentName string = summaryDeployment.name
