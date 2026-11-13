#Requires -Version 5.1
<#
.SYNOPSIS
  Empties the develop environment's database before a new testing cycle: every piece and profile that the web test site
  and the iPad's Test environment saved online goes. The players' real database (rg-motifflab) is never touched.

.DESCRIPTION
  In the subscription that holds rg-motifflab-dev (Visual Studio Premium):
  1. finds the develop Cosmos DB account as infra/dev-env.bicep names it: the one account cosmos-motifflab-dev-<8 characters>
     in rg-motifflab-dev, tagged env=develop. Anything else stops the script before it changes anything: another group,
     main's account cosmos-motifflab-<subscription>, no such account or more than one;
  2. shows what goes: the container projects of the database motifflab, with about how many items it holds (Azure Monitor's
     DocumentCount, which lags a few minutes behind) and its settings;
  3. after "y" (or with -Yes) deletes the container and creates it again, empty, with the same settings read from it just
     before: partition key /ownerId (Hash, the same key version), the same indexing policy, serverless (no throughput).
  The API's data access (dev-env.bicep's role assignment) is on the database, not on the container, so it stays; nothing
  else in rg-motifflab-dev changes. -WhatIf shows what would go and changes nothing.
  The pieces on the devices are cleared there: Settings, About, "Clear the test library" on the iPad (in Test) and on the
  web test site. Clear the database first, so a device's sync finds nothing to bring back.

.EXAMPLE
  .\infra\azure\reset-dev-data.ps1 -WhatIf

.EXAMPLE
  .\infra\azure\reset-dev-data.ps1
#>
[CmdletBinding(SupportsShouldProcess)]
param(
  [string]$Subscription = '[Andrey Kukalev] Visual Studio Premium',
  [switch]$Yes
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'MotiffAzure.psm1') -Force

# The develop environment's names (infra/dev-env.bicep). Fixed on purpose: this script empties nothing else.
$ResourceGroup = 'rg-motifflab-dev'
$AccountPattern = '^cosmos-motifflab-dev-[a-z0-9]{8}$'
$Database = 'motifflab'
$Container = 'projects'

Assert-MotiffCommand -Name 'az' -Hint 'Install the Azure CLI (winget install Microsoft.AzureCLI), then reopen the terminal.'

Write-MotiffStep 'Signing in to Azure'
$account = Connect-MotiffAzure -Subscription $Subscription
$ArmArgs = @('--subscription', "$($account.id)")
$mainAccount = "cosmos-motifflab-$("$($account.id)".Substring(0, 8))"

Write-MotiffStep "The develop database ($ResourceGroup)"
$accounts = @(Invoke-MotiffAz (@('cosmosdb', 'list', '--resource-group', $ResourceGroup, '-o', 'json') + $ArmArgs) | ConvertFrom-MotiffJson |
    Where-Object { $_ -and "$($_.name)" -match $AccountPattern })
if ($accounts.Count -ne 1) {
  throw "Expected exactly one account cosmos-motifflab-dev-<8 characters> in $ResourceGroup, found $($accounts.Count). Nothing was changed."
}
$cosmos = $accounts[0]
$name = "$($cosmos.name)"
# Hard guard: the account found must be develop's own, by its name, its group (from its resource id) and its tag.
$group = ("$($cosmos.id)" -split '/')[4]
$tags = Get-MotiffField $cosmos 'tags'
if ($name -eq $mainAccount -or $name -notmatch $AccountPattern -or $group -ne $ResourceGroup -or "$(Get-MotiffField $tags 'env')" -ne 'develop') {
  throw "$name in $group is not the develop environment's database (tag env=develop in $ResourceGroup). Nothing was changed."
}
Write-MotiffInfo "Account: $name ($($cosmos.documentEndpoint))"

