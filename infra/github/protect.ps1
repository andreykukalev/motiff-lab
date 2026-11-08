#Requires -Version 5.1
<#
.SYNOPSIS
  Sets up the GitHub guards of the agents' soft mode (governance/roles/common.md, "Guards"), once. Run it from a
  clone of the repository on your own computer, signed in to the GitHub CLI as the repository's owner; running it again
  is safe and brings the settings back to what is written here.

.DESCRIPTION
  In the repository (nothing in Azure, nothing in other repositories):
    1. a ruleset "main" on the default branch, with an empty bypass list, so it holds for everyone, the owner and the
       agents that act with the owner's token included: changes only through a pull request (0 approvals, because the
       agents open pull requests as the owner and an author cannot approve their own), the CI check build-test must
       pass (pinned to the app that reports it, GitHub Actions), no force push, no deletion;
    2. the Environment production: the owner is its required reviewer and only main may deploy to it. The workflows
       api.yml and scan-server.yml wait for it once the foundation's pull request adds their approve job; until then
       nothing uses it;
    3. the labels the process uses (agent, role:*, risk:*, aom:*, defect, env-change).
  It changes no workflow, variable, secret or setting beyond these. To switch the ruleset off for a while, run it
  with -Enforcement disabled; to remove the Environment, use Settings > Environments.

.EXAMPLE
  .\infra\github\protect.ps1
.EXAMPLE
  .\infra\github\protect.ps1 -DryRun            # shows what it would write, writes nothing
.EXAMPLE
  .\infra\github\protect.ps1 -Enforcement disabled
.PARAMETER CheckName
  The required check: the CI job's name (.github/workflows/ci.yml, job build-test).
.PARAMETER Reviewer
  The GitHub login that approves deploys to Azure.
#>
[CmdletBinding()]
param(
  [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repository = 'andreykukalev/motiff-lab',
  [string]$Reviewer = 'andreykukalev',
  [string]$RulesetName = 'main',
  [string]$CheckName = 'build-test',
  [string]$EnvironmentName = 'production',
  [ValidateSet('active', 'disabled')][string]$Enforcement = 'active',
  [switch]$SkipRuleset,
  [switch]$SkipEnvironment,
  [switch]$SkipLabels,
  [switch]$DryRun,
  [switch]$Yes
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$GitHubActionsAppId = 15368 # the app that reports workflow checks; used when main has no build-test run to read it from
$Utf8NoBom = New-Object System.Text.UTF8Encoding $false

function Write-Step([string]$Text) { Write-Host ''; Write-Host "==> $Text" -ForegroundColor Cyan }
function Write-Info([string]$Text) { Write-Host "    $Text" }
function Write-Warn([string]$Text) { Write-Host "    ! $Text" -ForegroundColor Yellow }

function Confirm-Step([string]$Question) {
  if ($Yes) { return }
  $answer = Read-Host "    $Question [y/N]"
  if ($answer -notmatch '^(y|yes)$') { Write-Warn 'Stopped: not confirmed (type y to go ahead). Nothing more was changed.'; exit 1 }
}

function Invoke-Native([scriptblock]$Command) {
  # Windows PowerShell 5.1 turns stderr lines into errors under Stop; gh writes notes there. Callers check $LASTEXITCODE.
  $saved = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try { $global:LASTEXITCODE = -1; & $Command } finally { $ErrorActionPreference = $saved }
}

function Invoke-Gh([string[]]$Arguments) {
  <# Runs the GitHub CLI and returns its output. On failure throws with gh's message. #>
  $output = Invoke-Native { & gh @Arguments 2>&1 }
  if ($LASTEXITCODE -ne 0) {
    $message = ($output | ForEach-Object { $_.ToString() }) -join ' '
    throw "gh $($Arguments[0..1] -join ' ') failed: $message"
  }
  return ($output | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] }) -join "`n"
}

function Test-Gh([string[]]$Arguments) {
  <# Like Invoke-Gh, but returns $null on failure (a 404 answers "there is none"). #>
  $output = Invoke-Native { & gh @Arguments 2>$null }
  if ($LASTEXITCODE -ne 0) { return $null }
  return ($output -join "`n")
}

function Invoke-GhJson([string]$Method, [string]$Path, [object]$Body) {
  <# Sends $Body as JSON through gh api, from a file, so quoting never depends on the shell. #>
  $file = [System.IO.Path]::GetTempFileName()
  try {
    [System.IO.File]::WriteAllText($file, ($Body | ConvertTo-Json -Depth 10), $Utf8NoBom)
    return Invoke-Gh @('api', '--method', $Method, $Path, '--input', $file, '-H', 'Accept: application/vnd.github+json')
  }
  finally { Remove-Item $file -ErrorAction SilentlyContinue }
}

