#Requires -Version 5.1
<#
.SYNOPSIS
  Sets up Motiff Lab's services in Azure, once (decision record 0014): the scan server, the API, their database and the
  players' sign-in. Run it from a clone of the repository on your own computer; running it again is safe.

.DESCRIPTION
  In the subscription that holds rg-shared and its free-tier Cosmos DB account (Visual Studio Premium):
    1. the database motifflab (400 RU/s shared) and its container projects in the shared account. It first adds up what
       the account's other databases use, and stops, creating nothing, if Motiff Lab would take it past the free 1000 RU/s;
    2. the resource group rg-motifflab (Poland Central) with the CI identity id-motifflab-github (infra/bootstrap.bicep,
       Contributor on that group only), the API's identity id-motifflab-api (data access to the motifflab database only)
       and the web app's Static Web App swa-motifflab (infra/main.bicep, Free plan, West Europe), so everything Motiff Lab
       runs is in one group and CI deploys all of it as one identity;
    3. the sign-in tenant, Microsoft Entra External ID (free up to 50,000 monthly users), linked to rg-motifflab, with the
       app registrations Motiff Lab API (scopes Projects.ReadWrite and Scan) and Motiff Lab (the web app), and a sign-up
       and sign-in flow by email code. Google and Apple are added later, by hand (infra/README.md);
    4. the repository variables the workflows read (identifiers, not secrets): AZURE_* and AZURE_SERVICES_* (both this
       group now), WEB_HOST, COSMOS_ENDPOINT, AUTH_*, SCAN_URL and API_URL;
    5. then it runs the Scan server, API and CI workflows and waits for each. GitHub makes a new package private and has no
       command to change that, so the first time it opens the package's settings for you to make it public, and goes on.
  It changes nothing on the Cosmos DB account itself, in its other databases or in any other resource group.

.EXAMPLE
  .\infra\azure\setup.ps1
.EXAMPLE
  .\infra\azure\setup.ps1 -SkipDeploy      # Azure, sign-in and GitHub variables only, no workflows
.PARAMETER WebHost
  The web app's address, when it is not the Static Web App's own *.azurestaticapps.net one (a custom domain).
.PARAMETER Domain
  The sign-in tenant's domain: players sign in on <Domain>.ciamlogin.com. Letters and digits; it must be free worldwide.
