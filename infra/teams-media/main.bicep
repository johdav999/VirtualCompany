targetScope = 'resourceGroup'

@description('Short lowercase prefix used for Azure resource names.')
@minLength(3)
@maxLength(18)
param namePrefix string

param location string = resourceGroup().location
param environmentName string
param vmSku string = 'Standard_D4s_v5'
@minValue(1)
@maxValue(20)
param instanceCount int = 2
@minValue(1)
@maxValue(20)
param minimumInstances int = 2
@minValue(1)
@maxValue(20)
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
param mediaInternalPort int = 8445
param mediaPublicPort int = 8445
param alertEmailAddresses array = []
var workbookData = loadTextContent('workbook.json')

var suffix = toLower('${namePrefix}-${environmentName}')
var vmssName = '${suffix}-media-vmss'
var identityName = '${suffix}-media-id'
var workspaceName = '${suffix}-logs'
var appInsightsName = '${suffix}-appi'
var actionGroupName = '${suffix}-ops'
var serviceFqdn = '${serviceDnsRecordName}.${dnsZoneName}'

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
}

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: '${suffix}-vnet'
  location: location
  properties: {
    addressSpace: { addressPrefixes: [ '10.42.0.0/16' ] }
    subnets: [
      {
        name: 'media-hosts'
        properties: {
          addressPrefix: '10.42.1.0/24'
          networkSecurityGroup: { id: nsg.id }
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
    ]
  }
}

resource nsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' = {
  name: '${suffix}-media-nsg'
  location: location
  properties: {
    securityRules: [
      {
        name: 'allow-teams-callback-https'
        properties: {
          priority: 100
          access: 'Allow'
          direction: 'Inbound'
          protocol: 'Tcp'
          sourceAddressPrefix: 'Internet'
          sourcePortRange: '*'
          destinationAddressPrefix: 'VirtualNetwork'
          destinationPortRange: '8443'
        }
      }
      {
        name: 'allow-teams-media-tcp'
        properties: {
          priority: 110
          access: 'Allow'
          direction: 'Inbound'
          protocol: 'Tcp'
          sourceAddressPrefix: 'Internet'
          sourcePortRange: '*'
          destinationAddressPrefix: 'VirtualNetwork'
          destinationPortRange: string(mediaPublicPort)
        }
      }
      {
        name: 'allow-teams-media-udp'
        properties: {
          priority: 120
          access: 'Allow'
          direction: 'Inbound'
          protocol: 'Udp'
          sourceAddressPrefix: 'Internet'
          sourcePortRange: '*'
          destinationAddressPrefix: 'VirtualNetwork'
          destinationPortRange: string(mediaPublicPort)
        }
      }
      {
        name: 'allow-vnet-sql'
        properties: {
          priority: 200
          access: 'Allow'
          direction: 'Outbound'
          protocol: 'Tcp'
          sourceAddressPrefix: 'VirtualNetwork'
          sourcePortRange: '*'
          destinationAddressPrefix: 'VirtualNetwork'
          destinationPortRange: '1433'
        }
      }
      {
        name: 'allow-vnet-redis-tls'
        properties: {
          priority: 210
          access: 'Allow'
          direction: 'Outbound'
          protocol: 'Tcp'
          sourceAddressPrefix: 'VirtualNetwork'
          sourcePortRange: '*'
          destinationAddressPrefix: 'VirtualNetwork'
          destinationPortRange: '6380'
        }
      }
      {
        name: 'deny-management-inbound'
        properties: {
          priority: 4000
          access: 'Deny'
          direction: 'Inbound'
          protocol: '*'
          sourceAddressPrefix: 'Internet'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRanges: [ '22', '3389', '5985', '5986' ]
        }
      }
    ]
  }
}

resource callbackPublicIp 'Microsoft.Network/publicIPAddresses@2024-05-01' = {
  name: '${suffix}-callback-pip'
  location: location
  sku: { name: 'Standard' }
  properties: {
    publicIPAllocationMethod: 'Static'
    dnsSettings: { domainNameLabel: callbackDnsLabel }
  }
}

resource dnsZone 'Microsoft.Network/dnsZones@2018-05-01' = {
  name: dnsZoneName
  location: 'global'
}

resource serviceDnsRecord 'Microsoft.Network/dnsZones/A@2018-05-01' = {
  parent: dnsZone
  name: serviceDnsRecordName
  properties: {
    TTL: 60
    ARecords: [ { ipv4Address: callbackPublicIp.properties.ipAddress } ]
  }
}

