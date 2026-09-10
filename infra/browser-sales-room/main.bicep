targetScope = 'resourceGroup'

@minLength(3)
param namePrefix string
param location string = 'swedencentral'
@secure()
param packageUri string
param keyVaultName string
param sqlSecretName string = 'browser-room-sql'
param redisSecretName string = 'browser-room-redis'
param liveKitUrlSecretName string = 'browser-room-livekit-url'
param liveKitApiKeySecretName string = 'browser-room-livekit-api-key'
param liveKitApiSecretSecretName string = 'browser-room-livekit-api-secret'
param openAiApiKeySecretName string = 'browser-room-openai-api-key'
param instanceSku string
@minValue(2)
@maxValue(20)
param instanceCount int
@minValue(1)
@maxValue(10000)
param maximumActiveAgentsGlobal int
@minValue(1)
@maxValue(100)
param maximumActiveAgentsPerCompany int
@minValue(1)
param maximumSessionMinutes int
@minValue(1)
param maximumInputAudioSeconds int
@minValue(1)
param maximumOutputAudioSeconds int
param maximumInputTokenCostPerMillionUsd string
param maximumOutputTokenCostPerMillionUsd string
param transcriptionCostPerMinuteUsd string
param maximumSpendPerCallUsd string
param maximumMonthlySpendPerCompanyUsd string
@description('UTC timestamp for the approved provider-rate review. The app fails readiness if it is stale.')
param providerRateCheckedUtc string
@minLength(3)
param providerRateCardReference string
@description('Keep false until readiness and rollout evidence are approved.')
param browserAdmissionEnabled bool = false
@description('Keep false until browser admission is enabled.')
param browserAgentEnabled bool = false
@description('Keep true during initial deployment and emergency response.')
param emergencyDisabled bool = true
@description('Keep true until the instance pool is ready to accept new browser work.')
param drainEnabled bool = true

var compact = toLower(replace(namePrefix, '-', ''))
var appName = take('${compact}-browser-sales', 60)
var vaultSecretBase = '${vault.properties.vaultUri}secrets'

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = { name: keyVaultName }

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${namePrefix}-browser-sales-id'
  location: location
}

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${namePrefix}-browser-sales-law'
  location: location
  properties: {
    retentionInDays: 30
    features: { enableLogAccessUsingOnlyResourcePermissions: true }
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${namePrefix}-browser-sales-ai'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    DisableLocalAuth: true
  }
}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${namePrefix}-browser-sales-plan'
  location: location
  kind: 'windows'
  sku: { name: instanceSku, capacity: instanceCount }
  properties: { reserved: false, zoneRedundant: false }
}