#>
[CmdletBinding()]
param(
  [string]$Subscription = '[Andrey Kukalev] Visual Studio Premium',
  [ValidatePattern('^[a-z0-9]{1,26}$')][string]$Domain = 'motifflab',
  [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repository = 'andreykukalev/motiff-lab',
  [string]$WebHost,
  [string]$CosmosAccount,
  [switch]$SkipDeploy,
  [switch]$Yes
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'MotiffAzure.psm1') -Force

$SharedGroup = 'rg-shared'
$ResourceGroup = 'rg-motifflab'
$Location = 'polandcentral'
$Database = 'motifflab'
$Container = 'projects'
$Throughput = 400 # the least a database with shared throughput can have
$FreeTierRu = 1000
$ApiIdentity = 'id-motifflab-api'
$DataContributor = '00000000-0000-0000-0000-000000000002' # Cosmos DB Built-in Data Contributor
$TenantApiVersion = '2023-05-17-preview'
$ApiAppName = 'Motiff Lab API'
$WebAppName = 'Motiff Lab'
$FlowName = 'Motiff Lab sign-in'
$GraphAppId = '00000003-0000-0000-c000-000000000000'
$GraphOpenId = '37f7f235-527c-4136-accd-4a02d197296e'        # openid
$GraphOfflineAccess = '7427e0e9-2fba-42fe-b0c0-848c9e6a8182' # offline_access
$Scopes = @(
  @{ value = 'Projects.ReadWrite'; name = 'Save and read your projects'; description = 'Motiff Lab saves your projects online and reads them back on your other devices.' },
  @{ value = 'Scan'; name = 'Read printed music and recordings you send'; description = "Motiff Lab's server reads the pages and recordings you send it, and keeps none of them." }
)
$Owner = $Repository.Split('/')[0]

function Confirm-Step([string]$Question) {
  if ($Yes) { return }
  $answer = Read-Host "    $Question [y/N]"
  if ($answer -notmatch '^(y|yes)$') { Write-MotiffWarn 'Stopped: not confirmed (type y to go ahead). Nothing more was changed.'; exit 1 }
}

function Set-RepoVariable([string]$Name, [string]$Value) {
  Invoke-MotiffGh @('variable', 'set', $Name, '--repo', $Repository, '--body', $Value) | Out-Null
  Write-MotiffInfo "GitHub variable $Name = $Value"
}

function Get-DatabaseThroughput([string]$Account, [string[]]$Target) {
  # A database's or container's own RU/s, 0 when it shares its database's. Autoscale counts at its maximum, which is what
  # the free tier is measured against at the busiest hour.
  $json = Test-MotiffAz (@('cosmosdb', 'sql') + $Target + @('--account-name', $Account, '--resource-group', $SharedGroup, '-o', 'json'))
  if (-not $json) { return 0 }
  $resource = Get-MotiffField ($json | ConvertFrom-Json) 'resource'
  $autoscale = Get-MotiffField $resource 'autoscaleSettings'
  $max = Get-MotiffField $autoscale 'maxThroughput'
  if ($max) { return [int]$max }
  $fixed = Get-MotiffField $resource 'throughput'
  if ($fixed) { return [int]$fixed }
  return 0
}

Write-MotiffStep 'Checking prerequisites'
Assert-MotiffCommand -Name 'az' -Hint 'Install the Azure CLI (winget install Microsoft.AzureCLI), then reopen the terminal.'
Assert-MotiffCommand -Name 'gh' -Hint 'Install the GitHub CLI (winget install GitHub.cli), then reopen the terminal.'
$version = [version]((Invoke-MotiffAz @('version', '-o', 'json') | ConvertFrom-MotiffJson).'azure-cli')
if ($version -lt [version]'2.60.0') { throw "Azure CLI $version is too old; run 'az upgrade' (2.60 or later is needed)." }
Write-MotiffInfo "Azure CLI $version"
if ($null -eq (Test-MotiffAz @('bicep', 'version'))) { Invoke-MotiffAz @('bicep', 'install') | Out-Null }
$null = Invoke-MotiffNative { & gh auth status 2>&1 }
if ($LASTEXITCODE -ne 0) {
  Write-MotiffInfo 'Signing in to GitHub...'
  Invoke-MotiffNative { & gh auth login --hostname github.com --git-protocol https --web }
  if ($LASTEXITCODE -ne 0) { throw 'GitHub sign-in failed.' }
}
Write-MotiffInfo "GitHub repository $Repository"

Write-MotiffStep 'Signing in to Azure'
$account = Connect-MotiffAzure -Subscription $Subscription
$subscriptionId = "$($account.id)"
$homeTenant = "$($account.tenantId)"
$ArmArgs = @('--subscription', $subscriptionId)
foreach ($provider in 'Microsoft.App', 'Microsoft.ManagedIdentity', 'Microsoft.DocumentDB', 'Microsoft.AzureActiveDirectory', 'Microsoft.Web') {
  $state = "$(Invoke-MotiffAz (@('provider', 'show', '--namespace', $provider, '--query', 'registrationState', '-o', 'tsv') + $ArmArgs))".Trim()
  if ($state -ne 'Registered') {
    Write-MotiffInfo "Registering $provider for the subscription (a minute or two)..."
    Invoke-MotiffAz (@('provider', 'register', '--namespace', $provider, '--wait') + $ArmArgs) | Out-Null
  }
}

Write-MotiffStep "Cosmos DB account"
$groupExists = (Test-MotiffAz (@('group', 'exists', '--name', $ResourceGroup) + $ArmArgs)) -match 'true'
# Motiff Lab's own serverless account, for when the shared free tier has no room left (two cix databases take 800 RU/s).
$OwnAccount = "cosmos-motifflab-$($subscriptionId.Substring(0, 8))"
$CosmosGroup = $SharedGroup
$createAccount = $false
$cosmos = $null
if (-not $CosmosAccount -and $groupExists) {
  $json = Test-MotiffAz (@('cosmosdb', 'show', '--name', $OwnAccount, '--resource-group', $ResourceGroup, '-o', 'json') + $ArmArgs)
  if ($json) { $cosmos = $json | ConvertFrom-Json; $CosmosAccount = $OwnAccount; $CosmosGroup = $ResourceGroup }
}
if (-not $cosmos -and -not $CosmosAccount) {
  $free = @(Invoke-MotiffAz (@('cosmosdb', 'list', '--resource-group', $SharedGroup, '-o', 'json') + $ArmArgs) | ConvertFrom-MotiffJson |
      Where-Object { Get-MotiffField $_ 'enableFreeTier' })
  if ($free.Count -ne 1) { throw "Expected one free-tier Cosmos DB account in $SharedGroup, found $($free.Count). Name it with -CosmosAccount <name>." }
  $CosmosAccount = "$($free[0].name)"
}
if (-not $cosmos) { $cosmos = Invoke-MotiffAz (@('cosmosdb', 'show', '--name', $CosmosAccount, '--resource-group', $CosmosGroup, '-o', 'json') + $ArmArgs) | ConvertFrom-Json }
if ("$($cosmos.kind)" -ne 'GlobalDocumentDB') { throw "$CosmosAccount is a $($cosmos.kind) account; Motiff Lab needs the NoSQL API (GlobalDocumentDB)." }
$serverless = @(@(Get-MotiffField $cosmos 'capabilities') | Where-Object { $_ -and "$($_.name)" -eq 'EnableServerless' }).Count -gt 0
$databases = @(Invoke-MotiffAz (@('cosmosdb', 'sql', 'database', 'list', '--account-name', $CosmosAccount, '--resource-group', $CosmosGroup, '-o', 'json') + $ArmArgs) |
    ConvertFrom-MotiffJson | ForEach-Object { "$($_.name)" })
$databaseExists = $databases -contains $Database
if (-not $databaseExists -and -not $serverless) {
  $used = 0
  foreach ($db in $databases) {
    $shared = Get-DatabaseThroughput $CosmosAccount @('database', 'throughput', 'show', '--name', $db)
    if ($shared) { Write-MotiffInfo "database ${db}: $shared RU/s"; $used += $shared }
    $containers = @(Invoke-MotiffAz (@('cosmosdb', 'sql', 'container', 'list', '--account-name', $CosmosAccount, '--resource-group', $SharedGroup, '--database-name', $db, '-o', 'json') + $ArmArgs) |
        ConvertFrom-MotiffJson | ForEach-Object { "$($_.name)" })
    foreach ($coll in $containers) {
      $own = Get-DatabaseThroughput $CosmosAccount @('container', 'throughput', 'show', '--database-name', $db, '--name', $coll)
      if ($own) { Write-MotiffInfo "container $db/${coll}: $own RU/s"; $used += $own }
    }
  }
  Write-MotiffInfo "$CosmosAccount already uses $used of the free tier's $FreeTierRu RU/s."
  if ($used + $Throughput -gt $FreeTierRu) {
    # Its RU/s would be billed by the hour, used or not; a serverless account bills only the requests and the storage.
    Write-MotiffWarn "Adding $Throughput RU/s for $Database would go past the free tier. Motiff Lab gets its own serverless account instead, $OwnAccount in ${ResourceGroup}: no reserved RU/s, billed per request (about 0.25 USD per million RU and 0.25 USD per GB a month, cents at this use)."
    $CosmosAccount = $OwnAccount
    $CosmosGroup = $ResourceGroup
    $createAccount = $true
    $serverless = $true
    $databaseExists = $false
    $cosmos = $null
  }
}
$plan = if ($databaseExists) { "database $Database is there already" } elseif ($serverless) { "database $Database (serverless)" } else { "database $Database at $Throughput RU/s shared" }
Write-MotiffInfo "Account: $CosmosAccount ($CosmosGroup)$(if ($createAccount) { ', new, serverless' }); $plan, container $Container (partition /ownerId)"

$tenantResource = "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.AzureActiveDirectory/ciamDirectories/$Domain"
$tenantUrl = "https://management.azure.com${tenantResource}?api-version=$TenantApiVersion"
$tenant = $null
if ($groupExists) {
  $json = Test-MotiffAz @('rest', '--method', 'get', '--url', $tenantUrl, '-o', 'json')
  if ($json) { $tenant = $json | ConvertFrom-Json }
}

Write-MotiffStep 'What this creates'
Write-MotiffInfo "In $($account.name):"
Write-MotiffInfo "  - resource group $ResourceGroup ($Location) with id-motifflab-github (CI), $ApiIdentity (the API) and the web app's Static Web App swa-motifflab (Free plan)"
if ($createAccount) { Write-MotiffInfo "  - the serverless Cosmos DB account $OwnAccount in $ResourceGroup, with the $plan; $SharedGroup is left as it is" }
else { Write-MotiffInfo "  - $plan in $CosmosAccount ($CosmosGroup), nothing else there changes" }
Write-MotiffInfo "  - $(if ($tenant) { "the sign-in tenant $Domain is there already" } else { "the sign-in tenant $Domain.onmicrosoft.com (External ID, Europe, free up to 50,000 monthly users)" })"
Write-MotiffInfo "In the sign-in tenant: app registrations '$ApiAppName' and '$WebAppName', and the flow '$FlowName' (email code)"
Write-MotiffInfo "On ${Repository}: repository variables (identifiers, not secrets)$(if (-not $SkipDeploy) { ', then the Scan server, API and CI workflows' })"
Write-MotiffInfo "Standing cost: none. The apps scale to zero, and $(if ($serverless) { 'the serverless database bills only its requests and storage' } else { 'the database stays in the free tier' })."
Confirm-Step 'Go ahead?'

Write-MotiffStep 'Database'
if ($createAccount) {
  Invoke-MotiffAz (@('group', 'create', '--name', $ResourceGroup, '--location', $Location, '--tags', 'app=motiff-lab', '-o', 'none') + $ArmArgs) | Out-Null
  Write-MotiffInfo "Creating the serverless account $OwnAccount (5 to 10 minutes)..."
  Invoke-MotiffAz (@('cosmosdb', 'create', '--name', $OwnAccount, '--resource-group', $ResourceGroup,
    '--locations', "regionName=$Location", 'failoverPriority=0', 'isZoneRedundant=False',
    '--capabilities', 'EnableServerless', '--default-consistency-level', 'Session', '--tags', 'app=motiff-lab', '-o', 'none') + $ArmArgs) | Out-Null
  $cosmos = Invoke-MotiffAz (@('cosmosdb', 'show', '--name', $OwnAccount, '--resource-group', $ResourceGroup, '-o', 'json') + $ArmArgs) | ConvertFrom-Json
}
if (-not $databaseExists) {
  $create = @('cosmosdb', 'sql', 'database', 'create', '--account-name', $CosmosAccount, '--resource-group', $CosmosGroup, '--name', $Database, '-o', 'none') + $ArmArgs
  if (-not $serverless) { $create += @('--throughput', "$Throughput") }
  Invoke-MotiffAz $create | Out-Null
  Write-MotiffInfo "Created the database $Database."
}
$containerArgs = @('--account-name', $CosmosAccount, '--resource-group', $CosmosGroup, '--database-name', $Database, '--name', $Container) + $ArmArgs
if ($null -eq (Test-MotiffAz (@('cosmosdb', 'sql', 'container', 'show') + $containerArgs + @('-o', 'none')))) {
  # One container for everything a player keeps, partitioned by player, so a player's data is one partition.
  Invoke-MotiffAz (@('cosmosdb', 'sql', 'container', 'create') + $containerArgs + @('--partition-key-path', '/ownerId', '-o', 'none')) | Out-Null
  Write-MotiffInfo "Created the container $Container."
}
$cosmosEndpoint = "$($cosmos.documentEndpoint)"

Write-MotiffStep "Resource group $ResourceGroup"
Invoke-MotiffAz (@('group', 'create', '--name', $ResourceGroup, '--location', $Location, '--tags', 'app=motiff-lab', '-o', 'none') + $ArmArgs) | Out-Null
$ciClientId = "$(Invoke-MotiffAz (@('deployment', 'group', 'create', '--resource-group', $ResourceGroup, '--name', 'bootstrap',
  '--template-file', (Join-Path (Join-Path (Get-MotiffRepoRoot) 'infra') 'bootstrap.bicep'),
  '--query', 'properties.outputs.clientId.value', '-o', 'tsv') + $ArmArgs))".Trim()
