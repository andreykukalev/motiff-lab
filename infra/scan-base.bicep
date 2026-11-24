// What the hosted scan server stands on (docs/architecture/adr/0035-scan-jobs-four-engines.md, sections 3 and 4), made
// once by Andrei, before the server's first deploy from scan.bicep: its own Container Apps environment, its identity,
// the storage account of its scan jobs and the identity's roles on that storage. Here and not in scan.bicep because the
// Scan server workflow signs in as a Contributor, which may not give roles (Microsoft.Authorization/roleAssignments).
// main: `az deployment group create --resource-group rg-motifflab --template-file infra/scan-base.bicep`, run by
// infra/azure/setup.ps1; develop: dev-env.bicep deploys it as a module (infra/README.md, "Scan jobs").
//
// The environment uses workload profiles with only the Consumption profile, which gives an app up to 4 vCPU and 8 GiB
// and images up to 8 GB; a Consumption-only environment (cae-motifflab, the API's) stops at 2 vCPU and 4 GiB. Without
// a dedicated profile there is no plan management charge, and on Azure's own network no load balancer of ours: the
// environment costs nothing by itself, the app is billed per second while it runs.
//
// Costs of the storage: an LRS account, one queue and one container; a job's blobs live a day at most, so the bill is
// its transactions, cents a month.

targetScope = 'resourceGroup'

// Poland Central, next to the API and the database; an EU region either way.
param location string = 'polandcentral'

@description('Which branch this is for: main (rg-motifflab) or develop (rg-motifflab-dev, names ending in -dev).')
@allowed([
  'main'
  'develop'
])
param branch string = 'main'

param tags object = {
  app: 'motiff-lab'
  service: 'scan'
}

var suffix = branch == 'develop' ? '-dev' : ''
var branchTags = branch == 'develop' ? union(tags, { env: 'develop' }) : tags

// scan.bicep finds these by the same names.
var queueName = 'scan-units'
var containerName = 'scan-jobs'
var queueDataContributor = '974c5e8b-45b9-4653-ba55-5f855dd0fb88' // Storage Queue Data Contributor
var blobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe' // Storage Blob Data Contributor

// No appLogsConfiguration: no Log Analytics bill. No VNet: Azure's own network.
resource environment 'Microsoft.App/managedEnvironments@2025-07-01' = {
  name: 'cae-motifflab-scan${suffix}'
  location: location
  tags: branchTags
  properties: {
    zoneRedundant: false
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
  }
}

// The scan server signs in to its storage as this identity (DefaultAzureCredential with AZURE_CLIENT_ID), and its
// queue scale rule reads the queue's length as it too, so no key exists anywhere.
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-motifflab-scan${suffix}'
  location: location
  tags: branchTags
}

// Signed in only (no shared key, no public blobs), over TLS 1.2.
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'stmlscan${uniqueString(resourceGroup().id)}'
  location: location
  tags: branchTags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

// The units' messages: references to a job, a page and an engine, never the music (claim check).
resource queue 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-05-01' = {
  parent: queueService
  name: queueName
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

// The jobs' pages, units' states and readings (jobs/<id>/…), and the engines' measured seconds (engines/…).
resource container 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: containerName
  properties: {
    publicAccess: 'None'
  }
}

// The 24-hour rule is the server's: its supervisor deletes a job a day after it was made at the latest. This rule
// is the storage's own backstop, for a job left behind while no copy of the server ran: a job's blob is deleted once
// it has not been written for a day (Azure runs lifecycle rules about once a day, so it may take a day more).
resource lifecycle 'Microsoft.Storage/storageAccounts/managementPolicies@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          name: 'delete-scan-jobs-after-a-day'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: {
              blobTypes: [
                'blockBlob'
              ]
              prefixMatch: [
                '${containerName}/jobs/'
              ]
            }
            actions: {
              baseBlob: {
                delete: {
                  daysAfterModificationGreaterThan: 1
                }
              }
            }
          }
        }
      ]
    }
  }
}

// Each role only on what it is for: the queue, and the container.
resource queueRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: queue
  name: guid(queue.id, identity.id, queueDataContributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', queueDataContributor)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource blobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: container
  name: guid(container.id, identity.id, blobDataContributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataContributor)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Container Apps with external ingress get <app name>.<default domain>.
output scanUrl string = 'https://ca-motifflab-scan${suffix}.${environment.properties.defaultDomain}'