function Get-Field([object]$Object, [string]$Name) {
  if ($null -ne $Object -and $Object.PSObject.Properties[$Name]) { return $Object.$Name }
  return $null
}

Write-Step 'Checking prerequisites'
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'gh is not installed. Install the GitHub CLI (winget install GitHub.cli), then reopen the terminal.' }
$null = Invoke-Native { & gh auth status 2>&1 }
if ($LASTEXITCODE -ne 0) {
  Write-Info 'Signing in to GitHub...'
  Invoke-Native { & gh auth login --hostname github.com --git-protocol https --web }
  if ($LASTEXITCODE -ne 0) { throw 'GitHub sign-in failed.' }
}
$login = (Invoke-Gh @('api', 'user', '--jq', '.login')).Trim()
$repo = Invoke-Gh @('api', "repos/$Repository") | ConvertFrom-Json
if (-not (Get-Field (Get-Field $repo 'permissions') 'admin')) { throw "$login is not an admin of $Repository; only an admin can set rulesets and Environments." }
$defaultBranch = "$($repo.default_branch)"
Write-Info "GitHub $login, repository $Repository (default branch $defaultBranch, $($repo.visibility))"

Write-Step 'What is there now'
$classic = Test-Gh @('api', "repos/$Repository/branches/$defaultBranch/protection")
if ($classic) { Write-Warn "$defaultBranch also has a classic branch protection rule; it stays, and both apply." } else { Write-Info "No classic branch protection on $defaultBranch." }
$rulesets = @((Invoke-Gh @('api', "repos/$Repository/rulesets")) | ConvertFrom-Json | ForEach-Object { $_ })
foreach ($r in $rulesets) { Write-Info "Ruleset '$($r.name)' (id $($r.id), $($r.enforcement), target $($r.target))" }
if (-not $rulesets) { Write-Info 'No rulesets.' }
$existing = $rulesets | Where-Object { $_.name -eq $RulesetName } | Select-Object -First 1
$environment = Test-Gh @('api', "repos/$Repository/environments/$EnvironmentName")
if ($environment) { Write-Info "Environment $EnvironmentName exists; it is brought back to the settings below." } else { Write-Info "No Environment $EnvironmentName." }

if (-not $SkipRuleset) {
  Write-Step "Ruleset '$RulesetName' on $defaultBranch"
  # The required check is pinned to the app that reports it, so a check of the same name from elsewhere does not count.
  $appId = $null
  $runs = Test-Gh @('api', "repos/$Repository/commits/$defaultBranch/check-runs?check_name=$CheckName&per_page=1")
  if ($runs) {
    $run = @(($runs | ConvertFrom-Json).check_runs) | Select-Object -First 1
    if ($run) { $appId = [int]$run.app.id; Write-Info "Check $CheckName is reported by $($run.app.name) (app id $appId)." }
  }
  if (-not $appId) { $appId = $GitHubActionsAppId; Write-Warn "No $CheckName run on $defaultBranch to read its app from; using GitHub Actions ($appId)." }
  $ruleset = [ordered]@{
    name = $RulesetName
    target = 'branch'
    enforcement = $Enforcement
    bypass_actors = @()
    conditions = [ordered]@{ ref_name = [ordered]@{ include = @('~DEFAULT_BRANCH'); exclude = @() } }
    rules = @(
      [ordered]@{ type = 'deletion' },
      [ordered]@{ type = 'non_fast_forward' },
      [ordered]@{
        type = 'pull_request'
        parameters = [ordered]@{
          required_approving_review_count = 0
          dismiss_stale_reviews_on_push = $false
          require_code_owner_review = $false
          require_last_push_approval = $false
          required_review_thread_resolution = $false
        }
      },
      [ordered]@{
        type = 'required_status_checks'
        parameters = [ordered]@{
          strict_required_status_checks_policy = $false
          required_status_checks = @([ordered]@{ context = $CheckName; integration_id = $appId })
        }
      }
    )
  }
  Write-Info 'Bypass list: empty (applies to everyone, the owner included)'
  Write-Info "Rules: pull request (0 approvals), check $CheckName must pass, no force push, no deletion; enforcement $Enforcement"
  if ($DryRun) {
    Write-Host ($ruleset | ConvertTo-Json -Depth 10)
  }
  else {
    if ($existing) { Confirm-Step "Update ruleset '$RulesetName' (id $($existing.id))?" } else { Confirm-Step "Create ruleset '$RulesetName'?" }
    if ($existing) { $saved = Invoke-GhJson 'PUT' "repos/$Repository/rulesets/$($existing.id)" $ruleset | ConvertFrom-Json }
    else { $saved = Invoke-GhJson 'POST' "repos/$Repository/rulesets" $ruleset | ConvertFrom-Json }
    # Read it back: what GitHub keeps is what counts.
    $check = Invoke-Gh @('api', "repos/$Repository/rulesets/$($saved.id)") | ConvertFrom-Json
    $types = @($check.rules | ForEach-Object { $_.type }) -join ', '
    $bypass = @(Get-Field $check 'bypass_actors' | Where-Object { $_ }).Count
    Write-Info "Saved: id $($check.id), $($check.enforcement), rules $types, bypass actors $bypass"
    if ($bypass -ne 0) { Write-Warn 'The bypass list is not empty: remove the actors in Settings > Rules > Rulesets.' }
  }
}

