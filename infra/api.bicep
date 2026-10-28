// The Motiff Lab API (src/MotiffLab.Api, decision record 0014): a signed-in player's projects in Cosmos DB.
// Deployed by .github/workflows/api.yml after it publishes the image, into the environment of scan.bicep.
// It reaches Cosmos DB as id-motifflab-api, which infra/services-bootstrap.sh allows into the motifflab
// database only; no account key is used.

targetScope = 'resourceGroup'

@description('Container image of the API, for example ghcr.io/andreykukalev/motiff-api:<commit>.')
param image string

@description('External ID authority, https://<tenant>.ciamlogin.com/<tenant id>/v2.0.')
param authAuthority string

@description('Client id of the Motiff Lab API app registration, the audience of the players\' tokens.')
param authAudience string

@description('Endpoint of the shared Cosmos DB account, https://<account>.documents.azure.com:443/.')
param cosmosEndpoint string

param location string = 'polandcentral'

// The Static Web App lives in another subscription (rg-motifflab), so its host name is given, not looked up.
@description('Host name of the web app: swa-motifflab\'s default host name.')
param webHost string = 'nice-smoke-0169fb403.6.azurestaticapps.net'

param tags object = {
  app: 'motiff-lab'
  service: 'api'
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: 'cae-motifflab'
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: 'id-motifflab-api'
}

// The web app, and its pull request previews (see scan.bicep).
var siteHost = split(webHost, '.')

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-motifflab-api'
  location: location
  tags: tags
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
              value: 'https://${siteHost[0]}-[0-9]+\\.westeurope\\.${siteHost[1]}\\.azurestaticapps\\.net'
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
