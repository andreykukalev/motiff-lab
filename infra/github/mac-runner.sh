#!/usr/bin/env bash
# Sets up this Mac as the repository's self-hosted GitHub Actions runner for the iPad workflows (docs/repository-structure.md,
# "iPad builds"), under its own hidden local account that runs in the background. One command, run by an administrator of
# the Mac (Andrei, not root) from anywhere in the clone:
#
#   infra/github/mac-runner.sh            # set up, start, route the iPad workflows here and start one test run on develop
#   infra/github/mac-runner.sh --remove   # move the runs back to GitHub's macOS, remove the runner and its account
#
# What it does:
# - checks Xcode (license, first launch, an iOS simulator runtime), Homebrew, XcodeGen, CMake and the GitHub CLI, which
#   signs in once in the browser as you (it gets the runner's token and sets the repository's settings);
# - creates the standard (not administrator) account "runner", hidden from the login window, with a random password
#   nobody needs: nobody signs in to it;
# - downloads the latest runner to /Users/runner/actions-runner and registers it as "macbook" with the label motiff-mac;
# - runs it as a launchd daemon under that account: it starts with the Mac, before anyone signs in, and runs whoever is
#   signed in or not; it gives the account an iPad simulator of its own;
# - keeps the Mac awake on the charger; requires approval for runs from outside contributors (the repository is public);
#   sets the repository variable IPAD_RUNNER=motiff-mac, which the iPad workflows read; starts the iPad workflow on develop.
# The runner uses Xcode and the simulator runtimes the Mac already has, and the brew tools in /opt/homebrew; .NET is
# installed into its home by the workflow (setup-dotnet). sudo asks for your password (once, then it remembers it).
# - keeps the Mac awake with the lid closed while it is on the charger: a second small daemon checks the power every minute
#   and sets `pmset disablesleep` on the charger and clears it on battery, so a closed MacBook in a bag still sleeps.
set -euo pipefail

repo=andreykukalev/motiff-lab
name=macbook
label=motiff-mac
account=runner
home=/Users/$account
dir=$home/actions-runner
daemon=com.motifflab.actions-runner
plist=/Library/LaunchDaemons/$daemon.plist
awake=com.motifflab.awake-on-charger
awake_plist=/Library/LaunchDaemons/$awake.plist
path=/opt/homebrew/bin:/opt/homebrew/sbin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin

say() { printf '\n== %s\n' "$*"; }
fail() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }
# A command as the runner's account, with the environment its daemon gets, from a folder it can read (not yours).
as_runner() { (cd / && sudo -u "$account" env HOME="$home" USER="$account" PATH="$path" "$@"); }

[[ "$(uname -s)" == Darwin && "$(uname -m)" == arm64 ]] || fail "this script is for an Apple Silicon Mac"
[[ $EUID -ne 0 ]] || fail "run it as yourself, not with sudo: it asks for the password itself"
id -Gn | grep -qw admin || fail "run it as an administrator of this Mac"

if [[ -x /opt/homebrew/bin/brew ]]; then eval "$(/opt/homebrew/bin/brew shellenv)"; fi
command -v brew >/dev/null || fail "Homebrew is missing: install it from https://brew.sh and run the script again"
command -v gh >/dev/null || { say "Installing the GitHub CLI"; brew install gh; }
gh auth status >/dev/null 2>&1 || { say "Sign in to GitHub (opens the browser)"; gh auth login --hostname github.com --git-protocol https --web; }

say "sudo asks for your Mac password"
sudo -v

if [[ "${1:-}" == --remove ]]; then
  say "Moving the iPad runs back to GitHub's macOS"
  gh variable delete IPAD_RUNNER -R "$repo" || true
  for d in "$daemon" "$awake"; do
    sudo launchctl bootout "system/$d" 2>/dev/null || true
    sudo rm -f "/Library/LaunchDaemons/$d.plist"
  done
  sudo pmset -a disablesleep 0
  if sudo test -f "$dir/.runner"; then
    token=$(gh api -X POST "repos/$repo/actions/runners/remove-token" --jq .token)
    as_runner bash -c "cd '$dir' && ./config.sh remove --token '$token'"
  fi
  if id "$account" >/dev/null 2>&1; then
    say "Deleting the account $account and its home"
    sudo sysadminctl -deleteUser "$account"
  fi
  say "Done: the runner and its account are removed"
  exit 0
fi

say "Checking Xcode and the tools"
xcodebuild -version || fail "Xcode is missing or xcode-select points to the Command Line Tools: open Xcode once, or run sudo xcode-select -s /Applications/Xcode.app"
if ! xcodebuild -checkFirstLaunchStatus >/dev/null 2>&1; then
  say "Accepting the Xcode license and finishing its first launch"
  sudo xcodebuild -license accept
  sudo xcodebuild -runFirstLaunch
fi
xcrun simctl list runtimes available | grep -q '^iOS' ||
  fail "no iOS simulator runtime: in Xcode > Settings > Components install the iOS platform, then run the script again"
for tool in xcodegen cmake; do
  command -v "$tool" >/dev/null || { say "Installing $tool"; brew install "$tool"; }
done

if id "$account" >/dev/null 2>&1; then
  say "The account $account already exists"
else
  say "Creating the hidden account $account"
  sudo sysadminctl -addUser "$account" -fullName "GitHub Actions runner" -password "$(openssl rand -base64 24)" -home "$home"
  sudo dscl . create "/Users/$account" IsHidden 1