Write-MotiffInfo "CI identity id-motifflab-github: $ciClientId"
Invoke-MotiffAz (@('identity', 'create', '--name', $ApiIdentity, '--resource-group', $ResourceGroup, '--location', $Location,
  '--tags', 'app=motiff-lab', 'service=api', '-o', 'none') + $ArmArgs) | Out-Null
$principal = "$(Invoke-MotiffAz (@('identity', 'show', '--name', $ApiIdentity, '--resource-group', $ResourceGroup, '--query', 'principalId', '-o', 'tsv') + $ArmArgs))".Trim()
# Data access to this database only, never to the account's other databases or its keys.
$assigned = @(Invoke-MotiffAz (@('cosmosdb', 'sql', 'role', 'assignment', 'list', '--account-name', $CosmosAccount, '--resource-group', $CosmosGroup, '-o', 'json') + $ArmArgs) |
    ConvertFrom-MotiffJson | Where-Object { "$($_.principalId)" -eq $principal })
if ($assigned.Count -eq 0) {
  Invoke-MotiffAz (@('cosmosdb', 'sql', 'role', 'assignment', 'create', '--account-name', $CosmosAccount, '--resource-group', $CosmosGroup,
    '--role-definition-id', $DataContributor, '--principal-id', $principal, '--scope', "/dbs/$Database", '-o', 'none') + $ArmArgs) | Out-Null
}
Write-MotiffInfo "$ApiIdentity may read and write the database $Database, and nothing else in $CosmosAccount."
Set-RepoVariable 'AZURE_SERVICES_CLIENT_ID' $ciClientId
Set-RepoVariable 'AZURE_SERVICES_TENANT_ID' $homeTenant
Set-RepoVariable 'AZURE_SERVICES_SUBSCRIPTION_ID' $subscriptionId
Set-RepoVariable 'AZURE_SERVICES_RESOURCE_GROUP' $ResourceGroup
Set-RepoVariable 'COSMOS_ENDPOINT' $cosmosEndpoint

