// The hosted scan server (docs/decisions/0014-hosted-scan-server.md): homr behind services/omr, unmodified,
// in Azure Container Apps. Deployed by .github/workflows/scan-server.yml after it publishes the image,
// not by CI's main.bicep run, so the app is never created before its image exists.
//
// Costs: a Consumption-only environment with Azure's own network costs nothing by itself (no load
// balancer, unlike a workload profiles environment or a custom VNet), and no Log Analytics workspace is
// attached. The app scales to zero, so it is billed only while it reads a page.
//
// The same file deploys develop's scan server (branch 'develop'): ca-motifflab-scan-dev in cae-motifflab-dev, the
// environment infra/dev-env.bicep created in rg-motifflab-dev with the same properties as here, so this deploy
// leaves it as it is.

targetScope = 'resourceGroup'

@description('Container image of the scan server, for example ghcr.io/andreykukalev/motiff-omr:<commit>.')
param image string

// Poland Central, next to the shared Cosmos DB account; an EU region either way.
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

@description('Other addresses allowed to call the server from a browser, besides the web app and its previews.')
param extraOrigins array = []

@description('Pages a PDF may have. Container Apps ends a request after 240 s, and a page takes up to 40 s.')
param maxPages int = 4

// Each copy still reads one request at a time (concurrentRequests 1); up to 4 copies let the app send the parts of a
// long recording side by side (Andrei, 2026-11-11, with the long-recordings plan), and still cap what scanning can cost.
@description('Most copies of the server running at once, each reading one request at a time.')
@minValue(1)
@maxValue(10)
param maxReplicas int = 4

@description('External ID authority, https://<tenant>.ciamlogin.com/<tenant id>/v2.0. Empty: anyone may scan.')
param authAuthority string = ''

@description('Client id of the Motiff Lab API app registration, the audience of the players\' tokens.')
param authAudience string = ''

param tags object = {
  app: 'motiff-lab'
  service: 'scan'
}

var suffix = branch == 'develop' ? '-dev' : ''
var branchTags = branch == 'develop' ? union(tags, { env: 'develop' }) : tags

// The web app, its pull request previews and the test site: <name>.<n>.azurestaticapps.net becomes
// <name>-<pr>.<region>.<n>.azurestaticapps.net or <name>-develop.<region>.<n>.azurestaticapps.net. The same on both
// branches, so develop's server answers the web test site.
var siteHost = split(webHost, '.')
var allowedOrigins = concat([ 'https://${webHost}' ], extraOrigins)
var previewOrigins = 'https://${siteHost[0]}-([0-9]+|develop)\\.[a-z0-9]+\\.${siteHost[1]}\\.azurestaticapps\\.net'

// No workloadProfiles: a Consumption-only environment. No appLogsConfiguration: no Log Analytics bill.
resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-motifflab${suffix}'
  location: location
  tags: branchTags
  properties: {
    zoneRedundant: false
  }
}

resource scan 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-motifflab-scan${suffix}'
  location: location
  tags: branchTags
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
      // Zero when idle. Each copy reads one upload at a time; maxReplicas caps what scanning can cost, signed in
      // or not.
      scale: {
        minReplicas: 0
        maxReplicas: maxReplicas
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