resource loadBalancer 'Microsoft.Network/loadBalancers@2024-05-01' = {
  name: '${suffix}-callback-lb'
  location: location
  sku: { name: 'Standard' }
  properties: {
    frontendIPConfigurations: [
      { name: 'callback', properties: { publicIPAddress: { id: callbackPublicIp.id } } }
    ]
    backendAddressPools: [ { name: 'media-hosts' } ]
    probes: [
      {
        name: 'media-host-ready'
        properties: {
          protocol: 'Https'
          port: 8443
          // Keep signaling available during bootstrap and drain; application policy admits media.
          requestPath: '/health/media-host/live'
          intervalInSeconds: 10
          numberOfProbes: 2
        }
      }
    ]
    loadBalancingRules: [
      {
        name: 'teams-callback-https'
        properties: {
          protocol: 'Tcp'
          frontendPort: 443
          backendPort: 8443
          enableFloatingIP: false
          idleTimeoutInMinutes: 30
          enableTcpReset: true
          loadDistribution: 'SourceIPProtocol'
          frontendIPConfiguration: { id: resourceId('Microsoft.Network/loadBalancers/frontendIPConfigurations', '${suffix}-callback-lb', 'callback') }
          backendAddressPool: { id: resourceId('Microsoft.Network/loadBalancers/backendAddressPools', '${suffix}-callback-lb', 'media-hosts') }
          probe: { id: resourceId('Microsoft.Network/loadBalancers/probes', '${suffix}-callback-lb', 'media-host-ready') }
        }
      }
    ]
  }
}

resource vmss 'Microsoft.Compute/virtualMachineScaleSets@2024-07-01' = {
  name: vmssName
  location: location
  sku: { name: vmSku, tier: 'Standard', capacity: instanceCount }
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identity.id}': {} } }
  dependsOn: [ keyVaultSecretsUser, appInsightsSecret, serviceDnsRecord ]
  properties: {
    orchestrationMode: 'Uniform'
    overprovision: false
    singlePlacementGroup: false
    upgradePolicy: { mode: 'Manual' }
    automaticRepairsPolicy: { enabled: true, gracePeriod: 'PT30M', repairAction: 'Replace' }
    scaleInPolicy: { rules: [ 'OldestVM' ], forceDeletion: false }
    virtualMachineProfile: {
      osProfile: {
        computerNamePrefix: take(replace(suffix, '-', ''), 9)
        adminUsername: adminUsername
        adminPassword: adminPassword
        windowsConfiguration: {
          provisionVMAgent: true
          enableAutomaticUpdates: true
          patchSettings: { patchMode: 'AutomaticByPlatform', assessmentMode: 'AutomaticByPlatform' }
        }
      }
      storageProfile: {
        imageReference: {
          publisher: 'MicrosoftWindowsServer'
          offer: 'WindowsServer'
          sku: '2022-datacenter-azure-edition'
          version: 'latest'
        }
        osDisk: { createOption: 'FromImage', caching: 'ReadWrite', managedDisk: { storageAccountType: 'Premium_LRS' } }
      }
      networkProfile: {
        healthProbe: { id: resourceId('Microsoft.Network/loadBalancers/probes', loadBalancer.name, 'media-host-ready') }
        networkInterfaceConfigurations: [
          {
            name: 'media-nic'
            properties: {
              primary: true
              enableAcceleratedNetworking: true
              networkSecurityGroup: { id: nsg.id }
              ipConfigurations: [
                {
                  name: 'media-ip'
                  properties: {
                    primary: true
                    subnet: { id: resourceId('Microsoft.Network/virtualNetworks/subnets', vnet.name, 'media-hosts') }
                    loadBalancerBackendAddressPools: [ { id: resourceId('Microsoft.Network/loadBalancers/backendAddressPools', loadBalancer.name, 'media-hosts') } ]
                    publicIPAddressConfiguration: {
                      name: 'instance-public-ip'
                      properties: { idleTimeoutInMinutes: 30 }
                      sku: { name: 'Standard', tier: 'Regional' }
                    }
                  }
                }
              ]
            }
          }
        ]
      }
      extensionProfile: {
        extensions: [
          {
            name: 'bootstrap-teams-media'
            properties: {
              publisher: 'Microsoft.Compute'
              type: 'CustomScriptExtension'
              typeHandlerVersion: '1.10'
              autoUpgradeMinorVersion: true
              settings: {}
              protectedSettings: {
                fileUris: [ bootstrapScriptUri, apiPackageUri ]
                commandToExecute: 'powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy AllSigned -File .\\Install-TeamsMediaHost.ps1 -ApiPackagePath .\\VirtualCompany.Api.zip -ApiPackageSha256 ${apiPackageSha256} -KeyVaultName ${keyVaultName} -CertificateSecretName ${mediaCertificateSecretName} -ConfigurationSecretName ${teamsConfigurationSecretName} -ServiceFqdn ${serviceFqdn} -MediaInternalPort ${mediaInternalPort} -MediaPublicPort ${mediaPublicPort} -ManagedIdentityClientId ${identity.properties.clientId}'
              }
            }
          }
          {
            name: 'AzureMonitorWindowsAgent'
            properties: {
              publisher: 'Microsoft.Azure.Monitor'
              type: 'AzureMonitorWindowsAgent'
              typeHandlerVersion: '1.30'
              autoUpgradeMinorVersion: true
              enableAutomaticUpgrade: true
              settings: { authentication: { managedIdentity: { 'identifier-name': 'mi_res_id', 'identifier-value': identity.id } } }
            }
          }
        ]
      }
    }
  }
}

