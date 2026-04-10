// ─────────────────────────────────────────────────────────────────────────────
// modules/serviceBus.bicep
// Service Bus Namespace + Topic + Subscription.
// Grants the Managed Identity the "Azure Service Bus Data Receiver" role.
// ─────────────────────────────────────────────────────────────────────────────

param namespaceName string
param location string
param topicName string
param subscriptionName string
param maxDeliveryCount int = 5
param managedIdentityPrincipalId string
param tags object = {}

resource namespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: namespaceName
  location: location
  tags: tags
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
}

resource topic 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  parent: namespace
  name: topicName
  properties: {
    defaultMessageTimeToLive: 'P14D'
    requiresDuplicateDetection: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
  }
}

resource subscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: topic
  name: subscriptionName
  properties: {
    maxDeliveryCount: maxDeliveryCount
    deadLetteringOnMessageExpiration: true
    lockDuration: 'PT5M'
  }
}

// Azure Service Bus Data Receiver built-in role
var serviceBusDataReceiverRoleId = '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'

resource sbRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(namespace.id, managedIdentityPrincipalId, serviceBusDataReceiverRoleId)
  scope: namespace
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      serviceBusDataReceiverRoleId
    )
    principalId: managedIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output namespaceHostname string = '${namespaceName}.servicebus.windows.net'
output topicName string = topic.name
output subscriptionName string = subscription.name
