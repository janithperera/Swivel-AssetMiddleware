// ─────────────────────────────────────────────────────────────────────────────
// main.bicep
// Orchestrates all modules for the AssetMiddleware deployment.
// Resources: Managed Identity → Key Vault → Service Bus → App Service
// ─────────────────────────────────────────────────────────────────────────────

targetScope = 'resourceGroup'

@description('Deployment environment (dev, staging, prod).')
@allowed(['dev', 'staging', 'prod'])
param environment string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Base name prefix for all resources.')
param baseName string = 'asset-middleware'

@description('Service Bus topic name.')
param serviceBusTopicName string = 'fieldops-events'

@description('Service Bus subscription name.')
param serviceBusSubscriptionName string = 'asset-middleware'

@description('AssetHub API base URL.')
param assetHubBaseUrl string

@description('AssetHub company ID.')
param assetHubCompanyId string

// ── Derived names ──────────────────────────────────────────────────────────
var suffix = '${baseName}-${environment}'
var identityName = 'id-${suffix}'
var serviceBusNamespaceName = 'sb-${suffix}'
var keyVaultName = 'kv-${take(suffix, 17)}'    // KV name max 24 chars
var appPlanName = 'plan-${suffix}'
var appName = 'app-${suffix}'

var tags = {
  environment: environment
  project: 'asset-middleware'
  managedBy: 'bicep'
}

// ── 1. Managed Identity ────────────────────────────────────────────────────
module identity 'modules/managedIdentity.bicep' = {
  name: 'identity'
  params: {
    name: identityName
    location: location
    tags: tags
  }
}

// ── 2. Key Vault ──────────────────────────────────────────────────────────
module keyVault 'modules/keyVault.bicep' = {
  name: 'keyVault'
  params: {
    vaultName: keyVaultName
    location: location
    managedIdentityPrincipalId: identity.outputs.principalId
    tags: tags
  }
  // identity dependency inferred from principalId output reference
}

// ── 3. Service Bus ─────────────────────────────────────────
module serviceBus 'modules/serviceBus.bicep' = {
  name: 'serviceBus'
  params: {
    namespaceName: serviceBusNamespaceName
    location: location
    topicName: serviceBusTopicName
    subscriptionName: serviceBusSubscriptionName
    managedIdentityPrincipalId: identity.outputs.principalId
    tags: tags
  }
  // identity dependency inferred from principalId output reference
}

// ── 4. App Service ─────────────────────────────────────────────────────────
module appService 'modules/appService.bicep' = {
  name: 'appService'
  params: {
    appName: appName
    planName: appPlanName
    location: location
    managedIdentityId: identity.outputs.identityId
    managedIdentityClientId: identity.outputs.clientId
    keyVaultUri: keyVault.outputs.vaultUri
    serviceBusNamespace: serviceBusNamespaceName
    serviceBusTopicName: serviceBusTopicName
    serviceBusSubscriptionName: serviceBusSubscriptionName
    assetHubBaseUrl: assetHubBaseUrl
    assetHubCompanyId: assetHubCompanyId
    tags: tags
  }
  // keyVault and serviceBus dependencies inferred from output references
}

// ── Outputs ────────────────────────────────────────────────────────────────
output appUrl string = appService.outputs.appUrl
output keyVaultName string = keyVault.outputs.vaultName
output serviceBusNamespace string = serviceBus.outputs.namespaceHostname