resource keyVaultSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, identity.id, 'Key Vault Secrets User')
  scope: keyVault
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
  }
}

resource vmContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vmss.id, identity.id, 'VMSS instance protection')
  scope: vmss
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '9980e02c-c2be-4d73-94e8-173b1dc7cf3c')
  }
}

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: workspaceName
  location: location
  properties: { retentionInDays: 30, features: { enableLogAccessUsingOnlyResourcePermissions: true } }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: { Application_Type: 'web', WorkspaceResourceId: workspace.id, DisableLocalAuth: true }
}

resource dataCollectionRule 'Microsoft.Insights/dataCollectionRules@2023-03-11' = {
  name: '${suffix}-media-dcr'
  location: location
  kind: 'Windows'
  properties: {
    dataSources: {
      performanceCounters: [
        {
          name: 'media-host-performance'
          streams: [ 'Microsoft-Perf' ]
          samplingFrequencyInSeconds: 60
          counterSpecifiers: [
            '\\Processor(_Total)\\% Processor Time'
            '\\Memory\\Available MBytes'
            '\\Network Interface(*)\\Bytes Total/sec'
            '\\Process(dotnet*)\\Private Bytes'
          ]
        }
      ]
    }
    destinations: {
      logAnalytics: [ { name: 'operations-workspace', workspaceResourceId: workspace.id } ]
    }
    dataFlows: [ { streams: [ 'Microsoft-Perf' ], destinations: [ 'operations-workspace' ] } ]
  }
}

resource dataCollectionAssociation 'Microsoft.Insights/dataCollectionRuleAssociations@2023-03-11' = {
  name: '${suffix}-media-dcr-association'
  scope: vmss
  properties: { dataCollectionRuleId: dataCollectionRule.id }
}

resource loadBalancerDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: '${suffix}-load-balancer-diagnostics'
  scope: loadBalancer
  properties: {
    workspaceId: workspace.id
    logs: [ { categoryGroup: 'allLogs', enabled: true } ]
    metrics: [ { category: 'AllMetrics', enabled: true } ]
  }
}

resource publicIpDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: '${suffix}-public-ip-diagnostics'
  scope: callbackPublicIp
  properties: {
    workspaceId: workspace.id
    logs: [ { categoryGroup: 'allLogs', enabled: true } ]
    metrics: [ { category: 'AllMetrics', enabled: true } ]
  }
}

resource appInsightsSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'applicationinsights--connectionstring'
  properties: { value: appInsights.properties.ConnectionString }
}

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: actionGroupName
  location: 'global'
  properties: {
    groupShortName: take(replace(namePrefix, '-', ''), 12)
    enabled: true
    emailReceivers: [for (address, index) in alertEmailAddresses: { name: 'operator-${index}', emailAddress: address, useCommonAlertSchema: true }]
  }
}

resource cpuAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${suffix}-cpu-saturation'
  location: 'global'
  properties: {
    description: 'Teams media host CPU exceeds the admission safety threshold.'
    severity: 1
    enabled: true
    scopes: [ vmss.id ]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: { 'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria', allOf: [ { name: 'cpu', metricName: 'Percentage CPU', metricNamespace: 'Microsoft.Compute/virtualMachineScaleSets', operator: 'GreaterThan', threshold: 80, timeAggregation: 'Average', criterionType: 'StaticThresholdCriterion' } ] }
    actions: [ { actionGroupId: actionGroup.id } ]
  }
}