fi
sudo test -d "$home" || sudo createhomedir -c -u "$account" >/dev/null
sudo test -d "$home" || fail "the home folder $home was not created"

say "Checking that $account can use Xcode and an iPad simulator"
as_runner xcodebuild -version | head -1
if ! as_runner xcrun simctl list devices available | grep -q 'iPad'; then
  runtime=$(as_runner xcrun simctl list runtimes available -j | python3 -c '
import json, sys
print([r for r in json.load(sys.stdin)["runtimes"] if r["platform"] == "iOS"][-1]["identifier"])')
  type=$(as_runner xcrun simctl list devicetypes -j | python3 -c '
import json, sys
types = json.load(sys.stdin)["devicetypes"]
print(([t for t in types if t["name"].startswith("iPad Pro")] or [t for t in types if t["name"].startswith("iPad")])[-1]["identifier"])')
  as_runner xcrun simctl create "iPad Pro" "$type" "$runtime"
fi
as_runner xcrun simctl list devices available | grep 'iPad' | tail -1

if sudo test -f "$dir/.runner"; then
  say "The runner is already registered in $dir"
else
  say "Downloading the runner"
  version=$(gh api repos/actions/runner/releases/latest --jq .tag_name)
  version=${version#v}
  # Downloaded by the runner's account into its own folder: your temporary folder is closed to it.
  as_runner mkdir -p "$dir"
  as_runner curl -fsSL -o "$dir/runner.tar.gz" \
    "https://github.com/actions/runner/releases/download/v$version/actions-runner-osx-arm64-$version.tar.gz"
  as_runner tar xzf "$dir/runner.tar.gz" -C "$dir"
  as_runner rm -f "$dir/runner.tar.gz"

  say "Registering it as $name with the label $label"
  token=$(gh api -X POST "repos/$repo/actions/runners/registration-token" --jq .token)
  # The runner keeps this PATH (brew's tools) for its jobs; brew must not try to update /opt/homebrew, which is yours.
  as_runner bash -c "cd '$dir' && ./config.sh --unattended --url 'https://github.com/$repo' --token '$token' \
    --name '$name' --labels '$label' --work _work --replace"
  as_runner bash -c "printf 'HOMEBREW_NO_AUTO_UPDATE=1\nHOMEBREW_NO_INSTALL_CLEANUP=1\n' >> '$dir/.env'"
fi

say "Running it in the background as a daemon of $account"
sudo tee "$plist" >/dev/null <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>$daemon</string>
  <key>ProgramArguments</key><array><string>$dir/run.sh</string></array>
  <key>UserName</key><string>$account</string>
  <key>GroupName</key><string>staff</string>
  <key>WorkingDirectory</key><string>$dir</string>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><true/>
  <key>SessionCreate</key><true/>
  <!-- Below the player's own apps (Rekordbox, a DAW): builds yield the CPU and the disk to them. -->
  <key>ProcessType</key><string>Standard</string>
  <key>Nice</key><integer>15</integer>
  <key>LowPriorityIO</key><true/>
  <key>EnvironmentVariables</key>
  <dict>
    <key>HOME</key><string>$home</string>
    <key>USER</key><string>$account</string>
    <key>PATH</key><string>$path</string>
    <key>LANG</key><string>en_US.UTF-8</string>
  </dict>
  <key>StandardOutPath</key><string>$dir/daemon.log</string>
  <key>StandardErrorPath</key><string>$dir/daemon.log</string>
</dict>
</plist>
EOF
sudo chown root:wheel "$plist"
sudo chmod 644 "$plist"
sudo launchctl bootout "system/$daemon" 2>/dev/null || true
sudo launchctl bootstrap system "$plist"

say "Waiting for GitHub to see it online"
status=offline
for _ in $(seq 1 30); do
  status=$(gh api "repos/$repo/actions/runners" --jq ".runners[] | select(.name == \"$name\") | .status")
  [[ "$status" == online ]] && break
  sleep 2
done
[[ "$status" == online ]] || fail "the runner is $status on GitHub; its log: sudo tail $dir/daemon.log"
echo "online"

say "Keeping the Mac awake on the charger, with the lid closed too"
sudo pmset -c sleep 0 disksleep 0
sudo tee "$awake_plist" >/dev/null <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>$awake</string>
  <key>ProgramArguments</key>
  <array>
    <string>/bin/sh</string><string>-c</string>
    <string>if pmset -g ps | grep -q 'AC Power'; then pmset -a disablesleep 1; else pmset -a disablesleep 0; fi</string>
  </array>
  <key>RunAtLoad</key><true/>
  <key>StartInterval</key><integer>60</integer>
</dict>
</plist>
EOF
sudo chown root:wheel "$awake_plist"
sudo chmod 644 "$awake_plist"
sudo launchctl bootout "system/$awake" 2>/dev/null || true
sudo launchctl bootstrap system "$awake_plist"

say "Runs from outside contributors need approval (the repository is public)"
gh api -X PUT "repos/$repo/actions/permissions/fork-pr-contributor-approval" -f approval_policy=all_external_contributors

say "Routing the iPad workflows to this Mac"
gh variable set IPAD_RUNNER --body "$label" -R "$repo"

say "Starting the iPad workflow on develop"
gh workflow run ipad.yml --ref develop -R "$repo"
sleep 5
gh run list --workflow ipad.yml -R "$repo" --limit 1 --json url --jq '.[0].url'
echo "Done. The run above should say \"Runner name: $name\" in its first step. To go back: $0 --remove"
