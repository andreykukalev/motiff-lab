// The hosted scan server (docs/architecture/adr/0014-hosted-scan-server.md, 0035): its four engines behind
// src/backend/omr, unmodified, in Azure Container Apps. Deployed by .github/workflows/scan-server.yml after it
// publishes the image, not by CI's main.bicep run, so the app is never created before its image exists.
//
// It stands on infra/scan-base.bicep, made once by Andrei: its workload profiles environment (the Consumption profile,
// up to 4 vCPU and 8 GiB), its identity and the storage of its scan jobs with the identity's roles on it. This file
// only finds them, so the workflow's Contributor identity gives no roles.
//
// Costs: the environment costs nothing by itself and no Log Analytics workspace is attached. The app scales to zero,
// so it is billed only while it reads a page or a job waits; about 360 vCPU-seconds a page with four engines.
//
// The same file deploys develop's scan server (branch 'develop'): ca-motifflab-scan-dev in cae-motifflab-scan-dev, in
// rg-motifflab-dev, whose base dev-env.bicep made.

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

@description('Pages a PDF sent to /recognise may have. Container Apps ends a request after 240 s, and a page takes up to 40 s. A scan job is read in the background and has its own limit (MAX_JOB_PAGES, 100 in the server).')
param maxPages int = 4

// Each copy still reads one request at a time (concurrentRequests 1); up to 4 copies let the app send the parts of a
// long recording side by side (Andrei, 2026-11-11, with the long-recordings plan), and still cap what scanning can cost.
// A copy reads four units of the scan jobs at once (one page by its four engines), so 4 copies read 4 pages at once.
@description('Most copies of the server running at once, each reading one request, or four job units, at a time.')
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

// Made by scan-base.bicep, under the same names.
var jobsQueue = 'scan-units'
var jobsContainer = 'scan-jobs'
// A copy reads as many units at once as the server has engines (JOB_WORKERS, src/backend/omr/README.md): one more
// waiting unit than that wakes one more copy.
var unitsPerCopy = 4

// The web app, its pull request previews and the test site: <name>.<n>.azurestaticapps.net becomes
// <name>-<pr>.<region>.<n>.azurestaticapps.net or <name>-develop.<region>.<n>.azurestaticapps.net. The same on both
// branches, so develop's server answers the web test site.
var siteHost = split(webHost, '.')
var allowedOrigins = concat([ 'https://${webHost}' ], extraOrigins)
var previewOrigins = 'https://${siteHost[0]}-([0-9]+|develop)\\.[a-z0-9]+\\.${siteHost[1]}\\.azurestaticapps\\.net'

resource environment 'Microsoft.App/managedEnvironments@2025-07-01' existing = {
  name: 'cae-motifflab-scan${suffix}'
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: 'id-motifflab-scan${suffix}'
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: 'stmlscan${uniqueString(resourceGroup().id)}'
}

resource scan 'Microsoft.App/containerApps@2025-07-01' = {
  name: 'ca-motifflab-scan${suffix}'
  location: location
  tags: branchTags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    environmentId: environment.id
    workloadProfileName: 'Consumption'
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
          // A page's four engines read it side by side, a core each (0035): the most the Consumption profile gives.
          resources: {
            cpu: json('4.0')
            memory: '8Gi'
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
            // The scan jobs lie in the storage account (src/backend/omr/app/azure_store.py), signed in as the identity.
            {
              name: 'JOBS_STORAGE_ACCOUNT'
              value: storage.name
            }
            {
              name: 'JOBS_QUEUE'
              value: jobsQueue
            }
            {
              name: 'JOBS_CONTAINER'
              value: jobsContainer
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: identity.properties.clientId
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
      // or not. The queue rule keeps a copy awake while any unit waits, is read or waits for its retry (the length
      // counts leased and delayed messages too), so the app scales to zero only once every job is finished.
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
          {
            name: 'scan-units'
            azureQueue: {
              accountName: storage.name
              queueName: jobsQueue
              queueLength: unitsPerCopy
              identity: identity.id
            }
          }
        ]
      }
    }
  }
}

output url string = 'https://${scan.properties.configuration.ingress.fqdn}'