Write-MotiffStep 'Web app'
# The Static Web App lives here too, next to the services (Andrei, 2026-10-30). The first one is in a subscription of
# another tenant, which Azure cannot move a resource across, so this is a new one with a new address; CI deploys to it
# from now on, signing in as the same identity as the services.
$siteHost = "$(Invoke-MotiffAz (@('deployment', 'group', 'create', '--resource-group', $ResourceGroup, '--name', 'web',
  '--template-file', (Join-Path (Join-Path (Get-MotiffRepoRoot) 'infra') 'main.bicep'),
  '--parameters', (Join-Path (Join-Path (Get-MotiffRepoRoot) 'infra') 'main.bicepparam'),
  '--query', 'properties.outputs.defaultHostName.value', '-o', 'tsv') + $ArmArgs))".Trim()
if (-not $WebHost) { $WebHost = $siteHost }
Write-MotiffInfo "Static Web App swa-motifflab: https://$siteHost"
Set-RepoVariable 'AZURE_CLIENT_ID' $ciClientId
Set-RepoVariable 'AZURE_TENANT_ID' $homeTenant
Set-RepoVariable 'AZURE_SUBSCRIPTION_ID' $subscriptionId
Set-RepoVariable 'AZURE_RESOURCE_GROUP' $ResourceGroup
# The scan server and the API answer browsers from this address and its pull request previews only.
Set-RepoVariable 'WEB_HOST' $WebHost
$RedirectUris = @(
  "https://$WebHost/authentication/login-callback",
  'https://localhost:7281/authentication/login-callback',
  'http://localhost:5112/authentication/login-callback'
)