resource app 'Microsoft.Web/sites@2023-12-01' = {
  name: appName
  location: location
  kind: 'app'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    keyVaultReferenceIdentity: identity.id
    siteConfig: {
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      webSocketsEnabled: true
      healthCheckPath: '/health/ready'
      appSettings: [
        { name: 'WEBSITE_RUN_FROM_PACKAGE', value: packageUri }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
        { name: 'ConnectionStrings__VirtualCompanyDb', value: '@Microsoft.KeyVault(SecretUri=${vaultSecretBase}/${sqlSecretName})' }
        { name: 'Observability__Redis__ConnectionString', value: '@Microsoft.KeyVault(SecretUri=${vaultSecretBase}/${redisSecretName})' }
        { name: 'SalesBrowserRoom__Url', value: '@Microsoft.KeyVault(SecretUri=${vaultSecretBase}/${liveKitUrlSecretName})' }
        { name: 'SalesBrowserRoom__ApiKey', value: '@Microsoft.KeyVault(SecretUri=${vaultSecretBase}/${liveKitApiKeySecretName})' }
        { name: 'SalesBrowserRoom__ApiSecret', value: '@Microsoft.KeyVault(SecretUri=${vaultSecretBase}/${liveKitApiSecretSecretName})' }
        { name: 'SharedRealtimeAgent__ApiKey', value: '@Microsoft.KeyVault(SecretUri=${vaultSecretBase}/${openAiApiKeySecretName})' }
        { name: 'SalesBrowserRoom__Enabled', value: string(browserAdmissionEnabled) }
        { name: 'SalesBrowserRoom__Lifecycle__Enabled', value: string(browserAdmissionEnabled) }
        { name: 'SalesBrowserRoom__Lifecycle__DrainEnabled', value: string(drainEnabled) }
        { name: 'SalesRoomAgent__Enabled', value: string(browserAgentEnabled) }
        { name: 'SalesRoomAgent__EmergencyDisabled', value: string(emergencyDisabled) }
        { name: 'SalesRoomAgent__DrainEnabled', value: string(drainEnabled) }
        { name: 'SalesRoomAgent__MaximumActiveAgentsGlobal', value: string(maximumActiveAgentsGlobal) }
        { name: 'SalesRoomAgent__MaximumActiveAgentsPerCompany', value: string(maximumActiveAgentsPerCompany) }
        { name: 'SalesRoomAgent__MaximumSessionMinutes', value: string(maximumSessionMinutes) }
        { name: 'SalesRoomAgent__MaximumInputAudioSeconds', value: string(maximumInputAudioSeconds) }
        { name: 'SalesRoomAgent__MaximumOutputAudioSeconds', value: string(maximumOutputAudioSeconds) }
        { name: 'SalesRoomAgent__MaximumInputTokenCostPerMillionUsd', value: maximumInputTokenCostPerMillionUsd }
        { name: 'SalesRoomAgent__MaximumOutputTokenCostPerMillionUsd', value: maximumOutputTokenCostPerMillionUsd }
        { name: 'SalesRoomAgent__TranscriptionCostPerMinuteUsd', value: transcriptionCostPerMinuteUsd }
        { name: 'SalesRoomAgent__MaximumSpendPerCallUsd', value: maximumSpendPerCallUsd }
        { name: 'SalesRoomAgent__MaximumMonthlySpendPerCompanyUsd', value: maximumMonthlySpendPerCompanyUsd }
        { name: 'SalesRoomAgent__CostCurrency', value: 'USD' }
        { name: 'SalesRoomAgent__ProviderRateCheckedUtc', value: providerRateCheckedUtc }
        { name: 'SalesRoomAgent__ProviderRateMaximumAgeDays', value: '31' }
        { name: 'SalesRoomAgent__ProviderRateCardReference', value: providerRateCardReference }
      ]
    }
  }
}

resource vaultReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, identity.id, 'browser-sales-secrets')
  scope: vault
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
  }
}

resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'browser-sales-diagnostics'
  scope: app
  properties: {
    workspaceId: workspace.id
    logs: [
      { category: 'AppServiceHTTPLogs', enabled: true }
      { category: 'AppServiceConsoleLogs', enabled: true }
      { category: 'AppServicePlatformLogs', enabled: true }
    ]
    metrics: [{ category: 'AllMetrics', enabled: true }]
  }
}

resource availability 'Microsoft.Insights/webtests@2022-06-15' = {
  name: '${namePrefix}-browser-sales-ready'
  location: 'global'
  kind: 'ping'
  tags: { 'hidden-link:${insights.id}': 'Resource' }
  properties: {
    Name: '${namePrefix}-browser-sales-ready'
    SyntheticMonitorId: '${namePrefix}-browser-sales-ready'
    Enabled: true
    Frequency: 300
    Timeout: 30
    Kind: 'ping'
    RetryEnabled: true
    Locations: [{ Id: 'emea-se-sto-edge' }]
    Configuration: { WebTest: '<WebTest Name="browser-sales-ready" Enabled="True"><Items><Request Method="GET" Url="https://${app.properties.defaultHostName}/health/ready" ThinkTime="0" Timeout="30" ParseDependentRequests="False" FollowRedirects="True" RecordResult="True" Cache="False" /></Items></WebTest>' }
  }
}

output appName string = app.name
output appResourceId string = app.id
output readinessUrl string = 'https://${app.properties.defaultHostName}/health/ready'
output managedIdentityClientId string = identity.properties.clientId
output initialControlState object = {
  browserAdmissionEnabled: browserAdmissionEnabled
  browserAgentEnabled: browserAgentEnabled
  emergencyDisabled: emergencyDisabled
  drainEnabled: drainEnabled
}
