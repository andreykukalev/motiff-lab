#Requires -Version 5.1
<#
.SYNOPSIS
  Gives players Pro or makes them testers (redesign preparation item 14, decision record 0018 section 12). Running it again
  is safe.

.DESCRIPTION
  A player's plan is an app role of the Motiff Lab API in the sign-in tenant, so it arrives in their access token and the
  API and the scan server check it without a database (MotiffLab.MusicCore.Accounts.Plans):
    Pro     scanning and transcription on Motiff Lab's server, saving online and, later, the assistant;
    Tester  may switch between Pro and no Pro in Settings to test both modes; the servers trust that switch only from a tester.
  The script adds the two roles to the 'Motiff Lab API' registration when it has none, then assigns them to the players
  named by email (they must have signed in once, so their account exists). With -Remove it takes the named roles away.
  The new roles reach a player at their next sign-in, or within an hour when their token is renewed.
  Payment comes later and will grant Pro by itself; until then this script is how Pro is given.

.EXAMPLE
  .\infra\azure\pro.ps1 -Email you@example.com -Pro -Tester     # Pro, and the switch to test without it
.EXAMPLE
  .\infra\azure\pro.ps1 -Email friend@example.com -Pro
.EXAMPLE
  .\infra\azure\pro.ps1 -Email friend@example.com -Pro -Remove
.EXAMPLE
  .\infra\azure\pro.ps1 -List
.PARAMETER Domain
  The sign-in tenant's domain, as in setup.ps1: players sign in on <Domain>.ciamlogin.com.
#>
[CmdletBinding(DefaultParameterSetName = 'Assign')]
param(
  [Parameter(ParameterSetName = 'Assign', Mandatory)][string[]]$Email,
  [Parameter(ParameterSetName = 'Assign')][switch]$Pro,
  [Parameter(ParameterSetName = 'Assign')][switch]$Tester,
  [Parameter(ParameterSetName = 'Assign')][switch]$Remove,
  [Parameter(ParameterSetName = 'List', Mandatory)][switch]$List,
  [ValidatePattern('^[a-z0-9]{1,26}$')][string]$Domain = 'motifflab'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'MotiffAzure.psm1') -Force

$ApiAppName = 'Motiff Lab API'
# The names are a contract with the API and the scan server (MotiffLab.MusicCore.Accounts.Plans, services/omr/app/auth.py).
$Roles = @(
  [ordered]@{ value = 'Pro'; name = 'Pro'; description = 'Scanning, transcription and saving online on Motiff Lab''s servers, and the assistant.' },
  [ordered]@{ value = 'Tester'; name = 'Tester'; description = 'May switch between Pro and no Pro to test both modes.' }
)

if (-not $List -and -not $Pro -and -not $Tester) { throw 'Name a role: -Pro, -Tester or both.' }
Assert-MotiffCommand -Name 'az' -Hint 'Install the Azure CLI (winget install Microsoft.AzureCLI), then reopen the terminal.'

Write-MotiffStep "Sign-in tenant $Domain"
# The tenant's id is in its public sign-in configuration; nothing needs a subscription.
$configuration = Invoke-RestMethod -UseBasicParsing -Uri "https://$Domain.ciamlogin.com/$Domain.onmicrosoft.com/v2.0/.well-known/openid-configuration"
if ("$($configuration.issuer)" -notmatch '([0-9a-fA-F-]{36})') { throw "No tenant answers on $Domain.ciamlogin.com." }
$tenantId = $Matches[1]
Write-MotiffInfo "Tenant ID: $tenantId"
$token = Get-MotiffGraphToken -TenantId $tenantId