Write-MotiffStep "Sign-in tenant $Domain"
if (-not $tenant) {
  $body = New-MotiffJsonFile ([ordered]@{
      location   = 'Europe'
      sku        = @{ name = 'Standard'; tier = 'A0' }
      properties = @{ createTenantProperties = @{ displayName = 'Motiff Lab'; countryCode = 'PL' } }
      tags       = @{ app = 'motiff-lab' }
    })
  try { Invoke-MotiffAz @('rest', '--method', 'put', '--url', $tenantUrl, '--body', "@$body", '-o', 'none') | Out-Null }
  catch { throw "$($_.Exception.Message) If the domain $Domain is taken, run this again with -Domain <another name>." }
  finally { Remove-Item $body -ErrorAction SilentlyContinue }
  Write-MotiffInfo 'Creating the tenant: this takes 5 to 30 minutes...'
}
$deadline = (Get-Date).AddMinutes(45)
while ($true) {
  $tenant = Invoke-MotiffAz @('rest', '--method', 'get', '--url', $tenantUrl, '-o', 'json') | ConvertFrom-Json
  $properties = Get-MotiffField $tenant 'properties'
  $tenantId = "$(Get-MotiffField $properties 'tenantId')"
  $state = "$(Get-MotiffField $properties 'provisioningState')"
  if ($tenantId -match '^[0-9a-fA-F-]{36}$' -and ($state -eq '' -or $state -eq 'Succeeded')) { break }
  if ($state -eq 'Failed') { throw "Creating the tenant $Domain failed. Delete $tenantResource in the portal and run this again." }
  if ((Get-Date) -gt $deadline) { throw "The tenant $Domain is still being created. Run this again later: it picks up where it stopped." }
  Write-MotiffInfo "  ...$state"
  Start-Sleep -Seconds 30
}
Write-MotiffInfo "Tenant ID: $tenantId (sign-in on $Domain.ciamlogin.com)"