$containerArgs = @('--account-name', $name, '--resource-group', $ResourceGroup, '--database-name', $Database, '--name', $Container) + $ArmArgs
$shown = Test-MotiffAz (@('cosmosdb', 'sql', 'container', 'show') + $containerArgs + @('-o', 'json'))
if (-not $shown) { throw "There is no container $Container in the database $Database of $name. Nothing was changed." }
$resource = ($shown | ConvertFrom-Json).resource
$paths = @($resource.partitionKey.paths)
if ($paths.Count -ne 1 -or "$($paths[0])" -ne '/ownerId') {
  throw "The container $Container is partitioned by $($paths -join ', '), not /ownerId as dev-env.bicep makes it. Nothing was changed."
}

# About how many items it holds: pieces and profiles. Azure Monitor counts them every few minutes; the latest count of
# the last six hours, or "not known" when there is none (an account nobody wrote to lately).
$count = 'not known'
$metrics = Test-MotiffAz (@('monitor', 'metrics', 'list', '--resource', "$($cosmos.id)", '--metric', 'DocumentCount',
    '--aggregation', 'Total', '--interval', 'PT5M', '--offset', '6h',
    '--filter', "DatabaseName eq '$Database' and CollectionName eq '$Container'", '-o', 'json') + $ArmArgs)
if ($metrics) {
  $points = @(($metrics | ConvertFrom-Json).value | ForEach-Object { $_.timeseries } | ForEach-Object { $_.data } |
      Where-Object { $_ -and $null -ne (Get-MotiffField $_ 'total') })
  if ($points.Count -gt 0) { $count = "about $([int64]$points[-1].total)" }
}

Write-MotiffStep 'What this deletes'
Write-MotiffInfo "Every item in $name / $Database / ${Container}: $count (every player's pieces and profiles in the test database)."
Write-MotiffInfo "The container is created again, empty: partition key /ownerId (Hash, version $(Get-MotiffField $resource.partitionKey 'version')), the same indexing policy, serverless."
Write-MotiffInfo 'The players'' real database in rg-motifflab is not touched. This cannot be undone.'
if (-not $PSCmdlet.ShouldProcess("$name/$Database/$Container", 'Delete every item (delete the container and create it again)')) { return }
if (-not $Yes) {
  $answer = Read-Host '    Empty the test database? [y/N]'
  if ($answer -notmatch '^(y|yes)$') { Write-MotiffInfo 'Nothing was changed.'; return }
}

# The settings to create it with again, read before it goes.
$create = @('cosmosdb', 'sql', 'container', 'create') + $containerArgs + @('--partition-key-path', '/ownerId', '-o', 'none')
$version = Get-MotiffField $resource.partitionKey 'version'
if ($null -ne $version) { $create += @('--partition-key-version', "$version") }
$ttl = Get-MotiffField $resource 'defaultTtl'
if ($null -ne $ttl) { $create += @('--ttl', "$ttl") }
$policy = Get-MotiffField $resource 'indexingPolicy'
$policyFile = $null
if ($null -ne $policy) {
  # Only the parts it has: a part the account answers as null is left to the service's default, as it was.
  $kept = [ordered]@{}
  foreach ($part in $policy.PSObject.Properties) { if ($null -ne $part.Value) { $kept[$part.Name] = $part.Value } }
  $policyFile = New-MotiffJsonFile $kept
  $create += @('--idx', "@$policyFile")
}

try {
  Write-MotiffStep "Deleting $Container"
  Invoke-MotiffAz (@('cosmosdb', 'sql', 'container', 'delete') + $containerArgs + @('--yes', '-o', 'none')) | Out-Null
  Write-MotiffStep "Creating $Container again, empty"
  Invoke-MotiffAz $create | Out-Null
}
finally {
  if ($policyFile) { Remove-Item -LiteralPath $policyFile -ErrorAction SilentlyContinue }
}

Write-MotiffStep 'Done'
Write-MotiffInfo 'The test database is empty. Now clear each device: Settings, About, "Clear the test library"'
Write-MotiffInfo '(the iPad in the Test environment, and the web test site in each browser you test with).'
