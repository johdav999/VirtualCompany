targetScope = 'subscription'

param deploymentLocation string
param resourceGroupName string
param namePrefix string
param environmentName string
param vmSku string = 'Standard_D4s_v5'
param instanceCount int = 2
param minimumInstances int = 2
param maximumInstances int = 6
param adminUsername string
@secure()
param adminPassword string
@secure()
param apiPackageUri string
@secure()
param bootstrapScriptUri string
param apiPackageSha256 string
param keyVaultName string
param mediaCertificateSecretName string
param teamsConfigurationSecretName string = 'teams-media-host-configuration'
param dnsZoneName string
param serviceDnsRecordName string = 'teams-media'
param callbackDnsLabel string
param alertEmailAddresses array = []
@minValue(100)
param monthlyBudgetAmount int = 2500
param budgetStartDate string

resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: deploymentLocation
}

module mediaRuntime './main.bicep' = {
  name: 'teams-media-${environmentName}'
  scope: resourceGroup
  params: {
    namePrefix: namePrefix
    location: deploymentLocation
    environmentName: environmentName
    vmSku: vmSku
    instanceCount: instanceCount
    minimumInstances: minimumInstances
    maximumInstances: maximumInstances
    adminUsername: adminUsername
    adminPassword: adminPassword
    apiPackageUri: apiPackageUri
    bootstrapScriptUri: bootstrapScriptUri
    apiPackageSha256: apiPackageSha256
    keyVaultName: keyVaultName
    mediaCertificateSecretName: mediaCertificateSecretName
    teamsConfigurationSecretName: teamsConfigurationSecretName
    dnsZoneName: dnsZoneName
    serviceDnsRecordName: serviceDnsRecordName
    callbackDnsLabel: callbackDnsLabel
    alertEmailAddresses: alertEmailAddresses
  }
}

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: '${namePrefix}-${environmentName}-teams-media-budget'
  properties: {
    amount: monthlyBudgetAmount
    category: 'Cost'
    timeGrain: 'Monthly'
    timePeriod: { startDate: budgetStartDate }
    filter: { dimensions: { name: 'ResourceGroupName', operator: 'In', values: [ resourceGroupName ] } }
    notifications: {
      forecast80: { enabled: true, operator: 'GreaterThan', threshold: 80, thresholdType: 'Forecasted', contactEmails: alertEmailAddresses, contactRoles: [ 'Owner' ], contactGroups: [] }
      actual100: { enabled: true, operator: 'GreaterThan', threshold: 100, thresholdType: 'Actual', contactEmails: alertEmailAddresses, contactRoles: [ 'Owner' ], contactGroups: [] }
    }
  }
}

output callbackFqdn string = mediaRuntime.outputs.callbackFqdn
output vmssName string = mediaRuntime.outputs.mediaHostVmssName
output validationState string = mediaRuntime.outputs.deploymentState