$token = Get-MotiffGraphToken -TenantId $tenantId
function Get-App([string]$Name) {
  $apps = @(Get-MotiffGraphList -Token $token -Path "/applications?`$filter=displayName eq '$Name'" | Where-Object { "$($_.displayName)" -ceq $Name })
  if ($apps.Count -gt 1) { throw "More than one app registration is named '$Name' in the sign-in tenant; delete the extra ones." }
  if ($apps.Count -eq 1) { return $apps[0] }
  return $null
}
function Get-ServicePrincipal([string]$AppId, [switch]$Create) {
  $sps = @(Get-MotiffGraphList -Token $token -Path "/servicePrincipals?`$filter=appId eq '$AppId'")
  if ($sps.Count -gt 0) { return $sps[0] }
  if (-not $Create) { throw "No service principal for $AppId in the sign-in tenant." }
  return Invoke-MotiffGraph -Token $token -Method POST -Path '/servicePrincipals' -Body @{ appId = $AppId }
}

Write-MotiffStep "App registration '$ApiAppName'"
$api = Get-App $ApiAppName
if (-not $api) {
  $api = Invoke-MotiffGraph -Token $token -Method POST -Path '/applications' -Body @{ displayName = $ApiAppName; signInAudience = 'AzureADMyOrg' }
  Write-MotiffInfo "Created it: $($api.appId)"
}
$apiApp = Get-MotiffField $api 'api'
$haveScopes = @(@(Get-MotiffField $apiApp 'oauth2PermissionScopes') | Where-Object { $_ })
$scopeList = @($haveScopes)
foreach ($scope in $Scopes) {
  if (@($haveScopes | Where-Object { "$($_.value)" -ceq $scope.value }).Count -eq 0) {
    $scopeList += [pscustomobject][ordered]@{
      id = [guid]::NewGuid().ToString(); value = $scope.value; type = 'User'; isEnabled = $true
      adminConsentDisplayName = $scope.name; adminConsentDescription = $scope.description
      userConsentDisplayName = $scope.name; userConsentDescription = $scope.description
    }
  }
}
$audience = "api://$($api.appId)"
# v2 access tokens, whose audience is the API's client ID: what the API and the scan server check.
Invoke-MotiffGraph -Token $token -Method PATCH -Path "/applications/$($api.id)" -Body ([ordered]@{
    identifierUris = @($audience)
    api            = [ordered]@{ requestedAccessTokenVersion = 2; oauth2PermissionScopes = @($scopeList) }
  }) | Out-Null
$apiSp = Get-ServicePrincipal "$($api.appId)" -Create
$scopeIds = @{}
foreach ($scope in $scopeList) { $scopeIds["$($scope.value)"] = "$($scope.id)" }
Write-MotiffInfo "$audience with the scopes $(@($Scopes | ForEach-Object { $_.value }) -join ' and ')"

Write-MotiffStep "App registration '$WebAppName'"
$web = Get-App $WebAppName
if (-not $web) {
  $web = Invoke-MotiffGraph -Token $token -Method POST -Path '/applications' -Body @{ displayName = $WebAppName; signInAudience = 'AzureADMyOrg' }
  Write-MotiffInfo "Created it: $($web.appId)"
}
$uris = @(@(Get-MotiffField (Get-MotiffField $web 'spa') 'redirectUris') | Where-Object { $_ })
foreach ($uri in $RedirectUris) { if ($uris -notcontains $uri) { $uris += $uri } }
Invoke-MotiffGraph -Token $token -Method PATCH -Path "/applications/$($web.id)" -Body ([ordered]@{
    spa                    = @{ redirectUris = @($uris) }
    requiredResourceAccess = @(
      [ordered]@{ resourceAppId = "$($api.appId)"; resourceAccess = @($Scopes | ForEach-Object { @{ id = $scopeIds[$_.value]; type = 'Scope' } }) },
      [ordered]@{ resourceAppId = $GraphAppId; resourceAccess = @(@{ id = $GraphOpenId; type = 'Scope' }, @{ id = $GraphOfflineAccess; type = 'Scope' }) }
    )
  }) | Out-Null
