targetScope = 'resourceGroup'
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = { name: 'vco-dev-media-id' }
resource storage 'Microsoft.Storage/storageAccounts@2025-06-01' existing = { name: 'vcoteamsetr7qjsd' }
resource files 'Microsoft.Storage/storageAccounts/fileServices@2023-05-01' existing = { parent: storage, name: 'default' }
resource shares 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-05-01' existing = [for n in [ 'keys', 'documents' ]: { parent: files, name: n }]
resource access 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (n, i) in [ 'keys', 'documents' ]: {
  name: guid(shares[i].id, identity.id, 'Storage File Data SMB MI Admin')
  scope: shares[i]
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'a235d3ee-5935-4cfb-8cc5-a3303ad5995e')
  }
}]
