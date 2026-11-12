#Requires -Version 5.1
<#
.SYNOPSIS
  Creates the develop environment's own servers and data, once: the group rg-motifflab-dev next to rg-motifflab.

.DESCRIPTION
  In the subscription that holds rg-motifflab (Visual Studio Premium), Poland Central:
  1. the resource group rg-motifflab-dev;
  2. infra/dev-env.bicep there: the Container Apps environment cae-motifflab-dev (Consumption only, scales to zero),
     a serverless Cosmos DB account cosmos-motifflab-dev-<8 characters> with the database motifflab and the container projects,
     the API's identity id-motifflab-api-dev (data access to that database only), and the CI identity
     id-motifflab-github-dev, which signs in from the develop branch and is Contributor on this group only;
  3. the GitHub repository variables the API and Scan server workflows read for develop (identifiers, not secrets):
     AZURE_DEV_CLIENT_ID, AZURE_DEV_RESOURCE_GROUP, COSMOS_ENDPOINT_DEV, API_URL_DEV, SCAN_URL_DEV.
  Nothing in rg-motifflab, the sign-in tenant or the Static Web App changes. Running it again is safe.
  Standing cost: none. The servers scale to zero; the serverless database bills only requests and storage.
  Everything for develop can be removed at once: az group delete --name rg-motifflab-dev.

.EXAMPLE
  .\infra\azure\dev-env.ps1
#>
[CmdletBinding()]
param(
  [string]$Subscription = '[Andrey Kukalev] Visual Studio Premium',
  [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repository = 'andreykukalev/motiff-lab',
  [string]$ResourceGroup = 'rg-motifflab-dev',
  [string]$Location = 'polandcentral',
  [switch]$Yes
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'MotiffAzure.psm1') -Force

Assert-MotiffCommand -Name 'az' -Hint 'Install the Azure CLI (winget install Microsoft.AzureCLI), then reopen the terminal.'
Assert-MotiffCommand -Name 'gh' -Hint 'Install the GitHub CLI (winget install GitHub.cli), then reopen the terminal.'
# Signed in to GitHub before anything changes in Azure, so the variables at the end can be set (as setup.ps1 does).
$null = Invoke-MotiffNative { & gh auth status 2>&1 }
if ($LASTEXITCODE -ne 0) {
  Write-MotiffInfo 'Signing in to GitHub...'
  Invoke-MotiffNative { & gh auth login --hostname github.com --git-protocol https --web }
  if ($LASTEXITCODE -ne 0) { throw 'GitHub sign-in failed.' }
}

Write-MotiffStep 'Signing in to Azure'
$account = Connect-MotiffAzure -Subscription $Subscription
$ArmArgs = @('--subscription', "$($account.id)")

Write-MotiffStep 'What this creates'
Write-MotiffInfo "Resource group $ResourceGroup ($Location) with:"
Write-MotiffInfo '  - Container Apps environment cae-motifflab-dev (the develop API and scan server go here)'
Write-MotiffInfo '  - serverless Cosmos DB account cosmos-motifflab-dev-..., database motifflab, container projects'
Write-MotiffInfo '  - identities id-motifflab-api-dev (that database only) and id-motifflab-github-dev (CI from develop, this group only)'
Write-MotiffInfo "On ${Repository}: variables AZURE_DEV_CLIENT_ID, AZURE_DEV_RESOURCE_GROUP, COSMOS_ENDPOINT_DEV, API_URL_DEV, SCAN_URL_DEV"
Write-MotiffInfo 'Standing cost: none. rg-motifflab and its data are not touched.'
if (-not $Yes) {
  $answer = Read-Host '    Go ahead? [y/N]'
  if ($answer -notmatch '^(y|yes)$') { Write-MotiffInfo 'Nothing was changed.'; return }
}

Write-MotiffStep "Resource group $ResourceGroup"
Invoke-MotiffAz (@('group', 'create', '--name', $ResourceGroup, '--location', $Location, '--tags', 'app=motiff-lab', 'env=develop', '-o', 'none') + $ArmArgs) | Out-Null

Write-MotiffStep 'Environment, database and identities (5 to 10 minutes the first time)'
$template = Join-Path (Join-Path (Get-MotiffRepoRoot) 'infra') 'dev-env.bicep'
$outputs = Invoke-MotiffAz (@('deployment', 'group', 'create', '--resource-group', $ResourceGroup, '--name', 'dev-env',
    '--template-file', $template, '--query', 'properties.outputs', '-o', 'json') + $ArmArgs) | ConvertFrom-Json
$ciClientId = "$($outputs.ciClientId.value)"
$cosmosEndpoint = "$($outputs.cosmosEndpoint.value)"
$apiUrl = "$($outputs.apiUrl.value)"
$scanUrl = "$($outputs.scanUrl.value)"
Write-MotiffInfo "CI identity id-motifflab-github-dev: $ciClientId"
Write-MotiffInfo "Database: $cosmosEndpoint"
Write-MotiffInfo "API (after its first deploy): $apiUrl"
Write-MotiffInfo "Scan server (after its first deploy): $scanUrl"

Write-MotiffStep 'GitHub variables'
foreach ($pair in @(
    @('AZURE_DEV_CLIENT_ID', $ciClientId),
    @('AZURE_DEV_RESOURCE_GROUP', $ResourceGroup),
    @('COSMOS_ENDPOINT_DEV', $cosmosEndpoint),
    @('API_URL_DEV', $apiUrl),
    @('SCAN_URL_DEV', $scanUrl))) {
  Invoke-MotiffGh @('variable', 'set', $pair[0], '--repo', $Repository, '--body', $pair[1]) | Out-Null
  Write-MotiffInfo "$($pair[0]) = $($pair[1])"
}

Write-MotiffStep 'Done'
Write-MotiffInfo 'The next push to develop deploys the develop API and scan server here, without approval.'
Write-MotiffInfo 'The web test site switches to them at its next deploy from develop. The iPad app keeps them as its Test'
Write-MotiffInfo 'environment in ipad/App/MotiffLab/Account.json: if the addresses above changed, change them there too.'