$webSp = Get-ServicePrincipal "$($web.appId)" -Create
Write-MotiffInfo "Signs in at $($RedirectUris -join ', ')"
# Players of an external tenant can't consent themselves: the tenant consents once for everyone (admin consent).
foreach ($grant in @(
    @{ resource = $apiSp; scope = (@($Scopes | ForEach-Object { $_.value }) -join ' ') },
    @{ resource = (Get-ServicePrincipal $GraphAppId -Create); scope = 'openid offline_access' }
  )) {
  $resourceId = "$($grant.resource.id)"
  $existing = @(Get-MotiffGraphList -Token $token -Path "/oauth2PermissionGrants?`$filter=clientId eq '$($webSp.id)'" |
      Where-Object { "$($_.resourceId)" -eq $resourceId -and "$($_.consentType)" -eq 'AllPrincipals' })
  if ($existing.Count -eq 0) {
    Invoke-MotiffGraph -Token $token -Method POST -Path '/oauth2PermissionGrants' -Body @{
      clientId = "$($webSp.id)"; consentType = 'AllPrincipals'; resourceId = $resourceId; scope = $grant.scope
    } | Out-Null
  }
  elseif ("$($existing[0].scope)".Trim() -ne $grant.scope) {
    Invoke-MotiffGraph -Token $token -Method PATCH -Path "/oauth2PermissionGrants/$($existing[0].id)" -Body @{ scope = $grant.scope } | Out-Null
  }
}
Write-MotiffInfo "Consent given for every player: $(@($Scopes | ForEach-Object { $_.value }) -join ', '), openid, offline_access"

Write-MotiffStep "Sign-in flow '$FlowName'"
$flows = Invoke-MotiffGraph -Token $token -Method GET -Path '/identity/authenticationEventsFlows' -AllowForbidden
if ("$flows" -eq 'forbidden') {
  # The Azure CLI's sign-in may not be allowed to manage user flows in every tenant: then this one step is by hand.
  Write-MotiffWarn 'The Azure CLI may not create sign-in flows here. Make it in the Entra admin center, once (2 minutes):'
  Write-MotiffInfo "  1. https://entra.microsoft.com, switch to the tenant Motiff Lab ($tenantId) with the gear icon."
  Write-MotiffInfo "  2. External Identities, User flows, New user flow: name '$FlowName', Email Accounts, Email one-time passcode, no attributes beyond email. Create."
  Write-MotiffInfo "  3. Open the flow, Applications, Add application, '$WebAppName'."
  $null = Read-Host '    Press Enter when it is done'
}
else {
  $flow = @(@(Get-MotiffField $flows 'value') | Where-Object { $_ -and "$($_.displayName)" -ceq $FlowName })
  if ($flow.Count -eq 0) {
    $flow = Invoke-MotiffGraph -Token $token -Method POST -Path '/identity/authenticationEventsFlows' -Body ([ordered]@{
        '@odata.type'                   = '#microsoft.graph.externalUsersSelfServiceSignUpEventsFlow'
        displayName                     = $FlowName
        description                     = 'Sign up and sign in to Motiff Lab with a code sent by email.'
        onAuthenticationMethodLoadStart = [ordered]@{
          '@odata.type'     = '#microsoft.graph.onAuthenticationMethodLoadStartExternalUsersSelfServiceSignUp'
          identityProviders = @(@{ id = 'EmailOtpSignup-OAUTH' })
        }
        onInteractiveAuthFlowStart      = [ordered]@{
          '@odata.type'   = '#microsoft.graph.onInteractiveAuthFlowStartExternalUsersSelfServiceSignUp'
          isSignUpAllowed = $true
        }
        onAttributeCollection           = [ordered]@{
          '@odata.type'           = '#microsoft.graph.onAttributeCollectionExternalUsersSelfServiceSignUp'
          attributes              = @([ordered]@{ id = 'email'; displayName = 'Email Address'; description = 'Email address of the user'; userFlowAttributeType = 'builtIn'; dataType = 'string' })
          attributeCollectionPage = @{
            views = @(@{
                inputs = @([ordered]@{ attribute = 'email'; label = 'Email Address'; inputType = 'text'; hidden = $true; editable = $false; writeToDirectory = $true; required = $true })
              })
          }
        }
      })
    Write-MotiffInfo 'Created it.'
  }
  else { $flow = $flow[0] }
  $included = @(Get-MotiffGraphList -Token $token -Path "/identity/authenticationEventsFlows/$($flow.id)/conditions/applications/includeApplications")
  if (@($included | Where-Object { "$($_.appId)" -eq "$($web.appId)" }).Count -eq 0) {
    Invoke-MotiffGraph -Token $token -Method POST -Path "/identity/authenticationEventsFlows/$($flow.id)/conditions/applications/includeApplications" -Body @{
      '@odata.type' = '#microsoft.graph.authenticationConditionApplication'; appId = "$($web.appId)"
    } | Out-Null
  }
  Write-MotiffInfo "'$WebAppName' signs players in with a code sent to their email."
}

Write-MotiffStep 'GitHub variables for sign-in'
Set-RepoVariable 'AUTH_AUTHORITY' "https://$Domain.ciamlogin.com/$tenantId/v2.0"
Set-RepoVariable 'AUTH_AUDIENCE' "$($api.appId)"
Set-RepoVariable 'AUTH_CLIENT_ID' "$($web.appId)"

