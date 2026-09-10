targetScope = 'resourceGroup'

@description('Explicit acknowledgement that this resource group is an isolated disposable benchmark target.')
@allowed([true])
param benchmarkAuthorized bool

@minLength(3)
param namePrefix string
param location string = 'swedencentral'
@secure()
param packageUri string
param keyVaultName string
@minLength(1)
param loadGeneratorResourceId string
param instanceSku string = 'P1v3'
@minValue(1)
@maxValue(5)
param instanceCount int = 1

var compact = toLower(replace(namePrefix, '-', ''))
var appName = take('${compact}-salesbench', 60)

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${namePrefix}-sales-benchmark-id'
  location: location
}

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${namePrefix}-sales-benchmark-law'
  location: location
  properties: {
    retentionInDays: 30
    features: { enableLogAccessUsingOnlyResourcePermissions: true }
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${namePrefix}-sales-benchmark-ai'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    DisableLocalAuth: true
  }
}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${namePrefix}-sales-benchmark-plan'
  location: location
  sku: {
    name: instanceSku
    capacity: instanceCount
  }
  kind: 'windows'
  properties: {
    reserved: false
    zoneRedundant: false
  }
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
    publicNetworkAccess: 'Enabled'
    siteConfig: {
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      webSocketsEnabled: true
      healthCheckPath: '/health'
      appSettings: [
        { name: 'WEBSITE_RUN_FROM_PACKAGE', value: packageUri }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
        { name: 'AzureKeyVault__Uri', value: vault.properties.vaultUri }
        { name: 'AzureKeyVault__ManagedIdentityClientId', value: identity.properties.clientId }
        { name: 'SalesBrowserRoom__Enabled', value: 'true' }
        { name: 'SalesBrowserRoom__Lifecycle__Enabled', value: 'true' }
        { name: 'SalesRoomAgent__Enabled', value: 'true' }
        { name: 'TeamsPresenter__Enabled', value: 'false' }
        { name: 'TeamsMediaHost__Enabled', value: 'false' }
        { name: 'VC_BENCHMARK_ISOLATED_TARGET', value: 'true' }
        { name: 'VC_BENCHMARK_AUTHORIZED', value: string(benchmarkAuthorized) }
      ]
    }
  }
}

resource vaultReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, identity.id, 'browser-benchmark-secrets')
  scope: vault
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
  }
}

resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'sales-benchmark-diagnostics'
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

resource fixedInstanceAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${namePrefix}-benchmark-cpu-stop'
  location: 'global'
  properties: {
    description: 'The isolated benchmark target exceeded the CPU stop threshold; do not increase load.'
    severity: 1
    enabled: true
    scopes: [app.id]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    autoMitigate: true
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [{
        name: 'cpu'
        metricName: 'CpuTime'
        metricNamespace: 'Microsoft.Web/sites'
        operator: 'GreaterThan'
        threshold: 240
        timeAggregation: 'Total'
        criterionType: 'StaticThresholdCriterion'
      }]
    }
    actions: []
  }
}

output targetAppName string = app.name
output targetResourceId string = app.id
output applicationInsightsName string = insights.name
output logAnalyticsWorkspaceName string = workspace.name
output managedIdentityClientId string = identity.properties.clientId
output runtime string = 'windows-app-service-self-contained-win-x64'
output instanceConfiguration object = { sku: instanceSku, count: instanceCount }
output separateLoadGeneratorResourceId string = loadGeneratorResourceId
output cleanupScope string = resourceGroup().id
