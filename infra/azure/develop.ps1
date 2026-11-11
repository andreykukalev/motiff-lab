#Requires -Version 5.1
<#
.SYNOPSIS
  Turns on the web test site: every push to develop deploys the Static Web App's environment "develop"
  (governance/README.md, "Versions"). Once; running it again is safe.

.DESCRIPTION
  1. Deploys infra/bootstrap.bicep again, which adds the CI identity's sign-in for the develop branch (github-develop)
     next to the ones for main and pull requests. Nothing else in Azure changes.
  2. Sets the GitHub repository variable DEPLOY_DEVELOP to true, which CI reads (.github/workflows/ci.yml).
  Sign-in on the test site needs its address registered once: infra/azure/test-site-signin.ps1.
  The next push to develop deploys it; the address is <site>-develop.<region>.azurestaticapps.net (CI's summary and
  the Static Web App's Environments tab show it).

.EXAMPLE
  .\infra\azure\develop.ps1
#>
[CmdletBinding()]
param(
  [string]$Subscription = '[Andrey Kukalev] Visual Studio Premium',
  [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repository = 'andreykukalev/motiff-lab',
  [string]$ResourceGroup = 'rg-motifflab'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'MotiffAzure.psm1') -Force

Assert-MotiffCommand -Name 'az' -Hint 'Install the Azure CLI (winget install Microsoft.AzureCLI), then reopen the terminal.'
Assert-MotiffCommand -Name 'gh' -Hint 'Install the GitHub CLI (winget install GitHub.cli), then reopen the terminal.'

Write-MotiffStep 'Signing in to Azure'
$account = Connect-MotiffAzure -Subscription $Subscription
$ArmArgs = @('--subscription', "$($account.id)")

Write-MotiffStep "CI sign-in for the develop branch ($ResourceGroup)"
$clientId = "$(Invoke-MotiffAz (@('deployment', 'group', 'create', '--resource-group', $ResourceGroup, '--name', 'bootstrap',
  '--template-file', (Join-Path (Join-Path (Get-MotiffRepoRoot) 'infra') 'bootstrap.bicep'),
  '--query', 'properties.outputs.clientId.value', '-o', 'tsv') + $ArmArgs))".Trim()
$credentials = Invoke-MotiffAz (@('identity', 'federated-credential', 'list', '--identity-name', 'id-motifflab-github',
  '--resource-group', $ResourceGroup, '--query', '[].name', '-o', 'tsv') + $ArmArgs)
if ($credentials -notmatch 'github-develop') { throw 'The credential github-develop is missing after the deployment; nothing else was changed.' }
Write-MotiffInfo "CI identity $clientId signs in from main, pull requests and develop."

Write-MotiffStep 'GitHub variable DEPLOY_DEVELOP'
Invoke-MotiffGh @('variable', 'set', 'DEPLOY_DEVELOP', '--repo', $Repository, '--body', 'true') | Out-Null
Write-MotiffInfo 'DEPLOY_DEVELOP = true: the next push to develop deploys the web test site.'