Write-MotiffStep "App roles of '$ApiAppName'"
$apps = @(Get-MotiffGraphList -Token $token -Path "/applications?`$filter=displayName eq '$ApiAppName'" | Where-Object { "$($_.displayName)" -ceq $ApiAppName })
if ($apps.Count -ne 1) { throw "Expected one app registration named '$ApiAppName' in the sign-in tenant, found $($apps.Count). Run setup.ps1 first." }
$api = $apps[0]
$have = @(@(Get-MotiffField $api 'appRoles') | Where-Object { $_ })
$roleList = @($have)
$added = @()
foreach ($role in $Roles) {
  if (@($have | Where-Object { "$($_.value)" -ceq $role.value }).Count -eq 0) {
    $roleList += [pscustomobject][ordered]@{
      id = [guid]::NewGuid().ToString(); value = $role.value; displayName = $role.name; description = $role.description
      allowedMemberTypes = @('User'); isEnabled = $true
    }
    $added += $role.value
  }
}
if ($added.Count -gt 0) {
  Invoke-MotiffGraph -Token $token -Method PATCH -Path "/applications/$($api.id)" -Body @{ appRoles = @($roleList) } | Out-Null
  Write-MotiffInfo "Added the roles $($added -join ' and ')"
}
else {
  Write-MotiffInfo 'Pro and Tester are there'
}
$roleIds = @{}
foreach ($role in $roleList) { $roleIds["$($role.value)"] = "$($role.id)" }
$roleNames = @{}
foreach ($role in $roleList) { $roleNames["$($role.id)"] = "$($role.value)" }

$sps = @(Get-MotiffGraphList -Token $token -Path "/servicePrincipals?`$filter=appId eq '$($api.appId)'")
if ($sps.Count -eq 0) { throw "The API has no service principal in the sign-in tenant. Run setup.ps1 first." }
$apiSp = $sps[0]
$assignments = @(Get-MotiffGraphList -Token $token -Path "/servicePrincipals/$($apiSp.id)/appRoleAssignedTo")

if ($List) {
  Write-MotiffStep 'Players with a role'
  if ($assignments.Count -eq 0) { Write-MotiffInfo 'Nobody yet.' }
  foreach ($group in ($assignments | Group-Object principalId)) {
    $names = @($group.Group | ForEach-Object { $roleNames["$($_.appRoleId)"] } | Where-Object { $_ }) -join ', '
    Write-MotiffInfo "$($group.Group[0].principalDisplayName): $names"
  }
  return
}

function Find-Player([string]$Address) {
  # Email-code accounts keep the address as their sign-in name; accounts from Google or Apple as their mail.
  $issuer = "$Domain.onmicrosoft.com"
  $quoted = $Address.Replace("'", "''")
  $found = @(Get-MotiffGraphList -Token $token -Path "/users?`$select=id,displayName,mail&`$filter=identities/any(i:i/issuer eq '$issuer' and i/issuerAssignedId eq '$quoted')")
  if ($found.Count -eq 0) { $found = @(Get-MotiffGraphList -Token $token -Path "/users?`$select=id,displayName,mail&`$filter=mail eq '$quoted'") }
  if ($found.Count -eq 0) { throw "No player signs in as $Address yet. They sign in once on the web app, then run this again." }
  return $found[0]
}

$wanted = @()
if ($Pro) { $wanted += 'Pro' }
if ($Tester) { $wanted += 'Tester' }
foreach ($address in $Email) {
  Write-MotiffStep $address
  $player = Find-Player $address
  foreach ($name in $wanted) {
    $existing = @($assignments | Where-Object { "$($_.principalId)" -eq "$($player.id)" -and "$($_.appRoleId)" -eq $roleIds[$name] })
    if ($Remove) {
      foreach ($assignment in $existing) {
        Invoke-MotiffGraph -Token $token -Method DELETE -Path "/servicePrincipals/$($apiSp.id)/appRoleAssignedTo/$($assignment.id)" | Out-Null
      }
      Write-MotiffInfo $(if ($existing.Count -gt 0) { "No longer $name" } else { "Was not $name" })
    }
    elseif ($existing.Count -gt 0) {
      Write-MotiffInfo "Already $name"
    }
    else {
      Invoke-MotiffGraph -Token $token -Method POST -Path "/servicePrincipals/$($apiSp.id)/appRoleAssignedTo" -Body ([ordered]@{
          principalId = "$($player.id)"; resourceId = "$($apiSp.id)"; appRoleId = $roleIds[$name]
        }) | Out-Null
      Write-MotiffInfo "Now $name"
    }
  }
}
Write-MotiffInfo 'Sign out and in again in the app to see it at once.'
