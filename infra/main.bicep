// Retail Reorder Advisor — all Azure resources (Free / always-free tiers).
// Deploy:  az deployment group create -g <rg> --parameters infra/main.bicepparam

targetScope = 'resourceGroup'

@description('Short lowercase prefix for resource names (letters and digits only).')
@minLength(3)
@maxLength(10)
param namePrefix string = 'reorder'

@description('Region for every resource. Keep everything in one region to avoid egress charges.')
param location string = resourceGroup().location

@description('Shared secret for POST /api/jobs/run-advisor. Leave empty until Day 4.')
@secure()
param jobKey string = ''

@description('Shared secret for POST /api/uploads. Leave empty until Day 2.')
@secure()
param adminKey string = ''

@description('Teams/Slack incoming webhook URL. Leave empty until Day 8.')
@secure()
param webhookUrl string = ''

var suffix = take(uniqueString(resourceGroup().id), 6)
var storageAccountName = toLower('${namePrefix}${suffix}st')   // globally unique, max 24 chars
var webAppName = '${namePrefix}-${suffix}-web'                  // globally unique host name
var planName = '${namePrefix}-plan'
var workspaceName = '${namePrefix}-logs'
var appInsightsName = '${namePrefix}-insights'
var containerNames = [
  'raw'
  'normalized'
  'quarantine'
  'recommendations'
  'locks'
]
var storageBlobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var tags = {
  project: 'retail-reorder-advisor'
  costCenter: 'free-tier'
}

// ---------------------------------------------------------------- Storage
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource containers 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = [
  for name in containerNames: {
    parent: blobService
    name: name
    properties: {
      publicAccess: 'None'
    }
  }
]

resource rawRetention 'Microsoft.Storage/storageAccounts/managementPolicies@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          name: 'delete-raw-after-30-days'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: {
              blobTypes: [
                'blockBlob'
              ]
              prefixMatch: [
                'raw/'
              ]
            }
            actions: {
              baseBlob: {
                delete: {
                  daysAfterModificationGreaterThan: 30
                }
              }
            }
          }
        }
      ]
    }
  }
}

// ---------------------------------------------------------------- Monitoring
resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: workspaceName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: json('0.1')   // hard daily ingestion cap
    }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    IngestionMode: 'LogAnalytics'
  }
}

// ---------------------------------------------------------------- Compute (F1 Free)
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  tags: tags
  sku: {
    name: 'F1'
    tier: 'Free'
  }
  kind: 'linux'
  properties: {
    reserved: true   // required for Linux plans
  }
}

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      appCommandLine: 'dotnet Reorder.Web.dll'
      alwaysOn: false          // F1 does not support Always On; true makes the deployment fail
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      // Bicep owns ALL app settings: a redeploy replaces the whole list,
      // so secrets are passed in as parameters rather than set by hand.
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'Storage__BlobServiceUri'
          value: storage.properties.primaryEndpoints.blob
        }
        {
          name: 'Security__JobKey'
          value: jobKey
        }
        {
          name: 'Security__AdminKey'
          value: adminKey
        }
        {
          name: 'Notifications__WebhookUrl'
          value: webhookUrl
        }
        {
          // The app's own public URL — used for the "Open dashboard" action in the Teams digest.
          // Built from webAppName (not webApp.properties) to avoid a self-reference inside the resource.
          name: 'Notifications__DashboardUrl'
          value: 'https://${webAppName}.azurewebsites.net'
        }
      ]
    }
  }
}

// ---------------------------------------------------------------- Access
// The web app reads and writes blobs with its managed identity — no connection string.
resource blobDataContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, webApp.id, storageBlobDataContributorRoleId)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributorRoleId)
    principalId: webApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------- Outputs
output webAppName string = webApp.name
output webAppUrl string = 'https://${webApp.properties.defaultHostName}'
output storageAccountName string = storage.name
output appInsightsName string = appInsights.name
