// One-time bootstrap for CI: a user-assigned managed identity that GitHub Actions signs in as
// through OIDC (no stored secrets), allowed to deploy into this resource group only.
// Deployed by bootstrap.sh; CI then provisions main.bicep and deploys the app on its own.

targetScope = 'resourceGroup'

@description('GitHub repository as owner/name.')
param repository string = 'andreykukalev/motiff-lab'

param location string = resourceGroup().location

param tags object = {
  app: 'motiff-lab'
  purpose: 'ci'
}

var contributorRoleId = 'b24988ac-6180-42a0-ab88-20f7382dd24c'

resource ci 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-motifflab-github'
  location: location
  tags: tags
}

// Pushes to main: provision infrastructure and deploy production.
resource mainBranch 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  parent: ci
  name: 'github-main'
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    subject: 'repo:${repository}:ref:refs/heads/main'
    audiences: [
      'api://AzureADTokenExchange'
    ]
  }
}

// Pull requests: what-if, deploy and close preview environments.
// Credentials on one identity must be written one at a time, hence the dependsOn.
resource pullRequests 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  parent: ci
  name: 'github-pull-request'
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    subject: 'repo:${repository}:pull_request'
    audiences: [
      'api://AzureADTokenExchange'
    ]
  }
  dependsOn: [
    mainBranch
  ]
}

resource contributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, ci.id, contributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', contributorRoleId)
    principalId: ci.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

output clientId string = ci.properties.clientId
