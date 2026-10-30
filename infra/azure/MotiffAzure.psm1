#Requires -Version 5.1
<#
  Helpers for infra/azure/setup.ps1.
  - Azure is reached through the Azure CLI (az), signed in as you; Microsoft Graph with a token az gets for the sign-in
    tenant; GitHub through the GitHub CLI (gh). Nothing here is a secret: the script writes identifiers only.
  - Runs on Windows PowerShell 5.1 and PowerShell 7: no 7-only syntax or parameters.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RepoRoot = (Resolve-Path (Join-Path (Join-Path $PSScriptRoot '..') '..')).Path
$script:Utf8NoBom = New-Object System.Text.UTF8Encoding $false
# Windows PowerShell 5.1 may still offer only TLS 1.0 by default; Microsoft Graph needs 1.2.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

function Get-MotiffRepoRoot { $script:RepoRoot }

function Write-MotiffStep([string]$Text) { Write-Host ''; Write-Host "==> $Text" -ForegroundColor Cyan }
function Write-MotiffInfo([string]$Text) { Write-Host "    $Text" }
function Write-MotiffWarn([string]$Text) { Write-Host "    ! $Text" -ForegroundColor Yellow }

function Assert-MotiffCommand {
  param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$Hint)
  if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) { throw "$Name is not installed. $Hint" }
}

function ConvertFrom-MotiffJson {
  <#
    ConvertFrom-Json that sends an array's items down the pipeline one by one on every version (Windows PowerShell 5.1 sends
    the whole array as one object, which breaks Where-Object filters).
  #>
  param([Parameter(Mandatory, ValueFromPipeline)][AllowEmptyString()][string]$Json)
  process {
    if (-not $Json.Trim()) { return }
    $value = $Json | ConvertFrom-Json
    if ($null -eq $value) { return }
    if ($value -is [System.Array]) { foreach ($item in $value) { $item } } else { $value }
  }
}

function Get-MotiffField {
  <# A property of a parsed JSON object, or $null when it has none (Set-StrictMode refuses a missing one). #>
  param([AllowNull()][object]$Object, [Parameter(Mandatory)][string]$Name)
  if ($null -ne $Object -and $Object.PSObject.Properties[$Name]) { return $Object.$Name }
  return $null
}

function Invoke-MotiffNative {
  <#
    Runs a native command. Windows PowerShell 5.1 can turn each stderr line into a terminating error when $ErrorActionPreference
    is Stop, and az and gh write warnings there; so the call runs with Continue, and callers check $LASTEXITCODE. The
    preference is relaxed in the caller's scope, because a script block from a script runs in that script's scope.
  #>
  [CmdletBinding()]
  param([Parameter(Mandatory)][scriptblock]$Command)
  $callerVariables = $PSCmdlet.SessionState.PSVariable
  $saved = $callerVariables.GetValue('ErrorActionPreference')
  $ErrorActionPreference = 'Continue'
  $callerVariables.Set('ErrorActionPreference', 'Continue')
  try {
    $global:LASTEXITCODE = -1 # a command that never started counts as failed, not as the last success
    & $Command
  }
  finally {
    $callerVariables.Set('ErrorActionPreference', $saved)
  }
}

function Invoke-MotiffAz {
  <# Runs the Azure CLI and returns its output. On failure throws with az's message. #>
  param([Parameter(Mandatory)][string[]]$Arguments)
  $output = Invoke-MotiffNative { & az @Arguments 2>&1 }
  if ($LASTEXITCODE -ne 0) {
    $message = ($output | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] } | ForEach-Object { $_.ToString() }) -join ' '
    throw "az $($Arguments[0..1] -join ' ') failed: $message"
  }
  return ($output | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] }) -join "`n"
}

function Test-MotiffAz {
  <# Runs the Azure CLI and returns its output, or $null when it fails (a resource that isn't there yet). #>
  param([Parameter(Mandatory)][string[]]$Arguments)
  $output = Invoke-MotiffNative { & az @Arguments 2>&1 }
  if ($LASTEXITCODE -ne 0) { return $null }
  return ($output | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] }) -join "`n"
}

function Invoke-MotiffGh {
  <# Runs the GitHub CLI and returns its output. On failure throws with gh's message. #>
  param([Parameter(Mandatory)][string[]]$Arguments)
  $output = Invoke-MotiffNative { & gh @Arguments 2>&1 }
  if ($LASTEXITCODE -ne 0) {
    $message = ($output | ForEach-Object { $_.ToString() }) -join ' '
    throw "gh $($Arguments[0..1] -join ' ') failed: $message"
  }
  return ($output | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] }) -join "`n"
}

