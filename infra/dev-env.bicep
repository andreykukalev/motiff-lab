// The develop environment's own servers and data (rg-motifflab-dev): a Consumption-only Container Apps environment
// for the API and the scan server of develop, a serverless Cosmos DB account with the same database and container as
// the main one, the API's identity with data access to that database only, and a CI identity that may change this
// group only, signing in from the develop branch. Deployed once by infra/azure/dev-env.ps1; the API and Scan server
// workflows then deploy api.bicep and scan.bicep here on every push to develop, without approval.
// The sign-in tenant, the Static Web App (its environment "develop" is the web test site) and the images are shared
// with main.

targetScope = 'resourceGroup'

@description('GitHub repository as it appears in the OIDC subject claim: owner@ownerId/name@repoId (see bootstrap.bicep).')
param repository string = 'andreykukalev@130970440/motiff-lab@1430211759'

param location string = resourceGroup().location

param tags object = {
  app: 'motiff-lab'
  env: 'develop'
}

var contributorRoleId = 'b24988ac-6180-42a0-ab88-20f7382dd24c'
var dataContributorRoleId = '00000000-0000-0000-0000-000000000002' // Cosmos DB Built-in Data Contributor
var database = 'motifflab'

// Same shape as scan.bicep's: no workloadProfiles (Consumption only), no Log Analytics.
resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-motifflab-dev'
  location: location
  tags: tags
  properties: {
    zoneRedundant: false
  }
}

resource ci 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-motifflab-github-dev'
  location: location
  tags: tags
}

// Pushes to develop and runs started by hand on develop.
resource developBranch 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  parent: ci
  name: 'github-develop'
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    subject: 'repo:${repository}:ref:refs/heads/develop'
    audiences: [
      'api://AzureADTokenExchange'
    ]
  }
}

// Jobs that name the GitHub environment "development". Credentials on one identity are written one at a time.
resource developmentEnvironment 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  parent: ci
  name: 'github-development'
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    subject: 'repo:${repository}:environment:development'
    audiences: [
      'api://AzureADTokenExchange'
    ]
  }
  dependsOn: [
    developBranch
  ]
}

// This group only: never rg-motifflab with the players' real data.
resource contributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, ci.id, contributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', contributorRoleId)
    principalId: ci.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource api 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-motifflab-api-dev'
  location: location
  tags: union(tags, { service: 'api' })
}

// Serverless: no reserved RU/s, billed only for requests and storage (cents a month at test use).
resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: 'cosmos-motifflab-dev-${take(uniqueString(resourceGroup().id), 8)}'
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    capabilities: [
      {
        name: 'EnableServerless'
      }
    ]
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    minimalTlsVersion: 'Tls12'
  }
}

resource sqlDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15' = {
  parent: cosmos
  name: database
  properties: {
    resource: {
      id: database
    }
  }
}

// One container for everything a player keeps, partitioned by player (as in the main database).
resource projects 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: sqlDatabase
  name: 'projects'
  properties: {
    resource: {
      id: 'projects'
      partitionKey: {
        paths: [
          '/ownerId'
        ]
        kind: 'Hash'
      }
    }
  }
}

// The API reads and writes this database only, without account keys.
resource apiData 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = {
  parent: cosmos
  name: guid(cosmos.id, api.id, dataContributorRoleId)
  properties: {
    roleDefinitionId: '${cosmos.id}/sqlRoleDefinitions/${dataContributorRoleId}'
    principalId: api.properties.principalId
    scope: '${cosmos.id}/dbs/${database}'
  }
  dependsOn: [
    sqlDatabase
  ]
}

output ciClientId string = ci.properties.clientId
output cosmosEndpoint string = cosmos.properties.documentEndpoint
// Container Apps with external ingress get <app name>.<default domain>.
output apiUrl string = 'https://ca-motifflab-api-dev.${environment.properties.defaultDomain}'
output scanUrl string = 'https://ca-motifflab-scan-dev.${environment.properties.defaultDomain}'
