// ─────────────────────────────────────────────────────────────────────────────
// parameters/dev.bicepparam
// Development environment parameter values.
// ─────────────────────────────────────────────────────────────────────────────

using '../main.bicep'

param environment = 'dev'
param location = 'australiaeast'
param baseName = 'asset-middleware'
param serviceBusTopicName = 'fieldops-events'
param serviceBusSubscriptionName = 'asset-middleware'
param assetHubBaseUrl = 'https://api.assethub.example.com'
param assetHubCompanyId = 'PLACEHOLDER'