function New-MotiffJsonFile {
  <# Writes a request body to a temporary file for az's @file: inline JSON doesn't survive Windows PowerShell 5.1's quoting. #>
  param([Parameter(Mandatory)][object]$Value)
  $path = Join-Path ([IO.Path]::GetTempPath()) "motiff-$([guid]::NewGuid().ToString('N')).json"
  [IO.File]::WriteAllText($path, (ConvertTo-Json -InputObject $Value -Depth 12), $script:Utf8NoBom)
  return $path
}

function Connect-MotiffAzure {
  <#
    Selects the subscription by name or ID, signing in first when it isn't visible to the current sign-in (a work account
    that can't see a personal subscription, say). Returns the selected account.
  #>
  param([Parameter(Mandatory)][string]$Subscription)
  if ($null -eq (Test-MotiffAz @('account', 'set', '--subscription', $Subscription))) {
    Write-MotiffInfo "Signing in to Azure (a browser window opens): choose the account that owns '$Subscription'."
    Invoke-MotiffNative { & az login --only-show-errors -o none }
    if ($LASTEXITCODE -ne 0) { throw 'Azure sign-in failed.' }
    if ($null -eq (Test-MotiffAz @('account', 'set', '--subscription', $Subscription))) {
      throw "This sign-in can't see the subscription '$Subscription'. Run 'az logout', then run this again and sign in with the account that owns it."
    }
  }
  $account = Invoke-MotiffAz @('account', 'show', '-o', 'json') | ConvertFrom-MotiffJson
  Write-MotiffInfo "Subscription: $($account.name) ($($account.id))"
  return $account
}

function Get-MotiffGraphToken {
  <# A Microsoft Graph token for the tenant, signing in to it first when az has no sign-in there yet. #>
  param([Parameter(Mandatory)][string]$TenantId)
  $arguments = @('account', 'get-access-token', '--tenant', $TenantId, '--resource-type', 'ms-graph', '--query', 'accessToken', '-o', 'tsv')
  $token = Test-MotiffAz $arguments
  if (-not $token) {
    Write-MotiffInfo 'Signing in to the sign-in tenant (a browser window opens): choose the same account as before.'
    Invoke-MotiffNative { & az login --tenant $TenantId --allow-no-subscriptions --only-show-errors -o none }
    if ($LASTEXITCODE -ne 0) { throw "Sign-in to the tenant $TenantId failed." }
    $token = Invoke-MotiffAz $arguments
  }
  return "$token".Trim()
}

function Invoke-MotiffGraph {
  <#
    Calls Microsoft Graph v1.0 in the sign-in tenant. Returns the parsed response, or $null for an empty one. -AllowForbidden
    returns 'forbidden' instead of throwing when the sign-in may not make this call.
  #>
  param(
    [Parameter(Mandatory)][string]$Token,
    [Parameter(Mandatory)][ValidateSet('GET', 'POST', 'PATCH')][string]$Method,
    [Parameter(Mandatory)][string]$Path,
    [object]$Body,
    [switch]$AllowForbidden
  )
  $request = @{
    Method      = $Method
    Uri         = "https://graph.microsoft.com/v1.0$Path"
    Headers     = @{ Authorization = "Bearer $Token" }
    ContentType = 'application/json; charset=utf-8'
    UseBasicParsing = $true
  }
  if ($null -ne $Body) { $request.Body = $script:Utf8NoBom.GetBytes((ConvertTo-Json -InputObject $Body -Depth 12)) }
  try {
    $response = Invoke-WebRequest @request
  }
  catch {
    $status = 0
    if ($_.Exception.PSObject.Properties['Response'] -and $_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
    if ($AllowForbidden -and $status -eq 403) { return 'forbidden' }
    $detail = if ($_.ErrorDetails -and $_.ErrorDetails.Message) { $_.ErrorDetails.Message } else { $_.Exception.Message }
    throw "Microsoft Graph $Method $Path failed ($status): $detail"
  }
  $content = if ($response.Content -is [byte[]]) { $script:Utf8NoBom.GetString($response.Content) } else { "$($response.Content)" }
  if (-not $content.Trim()) { return $null }
  return $content | ConvertFrom-Json
}

function Get-MotiffGraphList {
  <# Every item of a Graph collection, following its pages. #>
  param([Parameter(Mandatory)][string]$Token, [Parameter(Mandatory)][string]$Path)
  $items = @()
  $page = Invoke-MotiffGraph -Token $Token -Method GET -Path $Path
  while ($true) {
    $items += @(Get-MotiffField $page 'value' | Where-Object { $_ })
    $next = Get-MotiffField $page '@odata.nextLink'
    if (-not $next) { break }
    $page = Invoke-MotiffGraph -Token $Token -Method GET -Path ($next -replace '^https://graph\.microsoft\.com/v1\.0', '')
  }
  return $items
}