if ($SkipDeploy) {
  Write-MotiffStep 'Done (without deploying)'
  Write-MotiffInfo 'Run this again without -SkipDeploy to deploy the scan server, the API and the web app.'
  return
}

function Invoke-Workflow([string]$File, [string]$Package, [string]$Label, [string[]]$Inputs = @()) {
  # Runs a workflow on main and waits for it. A new package is private, and GitHub has no command to make it public: the
  # first time, its settings page opens, and the run is repeated once you have changed it. Returns the run's log.
  Write-MotiffStep "Workflow $File"
  $started = (Get-Date).ToUniversalTime().AddSeconds(-30)
  Invoke-MotiffGh (@('workflow', 'run', $File, '--repo', $Repository, '--ref', 'main') + @($Inputs | ForEach-Object { @('-f', $_) })) | Out-Null
  $run = $null
  for ($i = 0; $i -lt 20 -and -not $run; $i++) {
    Start-Sleep -Seconds 3
    $run = @(Invoke-MotiffGh @('run', 'list', '--repo', $Repository, '--workflow', $File, '--event', 'workflow_dispatch', '--limit', '5', '--json', 'databaseId,createdAt') |
        ConvertFrom-MotiffJson | Where-Object { ([datetime]$_.createdAt).ToUniversalTime() -ge $started }) | Select-Object -First 1
  }
  if (-not $run) { throw "The $File run didn't show up; see the Actions tab of $Repository." }
  $id = "$($run.databaseId)"
  Write-MotiffInfo "https://github.com/$Repository/actions/runs/$id"
  for ($attempt = 1; ; $attempt++) {
    # Polled rather than `gh run watch`, whose screen refreshes would end up in what this function returns.
    $last = ''
    while ($true) {
      $state = Invoke-MotiffGh @('run', 'view', $id, '--repo', $Repository, '--json', 'status,conclusion') | ConvertFrom-Json
      $now = "$($state.status) $($state.conclusion)".Trim()
      if ($now -ne $last) { Write-MotiffInfo "  ...$now"; $last = $now }
      if ("$($state.status)" -eq 'completed') { break }
      Start-Sleep -Seconds 20
    }
    if ("$($state.conclusion)" -eq 'success') { break }
    $failed = Invoke-MotiffNative { & gh run view $id --repo $Repository --log-failed 2>&1 } | Out-String
    if (-not $Package -or $failed -notmatch 'is private' -or $attempt -gt 3) { throw "The $File run failed: https://github.com/$Repository/actions/runs/$id" }
    $settingsPage = "https://github.com/users/$Owner/packages/container/$Package/settings"
    Write-MotiffWarn "The package $Package is private. On its settings page: Change visibility, Public, confirm with its name."
    Write-MotiffInfo $settingsPage
    try { Start-Process $settingsPage } catch { }
    $null = Read-Host '    Press Enter once it is public'
    Invoke-MotiffGh @('run', 'rerun', $id, '--repo', $Repository, '--failed') | Out-Null
    Start-Sleep -Seconds 5
  }
  $log = Invoke-MotiffNative { & gh run view $id --repo $Repository --log 2>&1 } | Out-String
  if ($Label) {
    $found = [regex]::Matches($log, "$Label`: (https://[A-Za-z0-9.-]+)")
    if ($found.Count -eq 0) { throw "The $File run finished but printed no address; see https://github.com/$Repository/actions/runs/$id" }
    return $found[$found.Count - 1].Groups[1].Value
  }
}

$scanUrl = Invoke-Workflow 'scan-server.yml' 'motiff-omr' 'Scan server'
Write-MotiffInfo "Scan server: $scanUrl"
$apiUrl = Invoke-Workflow 'api.yml' 'motiff-api' 'API'
Write-MotiffInfo "API: $apiUrl"
Set-RepoVariable 'SCAN_URL' $scanUrl
Set-RepoVariable 'API_URL' $apiUrl
# The web app is built with these settings (CI writes appsettings.json from the variables) and deployed.
# Without the browser tests, which run only when asked.
Invoke-Workflow 'ci.yml' '' '' @('browser_tests=false') | Out-Null

Write-MotiffStep 'Done'
Write-MotiffInfo "Web app:     https://$WebHost (Settings: Save your projects online)"
Write-MotiffInfo "Scan server: $scanUrl"
Write-MotiffInfo "API:         $apiUrl"
Write-MotiffInfo "Sign-in:     https://$Domain.ciamlogin.com, tenant $tenantId"
