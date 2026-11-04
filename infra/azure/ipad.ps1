#Requires -Version 5.1
<#
.SYNOPSIS
  Lets the iPad app sign in (decision record 0019, redesign preparation item 23), once. Running it again is safe.

.DESCRIPTION
  The iPad app signs in with MSAL on the web app's registration 'Motiff Lab' in the sign-in tenant, so it gets the same
  scopes, consent and app roles (Pro, Tester). This adds the registration's iOS redirect msauth.com.andreykukalev.motifflab://auth
  and turns public client flows on; the web app's redirects stay as they are. setup.ps1 does the same for a new tenant;
  this script is for a tenant set up before the iPad app signed in, and needs no subscription.

.EXAMPLE
  .\infra\azure\ipad.ps1
.PARAMETER Domain
  The sign-in tenant's domain, as in setup.ps1: players sign in on <Domain>.ciamlogin.com.
#>
param([ValidatePattern('^[a-z0-9]{1,26}$')][string]$Domain = 'motifflab')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'MotiffAzure.psm1') -Force

$WebAppName = 'Motiff Lab'
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
Set-MotiffIPadSignIn -Token $token -App $apps[0]
