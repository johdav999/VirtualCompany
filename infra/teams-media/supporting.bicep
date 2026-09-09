targetScope = 'resourceGroup'

// Supporting services only. Host deployment connects its VNet to this services VNet.
param location string = resourceGroup().location
param namePrefix string = 'vco'
param environmentName string = 'dev'
param sqlAdministratorObjectId string
param sqlAdministratorLogin string
var suffix = '${namePrefix}-${environmentName}'
var uniqueSuffix = take(uniqueString(subscription().id, resourceGroup().id), 8)
var tags = { application: 'VirtualCompany', environment: environmentName, purpose: 'TeamsMarketAssistant' }

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${suffix}-media-id'
  location: location
  tags: tags
}
resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: '${suffix}-services-vnet'
  location: location
  tags: tags
  properties: {
    addressSpace: { addressPrefixes: [ '10.43.0.0/16' ] }
    subnets: [
      {
        name: 'private-endpoints'
        properties: { addressPrefix: '10.43.1.0/24', privateEndpointNetworkPolicies: 'Disabled' }
      }
    ]
  }
}
resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: '${suffix}-kv-${uniqueSuffix}'
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Disabled'
    networkAcls: { bypass: 'AzureServices', defaultAction: 'Deny' }
    accessPolicies: []
  }
}
resource secretsReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, identity.id, 'Key Vault Secrets User')
  scope: vault
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
  }
}
resource sql 'Microsoft.Sql/servers@2023-08-01' = {
  name: '${suffix}-sql-${uniqueSuffix}'
  location: location
  tags: tags
  properties: {
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'User'
      login: sqlAdministratorLogin
      sid: sqlAdministratorObjectId
      tenantId: tenant().tenantId
      azureADOnlyAuthentication: true
    }
  }
}
resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sql
  name: 'VirtualCompany'
  location: location
  tags: tags
  sku: { name: 'Basic', tier: 'Basic', capacity: 5 }
  properties: {
    maxSizeBytes: 2147483648
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    zoneRedundant: false
    requestedBackupStorageRedundancy: 'Local'
  }
}
resource storage 'Microsoft.Storage/storageAccounts@2025-06-01' = {
  name: 'vcoteams${uniqueSuffix}'
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    // Verified against ARM 2025-06-01; not yet in this Bicep version's type definitions.
    azureFilesIdentityBasedAuthentication: any({ directoryServiceOptions: 'None', smbOAuthSettings: { isSmbOAuthEnabled: true } })
    publicNetworkAccess: 'Disabled'
    networkAcls: { bypass: 'AzureServices', defaultAction: 'Deny' }
  }
}
resource blobs 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    deleteRetentionPolicy: { enabled: true, days: 7 }
    containerDeleteRetentionPolicy: { enabled: true, days: 7 }
  }
}
resource artifacts 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobs
  name: 'deployment-artifacts'
  properties: { publicAccess: 'None' }
}
resource files 'Microsoft.Storage/storageAccounts/fileServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: { shareDeleteRetentionPolicy: { enabled: true, days: 7 } }
}
resource shares 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-05-01' = [for shareName in [ 'keys', 'documents' ]: {
  parent: files
  name: shareName
  properties: { accessTier: 'TransactionOptimized', enabledProtocols: 'SMB', shareQuota: 100 }
}]
resource redis 'Microsoft.Cache/redisEnterprise@2025-07-01' = {
  name: '${suffix}-redis-${uniqueSuffix}'
  location: location
  tags: tags
  sku: { name: 'Balanced_B0' }
  properties: { minimumTlsVersion: '1.2', highAvailability: 'Disabled', publicNetworkAccess: 'Disabled' }
}
resource redisDatabase 'Microsoft.Cache/redisEnterprise/databases@2025-07-01' = {
  parent: redis
  name: 'default'
  properties: {
    clientProtocol: 'Encrypted'
    clusteringPolicy: 'NoCluster'
    evictionPolicy: 'NoEviction'
    accessKeysAuthentication: 'Enabled'
  }
}
resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${suffix}-logs'
  location: location
  tags: tags
  properties: { retentionInDays: 30, features: { enableLogAccessUsingOnlyResourcePermissions: true } }
}
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${suffix}-appi'
  location: location
  tags: tags
  kind: 'web'
  properties: { Application_Type: 'web', WorkspaceResourceId: workspace.id, DisableLocalAuth: true }
}

var endpoints = [
  { name: 'sql', resourceId: sql.id, groupId: 'sqlServer', zone: 'privatelink${environment().suffixes.sqlServerHostname}' }
  { name: 'redis', resourceId: redis.id, groupId: 'redisEnterprise', zone: 'privatelink.redis.azure.net' }
  { name: 'vault', resourceId: vault.id, groupId: 'vault', zone: 'privatelink.vaultcore.azure.net' }
  { name: 'blob', resourceId: storage.id, groupId: 'blob', zone: 'privatelink.blob.${environment().suffixes.storage}' }
  { name: 'file', resourceId: storage.id, groupId: 'file', zone: 'privatelink.file.${environment().suffixes.storage}' }
]
resource zones 'Microsoft.Network/privateDnsZones@2024-06-01' = [for endpoint in endpoints: {
  name: endpoint.zone
  location: 'global'
  tags: tags
}]
resource links 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = [for (endpoint, i) in endpoints: {
  parent: zones[i]
  name: 'services'
  location: 'global'
  properties: { registrationEnabled: false, virtualNetwork: { id: vnet.id } }
}]
resource privateEndpoints 'Microsoft.Network/privateEndpoints@2024-05-01' = [for endpoint in endpoints: {
  name: '${suffix}-${endpoint.name}-pe'
  location: location
  tags: tags
  properties: {
    subnet: { id: resourceId('Microsoft.Network/virtualNetworks/subnets', vnet.name, 'private-endpoints') }
    privateLinkServiceConnections: [
      {
        name: endpoint.name
        properties: { privateLinkServiceId: endpoint.resourceId, groupIds: [ endpoint.groupId ] }
      }
    ]
  }
  dependsOn: [ redisDatabase ]
}]
resource zoneGroups 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = [for (endpoint, i) in endpoints: {
  parent: privateEndpoints[i]
  name: 'default'
  properties: { privateDnsZoneConfigs: [ { name: endpoint.name, properties: { privateDnsZoneId: zones[i].id } } ] }
}]
output keyVaultName string = vault.name
output sqlServerName string = sql.name
output databaseName string = database.name
output storageAccountName string = storage.name
output redisName string = redis.name
output servicesVnetId string = vnet.id
output managedIdentityClientId string = identity.properties.clientId

resource shareIdentityAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (shareName, i) in [ 'keys', 'documents' ]: {
  name: guid(shares[i].id, identity.id, 'Storage File Data SMB MI Admin')
  scope: shares[i]
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'a235d3ee-5935-4cfb-8cc5-a3303ad5995e')
  }
}]
