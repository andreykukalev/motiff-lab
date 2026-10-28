// The hosted scan server (docs/decisions/0014-hosted-scan-server.md): homr behind services/omr, unmodified,
// in Azure Container Apps. Deployed by .github/workflows/scan-server.yml after it publishes the image,
// not by CI's main.bicep run, so the app is never created before its image exists.
//
// Costs: a Consumption-only environment with Azure's own network costs nothing by itself (no load
// balancer, unlike a workload profiles environment or a custom VNet), and no Log Analytics workspace is
// attached. The app scales to zero, so it is billed only while it reads a page.

targetScope = 'resourceGroup'

@description('Container image of the scan server, for example ghcr.io/andreykukalev/motiff-omr:<commit>.')
param image string

// Poland Central, next to the shared Cosmos DB account; an EU region either way.
param location string = 'polandcentral'

@description('Other addresses allowed to call the server from a browser, besides the web app and its previews.')
param extraOrigins array = []

@description('Pages a PDF may have. Container Apps ends a request after 240 s, and a page takes up to 40 s.')
param maxPages int = 4

@description('External ID authority, https://<tenant>.ciamlogin.com/<tenant id>/v2.0. Empty: anyone may scan.')
param authAuthority string = ''

@description('Client id of the Motiff Lab API app registration, the audience of the players\' tokens.')
param authAudience string = ''

param tags object = {
  app: 'motiff-lab'
  service: 'scan'
}

resource staticSite 'Microsoft.Web/staticSites@2024-11-01' existing = {
  name: 'swa-motifflab'
}

// The web app, and its pull request previews: <name>.<n>.azurestaticapps.net becomes
// <name>-<pr>.westeurope.<n>.azurestaticapps.net.
var siteHost = split(staticSite.properties.defaultHostname, '.')
var allowedOrigins = concat([ 'https://${staticSite.properties.defaultHostname}' ], extraOrigins)
var previewOrigins = 'https://${siteHost[0]}-[0-9]+\\.westeurope\\.${siteHost[1]}\\.azurestaticapps\\.net'

// No workloadProfiles: a Consumption-only environment. No appLogsConfiguration: no Log Analytics bill.
resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-motifflab'
  location: location
  tags: tags
  properties: {
    zoneRedundant: false
  }
}

resource scan 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-motifflab-scan'
  location: location
  tags: tags
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8000
        transport: 'auto'
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'scan'
          image: image
          // homr uses every core for one page; 2 cores and 4 GiB is the most a Consumption-only app gets.
          resources: {
            cpu: json('2.0')
            memory: '4Gi'
          }
          env: [
            {
              name: 'ALLOWED_ORIGINS'
              value: join(allowedOrigins, ',')
            }
            {
              name: 'ALLOWED_ORIGIN_REGEX'
              value: previewOrigins
            }
            {
              name: 'MAX_PAGES'
              value: string(maxPages)
            }
            // A page is read only for a signed-in player once both are set (0014).
            {
              name: 'AUTH_AUTHORITY'
              value: authAuthority
            }
            {
              name: 'AUTH_AUDIENCE'
              value: authAudience
            }
          ]
          probes: [
            {
              // The models load on start; a cold start pulls the image too, so give it time.
              type: 'Startup'
              httpGet: {
                path: '/health'
                port: 8000
              }
              periodSeconds: 5
              failureThreshold: 48
            }
          ]
        }
      ]
      // Zero when idle. One copy at most: the server reads one upload at a time, and one copy caps what
      // scanning can cost, signed in or not.
      scale: {
        minReplicas: 0
        maxReplicas: 1
        rules: [
          {
            name: 'http'
            http: {
              metadata: {
                concurrentRequests: '1'
              }
            }
          }
        ]
      }
    }
  }
}

output url string = 'https://${scan.properties.configuration.ingress.fqdn}'
