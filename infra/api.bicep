// The Motiff Lab API (src/MotiffLab.Api, decision record 0014): a signed-in player's projects in Cosmos DB.
// Deployed by .github/workflows/api.yml after it publishes the image, into the environment of scan.bicep.
// It reaches Cosmos DB as id-motifflab-api, which infra/azure/setup.ps1 allows into the motifflab
// database only; no account key is used.
// The same file deploys develop's API (branch 'develop'): ca-motifflab-api-dev in rg-motifflab-dev, as
// id-motifflab-api-dev, which infra/dev-env.bicep allows into that group's own database only.

targetScope = 'resourceGroup'

@description('Container image of the API, for example ghcr.io/andreykukalev/motiff-api:<commit>.')
param image string

@description('External ID authority, https://<tenant>.ciamlogin.com/<tenant id>/v2.0.')
param authAuthority string

@description('Client id of the Motiff Lab API app registration, the audience of the players\' tokens.')
param authAudience string

@description('Endpoint of the branch\'s Cosmos DB account, https://<account>.documents.azure.com:443/ (COSMOS_ENDPOINT or COSMOS_ENDPOINT_DEV).')
param cosmosEndpoint string

param location string = 'polandcentral'

@description('Which branch this deploys: main (rg-motifflab) or develop (rg-motifflab-dev, names ending in -dev).')
@allowed([
  'main'
  'develop'
])
param branch string = 'main'

// The workflow passes the WEB_HOST repository variable, which infra/azure/setup.ps1 sets from swa-motifflab in this group.
@description('Host name of the web app: swa-motifflab\'s default host name.')
param webHost string = 'nice-smoke-0169fb403.6.azurestaticapps.net'

param tags object = {
  app: 'motiff-lab'
  service: 'api'
}

var suffix = branch == 'develop' ? '-dev' : ''
var appTags = branch == 'develop' ? union(tags, { env: 'develop' }) : tags

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: 'cae-motifflab${suffix}'
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: 'id-motifflab-api${suffix}'
}

// The web app, its pull request previews and the web test site (see scan.bicep), on both branches.
var siteHost = split(webHost, '.')

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-motifflab-api${suffix}'
  location: location
  tags: appTags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'api'
          image: image
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            {
              name: 'Auth__Authority'
              value: authAuthority
            }
            {
              name: 'Auth__Audience'
              value: authAudience
            }
            {
              name: 'Cosmos__Endpoint'
              value: cosmosEndpoint
            }
            {
              // DefaultAzureCredential signs in as this user-assigned identity.
              name: 'AZURE_CLIENT_ID'
              value: identity.properties.clientId
            }
            {
              name: 'Cors__Origins'
              value: 'https://${webHost}'
            }
            {
              name: 'Cors__OriginPattern'
              value: 'https://${siteHost[0]}-([0-9]+|develop)\\.[a-z0-9]+\\.${siteHost[1]}\\.azurestaticapps\\.net'
            }
          ]
          probes: [
            {
              type: 'Startup'
              httpGet: {
                path: '/health'
                port: 8080
              }
              periodSeconds: 2
              failureThreshold: 30
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 2
      }
    }
  }
}

output url string = 'https://${api.properties.configuration.ingress.fqdn}'