if (-not $SkipEnvironment) {
  Write-Step "Environment $EnvironmentName"
  $reviewerId = [long](Invoke-Gh @('api', "users/$Reviewer", '--jq', '.id')).Trim()
  # prevent_self_review stays off: the deploy runs after the owner's own merge, and the owner is the only reviewer.
  $settings = [ordered]@{
    wait_timer = 0
    prevent_self_review = $false
    reviewers = @([ordered]@{ type = 'User'; id = $reviewerId })
    deployment_branch_policy = [ordered]@{ protected_branches = $false; custom_branch_policies = $true }
  }
  Write-Info "Required reviewer $Reviewer (id $reviewerId); deploys only from $defaultBranch"
  if ($DryRun) {
    Write-Host ($settings | ConvertTo-Json -Depth 10)
  }
  else {
    Confirm-Step "Create or update Environment ${EnvironmentName}?"
    $null = Invoke-GhJson 'PUT' "repos/$Repository/environments/$EnvironmentName" $settings
    $policies = Invoke-Gh @('api', "repos/$Repository/environments/$EnvironmentName/deployment-branch-policies") | ConvertFrom-Json
    $names = @($policies.branch_policies | ForEach-Object { $_.name })
    if ($names -notcontains $defaultBranch) {
      $null = Invoke-GhJson 'POST' "repos/$Repository/environments/$EnvironmentName/deployment-branch-policies" ([ordered]@{ name = $defaultBranch; type = 'branch' })
    }
    foreach ($name in $names) { if ($name -ne $defaultBranch) { Write-Warn "Environment $EnvironmentName also allows '$name'; remove it in Settings > Environments if that is not meant." } }
    $check = Invoke-Gh @('api', "repos/$Repository/environments/$EnvironmentName") | ConvertFrom-Json
    $rules = @($check.protection_rules | ForEach-Object { $_.type }) -join ', '
    Write-Info "Saved: protection rules $rules"
  }
}

if (-not $SkipLabels) {
  Write-Step 'Labels'
  $labels = @(
    @('agent', '5319e7', 'Opened or changed by an agent (soft mode: the identity is the owner)'),
    @('role:developer', 'c5def5', 'Agent role in this pull request'),
    @('role:reviewer', 'c5def5', 'Agent role in this pull request'),
    @('role:qa', 'c5def5', 'Agent role in this pull request'),
    @('task', '0e8a16', 'Task Record (.github/ISSUE_TEMPLATE/task-record.yml)'),
    @('risk:low', 'bfdadc', 'Risk class (AOM)'),
    @('risk:standard', 'fef2c0', 'Risk class (AOM)'),
    @('risk:high', 'f9d0c4', 'Risk class (AOM)'),
    @('risk:guardrail', 'b60205', 'Risk class (AOM): changes the rules agents work under'),
    @('aom:criteria-approved', '1d76db', 'Andrei approved the acceptance criteria; only Andrei sets it'),
    @('aom:drill', 'd4c5f9', 'Stop drill (metric M8); Andrei adds it only after the task ends'),
    @('defect', 'd73a4a', 'Defect found after merge; name the task or PR that caused it (metric M2)'),
    @('defect:serious', 'b60205', 'A defect at the High level of failure consequences (metric M2)'),
    @('env-change', 'fbca04', 'Changes the agents environment (E1-E3)')
  )
  foreach ($label in $labels) {
    if ($DryRun) { Write-Info "label $($label[0])"; continue }
    $null = Invoke-Gh @('label', 'create', $label[0], '--repo', $Repository, '--color', $label[1], '--description', $label[2], '--force')
    Write-Info "label $($label[0])"
  }
}

Write-Step 'Done'
if ($DryRun) { Write-Info 'Dry run: nothing was written.' }
Write-Info 'Next: open the foundation pull request (the approve job in api.yml and scan-server.yml, .claude, governance) and merge it.'
Write-Info 'Check after the merge: a pull request cannot merge while build-test is red, a push to main is refused,'
Write-Info "and a deploy of the API or the scan server waits for you under Actions (Review deployments)."