resource networkAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${suffix}-network-saturation'
  location: 'global'
  properties: {
    description: 'Teams media ingress exceeds the measured capacity ceiling.'
    severity: 2
    enabled: true
    scopes: [ vmss.id ]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: { 'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria', allOf: [ { name: 'network', metricName: 'Network In Total', metricNamespace: 'Microsoft.Compute/virtualMachineScaleSets', operator: 'GreaterThan', threshold: 1073741824, timeAggregation: 'Total', criterionType: 'StaticThresholdCriterion' } ] }
    actions: [ { actionGroupId: actionGroup.id } ]
  }
}

var applicationAlertDefinitions = [
  {
    name: 'media-or-callback-failures'
    description: 'Teams media admission, callback authentication, or forced termination failed.'
    query: 'AppTraces | where Message has_any ("Teams media start failed", "callback authentication", "forced_termination", "runtime validation is fail-closed") | summarize Breaches=count()'
    threshold: 0
  }
  {
    name: 'dropped-media-frames'
    description: 'Teams media frames were dropped during the alert window.'
    query: 'AppMetrics | where Name == "teams.media.frames.dropped" | summarize Breaches=sum(Sum)'
    threshold: 10
  }
  {
    name: 'realtime-latency'
    description: 'Teams media setup or realtime latency exceeded the approved threshold.'
    query: 'AppMetrics | where Name in ("teams.media.setup.latency", "teams.call.provider.latency") | summarize Breaches=percentile(Max, 95)'
    threshold: 3000
  }
]

resource applicationAlerts 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = [for definition in applicationAlertDefinitions: {
  name: '${suffix}-${definition.name}'
  location: location
  kind: 'LogAlert'
  properties: {
    description: definition.description
    severity: 1
    enabled: true
    scopes: [ appInsights.id ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    autoMitigate: true
    skipQueryValidation: true
    criteria: {
      allOf: [
        {
          query: definition.query
          timeAggregation: 'Total'
          metricMeasureColumn: 'Breaches'
          operator: 'GreaterThan'
          threshold: definition.threshold
          failingPeriods: { numberOfEvaluationPeriods: 1, minFailingPeriodsToAlert: 1 }
        }
      ]
    }
    actions: { actionGroups: [ actionGroup.id ] }
  }
}]

resource workbook 'Microsoft.Insights/workbooks@2023-06-01' = {
  name: guid(resourceGroup().id, suffix, 'teams-media-operations')
  location: location
  kind: 'shared'
  properties: {
    displayName: 'Alex Teams media operations'
    category: 'workbook'
    sourceId: appInsights.id
    serializedData: workbookData
    version: '1.0'
  }
}

resource autoscale 'Microsoft.Insights/autoscaleSettings@2022-10-01' = {
  name: '${suffix}-media-autoscale'
  location: location
  properties: {
    enabled: true
    targetResourceUri: vmss.id
    profiles: [
      {
        name: 'bounded-cpu-capacity'
        capacity: { minimum: string(minimumInstances), maximum: string(maximumInstances), default: string(instanceCount) }
        rules: [
          {
            metricTrigger: { metricName: 'Percentage CPU', metricResourceUri: vmss.id, timeGrain: 'PT1M', statistic: 'Average', timeWindow: 'PT5M', timeAggregation: 'Average', operator: 'GreaterThan', threshold: 60, dividePerInstance: false }
            scaleAction: { direction: 'Increase', type: 'ChangeCount', value: '1', cooldown: 'PT10M' }
          }
          {
            metricTrigger: { metricName: 'Percentage CPU', metricResourceUri: vmss.id, timeGrain: 'PT1M', statistic: 'Average', timeWindow: 'PT15M', timeAggregation: 'Average', operator: 'LessThan', threshold: 25, dividePerInstance: false }
            scaleAction: { direction: 'Decrease', type: 'ChangeCount', value: '1', cooldown: 'PT20M' }
          }
        ]
      }
    ]
    notifications: []
  }
}

output callbackFqdn string = callbackPublicIp.properties.dnsSettings.fqdn
output serviceFqdn string = serviceFqdn
output mediaHostVmssName string = vmss.name
output managedIdentityClientId string = identity.properties.clientId
output applicationInsightsName string = appInsights.name
output operationsWorkbookName string = workbook.name
output deploymentState string = 'templates_deployed_not_live_validated'
