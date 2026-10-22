// Motiff Lab hosting: one Azure Static Web App on the Free plan (docs/decisions/0001-stack.md).
// Stage 1 runs fully in the browser, so there are no Functions, storage or AI resources yet.

targetScope = 'resourceGroup'

@description('Name of the Static Web App. Becomes part of the default *.azurestaticapps.net host name.')
param name string = 'swa-motifflab'

// Static Web Apps is offered in a few regions only; West Europe is the EU one.
// Static content is served from the global edge; the region holds the app's metadata
// and, from Stage 2, its managed Functions.
@description('Region for the Static Web App resource.')
@allowed([
  'westeurope'
])
param location string = 'westeurope'

@description('Pricing plan. Free covers Stage 1: 3 preview environments, 100 GB bandwidth a month.')
@allowed([
  'Free'
  'Standard'
])
param sku string = 'Free'

param tags object = {
  app: 'motiff-lab'
  stage: '1'
}

resource site 'Microsoft.Web/staticSites@2024-11-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: sku
    tier: sku
  }
  properties: {
    // Deploys come from GitHub Actions with the deployment token, so the site is not
    // linked to the repository here (linking would need a GitHub PAT at deploy time).
    stagingEnvironmentPolicy: 'Enabled'
    allowConfigFileUpdates: true
    publicNetworkAccess: 'Enabled'
  }
}

output name string = site.name
output defaultHostName string = site.properties.defaultHostname
