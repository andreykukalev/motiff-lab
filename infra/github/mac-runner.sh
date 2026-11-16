#!/usr/bin/env bash
# Sets up this Mac as the repository's self-hosted GitHub Actions runner for the iPad workflows (docs/repository-structure.md,
# "iPad builds"). One command, run on Andrei's Mac from anywhere in the clone, as the user the runner runs as:
#
#   infra/github/mac-runner.sh            # set up, start, route the iPad workflows here and start one test run on develop
#   infra/github/mac-runner.sh --remove   # move the runs back to GitHub's macOS and remove the runner
#
# What it does: checks Xcode (license, an iPad simulator), Homebrew, XcodeGen and the GitHub CLI (signs in once in the
# browser); downloads the latest runner to ~/actions-runner and registers it as "macbook" with the label motiff-mac;
# installs it as a launchd service of this user (it runs while the user is signed in, and starts again after a restart
# and sign-in); keeps the Mac awake on the charger; requires approval for runs from outside contributors; sets the
# repository variable IPAD_RUNNER=motiff-mac, which the iPad workflows read; starts the iPad workflow on develop.
# sudo asks for this user's password twice at most: the Xcode license and the power setting.
# The lid must stay open (or an external display connected): a closed MacBook sleeps and the runs wait in the queue.
set -euo pipefail

repo=andreykukalev/motiff-lab
name=macbook
label=motiff-mac
dir="$HOME/actions-runner"

say() { printf '\n== %s\n' "$*"; }
fail() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }

[[ "$(uname -s)" == Darwin && "$(uname -m)" == arm64 ]] || fail "this script is for an Apple Silicon Mac"

if [[ -x /opt/homebrew/bin/brew ]]; then eval "$(/opt/homebrew/bin/brew shellenv)"; fi
command -v brew >/dev/null || fail "Homebrew is missing: install it from https://brew.sh and run the script again"
command -v gh >/dev/null || { say "Installing the GitHub CLI"; brew install gh; }
gh auth status >/dev/null 2>&1 || { say "Sign in to GitHub (opens the browser)"; gh auth login --hostname github.com --git-protocol https --web; }

if [[ "${1:-}" == --remove ]]; then
  say "Moving the iPad runs back to GitHub's macOS"
  gh variable delete IPAD_RUNNER -R "$repo" || true
  if [[ -f "$dir/.runner" ]]; then
    cd "$dir"
    ./svc.sh stop || true
    ./svc.sh uninstall || true
    token=$(gh api -X POST "repos/$repo/actions/runners/remove-token" --jq .token)
    ./config.sh remove --token "$token"
  fi
  say "Done: the runner is removed; $dir can be deleted"
  exit 0
fi

say "Checking Xcode"
xcodebuild -version || fail "Xcode is missing or xcode-select points to the Command Line Tools: open Xcode once, or run sudo xcode-select -s /Applications/Xcode.app"
if ! xcodebuild -checkFirstLaunchStatus >/dev/null 2>&1; then
  say "Accepting the Xcode license and finishing its first launch (sudo asks for your password)"
  sudo xcodebuild -license accept
  sudo xcodebuild -runFirstLaunch
fi
xcrun simctl list devices available | grep -q "iPad" ||
  fail "no iPad simulator: in Xcode > Settings > Components install the iOS platform, then run the script again"
command -v xcodegen >/dev/null || { say "Installing XcodeGen"; brew install xcodegen; }
if command -v dotnet >/dev/null; then dotnet --version; else echo "No .NET SDK here: the workflow installs it (setup-dotnet)"; fi

if [[ -f "$dir/.runner" ]]; then
  say "The runner is already registered in $dir"
else
  say "Downloading the runner"
  version=$(gh api repos/actions/runner/releases/latest --jq .tag_name)
  version=${version#v}
  mkdir -p "$dir"
  cd "$dir"
  curl -fsSL -o runner.tar.gz "https://github.com/actions/runner/releases/download/v$version/actions-runner-osx-arm64-$version.tar.gz"
  tar xzf runner.tar.gz
  rm runner.tar.gz

  say "Registering it as $name with the label $label"
  token=$(gh api -X POST "repos/$repo/actions/runners/registration-token" --jq .token)
  # The runner keeps this PATH (brew's tools, xcodegen) for its jobs.
  ./config.sh --unattended --url "https://github.com/$repo" --token "$token" --name "$name" --labels "$label" --work _work --replace
fi

cd "$dir"
say "Starting it as a service of $(whoami)"
./svc.sh install >/dev/null 2>&1 || true
./svc.sh start
./svc.sh status | tail -3

say "Waiting for GitHub to see it online"
status=offline
for _ in $(seq 1 30); do
  status=$(gh api "repos/$repo/actions/runners" --jq ".runners[] | select(.name == \"$name\") | .status")
  [[ "$status" == online ]] && break
  sleep 2
done
[[ "$status" == online ]] || fail "the runner is $status on GitHub; see $dir/_diag for its log"
echo "online"

say "Keeping the Mac awake on the charger (sudo asks for your password)"
sudo pmset -c sleep 0 disksleep 0

say "Runs from outside contributors need approval (the repository is public)"
gh api -X PUT "repos/$repo/actions/permissions/fork-pr-contributor-approval" -f approval_policy=all_external_contributors

say "Routing the iPad workflows to this Mac"
gh variable set IPAD_RUNNER --body "$label" -R "$repo"

say "Starting the iPad workflow on develop"
gh workflow run ipad.yml --ref develop -R "$repo"
sleep 5
gh run list --workflow ipad.yml -R "$repo" --limit 1 --json url --jq '.[0].url'
echo "Done. The run above should say \"Runner name: $name\" in its first step. To go back: $0 --remove"
