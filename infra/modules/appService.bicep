// ─────────────────────────────────────────────────────────────────────────────
// modules/appService.bicep
// App Service Plan + Web App to host the AssetMiddleware API.
// Assigns the Managed Identity. App settings reference Key Vault secrets.
// ─────────────────────────────────────────────────────────────────────────────

param appName string
param planName string
param location string
param managedIdentityId string
param managedIdentityClientId string
param keyVaultUri string
param serviceBusNamespace string
param serviceBusTopicName string
param serviceBusSubscriptionName string
param assetHubBaseUrl string
param assetHubCompanyId string
param tags object = {}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  tags: tags
  sku: {
    name: 'B2'
    tier: 'Basic'
    size: 'B2'
    capacity: 1
  }
  properties: {
    reserved: true   // Linux
  }
  kind: 'linux'
}

resource app 'Microsoft.Web/sites@2023-12-01' = {
  name: appName
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentityId}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      appSettings: [
        // ── Managed Identity client ID ────────────────────────────────────
        {
          name: 'AZURE_CLIENT_ID'
          value: managedIdentityClientId
        }
        // ── Service Bus ───────────────────────────────────────────────────
        {
          name: 'ServiceBus__FullyQualifiedNamespace'
          value: '${serviceBusNamespace}.servicebus.windows.net'
        }
        {
          name: 'ServiceBus__TopicName'
          value: serviceBusTopicName
        }
        {
          name: 'ServiceBus__SubscriptionName'
          value: serviceBusSubscriptionName
        }
        {
          name: 'ServiceBus__MaxConcurrentCalls'
          value: '5'
        }
        // ── AssetHub ──────────────────────────────────────────────────────
        {
          name: 'AssetHub__BaseUrl'
          value: assetHubBaseUrl
        }
        {
          name: 'AssetHub__CompanyId'
          value: assetHubCompanyId
        }
        // Secrets from Key Vault — format: @Microsoft.KeyVault(SecretUri=...)
        {
          name: 'AssetHub__ClientId'
          value: '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/AssetHubClientId/)'
        }
        {
          name: 'AssetHub__ClientSecret'
          value: '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/AssetHubClientSecret/)'
        }
        // ── Resilience ────────────────────────────────────────────────────
        {
          name: 'Resilience__Retry__MaxRetryAttempts'
          value: '3'
        }
        {
          name: 'Resilience__CircuitBreaker__FailureRatio'
          value: '0.5'
        }
        {
          name: 'Resilience__CircuitBreaker__BreakDurationSeconds'
          value: '60'
        }
      ]
    }
  }
}

output appUrl string = 'https://${app.properties.defaultHostName}'
output appName string = app.name
