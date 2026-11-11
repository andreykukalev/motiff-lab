#Requires -Version 5.1
<#
.SYNOPSIS
  Lets players sign in on the web test site (the Static Web App's environment "develop", governance/README.md), once.
  Running it again is safe.

.DESCRIPTION
  The test site signs in with the web app's registration 'Motiff Lab' in the sign-in tenant, as the main address does,
  so it gets the same scopes, consent and app roles (Pro, Tester). This adds the test site's address to the
  registration's single-page-app redirects; the redirects already there stay. CI writes the same sign-in settings into
  the test site as into the main address (.github/workflows/ci.yml), and the API and the scan server already answer the
  test site (infra/api.bicep, infra/scan.bicep). Needs no subscription.

.EXAMPLE
  .\infra\azure\test-site-signin.ps1
.PARAMETER TestSiteHost
  The test site's address, as CI's summary and the Static Web App's Environments tab show it.
.PARAMETER Domain
  The sign-in tenant's domain, as in setup.ps1: players sign in on <Domain>.ciamlogin.com.
#>
param(
  [ValidatePattern('^[a-z0-9-]+-develop\.[a-z0-9]+\.[0-9]+\.azurestaticapps\.net$')]
  [string]$TestSiteHost = 'delightful-ground-046924d0f-develop.eastus2.4.azurestaticapps.net',
  [ValidatePattern('^[a-z0-9]{1,26}$')][string]$Domain = 'motifflab'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'MotiffAzure.psm1') -Force

$WebAppName = 'Motiff Lab'
$RedirectUri = "https://$TestSiteHost/authentication/login-callback"
Assert-MotiffCommand -Name 'az' -Hint 'Install the Azure CLI (winget install Microsoft.AzureCLI), then reopen the terminal.'

Write-MotiffStep "Sign-in tenant $Domain"
# The tenant's id is in its public sign-in configuration; nothing needs a subscription.
$configuration = Invoke-RestMethod -UseBasicParsing -Uri "https://$Domain.ciamlogin.com/$Domain.onmicrosoft.com/v2.0/.well-known/openid-configuration"
if ("$($configuration.issuer)" -notmatch '([0-9a-fA-F-]{36})') { throw "No tenant answers on $Domain.ciamlogin.com." }
$tenantId = $Matches[1]
Write-MotiffInfo "Tenant ID: $tenantId"
$token = Get-MotiffGraphToken -TenantId $tenantId

Write-MotiffStep "App registration '$WebAppName'"
$apps = @(Get-MotiffGraphList -Token $token -Path "/applications?`$filter=displayName eq '$WebAppName'" | Where-Object { "$($_.displayName)" -ceq $WebAppName })
if ($apps.Count -ne 1) { throw "Expected one app registration named '$WebAppName' in the sign-in tenant, found $($apps.Count). Run setup.ps1 first." }
$app = $apps[0]
$uris = @(@(Get-MotiffField (Get-MotiffField $app 'spa') 'redirectUris') | Where-Object { $_ })
if ($uris -contains $RedirectUri) {
  Write-MotiffInfo "Already registered: $RedirectUri"
} else {
  $uris += $RedirectUri
  Invoke-MotiffGraph -Token $token -Method PATCH -Path "/applications/$($app.id)" -Body ([ordered]@{
      spa = @{ redirectUris = @($uris) }
    }) | Out-Null
  Write-MotiffInfo "The test site signs in at $RedirectUri"
}
Write-MotiffInfo 'The next push to develop deploys the test site with sign-in (or rerun CI on develop).'
