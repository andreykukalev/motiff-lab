#!/usr/bin/env bash
# Builds a version of the iPad app from its tag and installs it on the iPad connected to this Mac
# (governance/README.md, "Versions"). One command, run from anywhere in the clone:
#
#   ipad/build-version.sh v0.1          # the tag of the version
#   ipad/build-version.sh develop       # or a branch, for a hotfix or an experiment build
#
# It checks out the tag (detached; your branch comes back with `git switch -`), builds music-core, Verovio (only the
# first time), the web app's score scripts, generates the Xcode project and builds it signed with your team from
# ipad/App/Config/Local.xcconfig (a free Apple ID: the app runs for 7 days). With one iPad connected by cable or on the
# same network it installs the app there; otherwise it opens Xcode, where Product > Run installs it.
# Needs what ipad/README.md lists: Xcode, XcodeGen, CMake, the .NET 10 SDK, and the Apple ID signed in to Xcode once.
#
# Servers (infra/README.md, "The develop environment"): a version not yet in main (a v0.n tag is a develop commit) talks
# to develop's API and scan server, as the web test site does: their addresses are read from the test site's
# appsettings.json and written into Account.json for this build only. A commit already in main (a released version, a
# hotfix tag) keeps main's servers from the committed Account.json, and so does a build when the test site does not
# answer. MOTIFF_SERVERS=main or MOTIFF_SERVERS=develop chooses by hand.
set -euo pipefail

ref="${1:?Usage: ipad/build-version.sh <tag or branch>, for example v0.1}"
root="$(git rev-parse --show-toplevel)"
cd "$root"

if [[ -n "$(git status --porcelain --untracked-files=no)" ]]; then
  echo "The clone has uncommitted changes; commit or stash them first." >&2
  exit 1
fi
if [[ ! -f ipad/App/Config/Local.xcconfig ]]; then
  echo "ipad/App/Config/Local.xcconfig is missing: copy Local.xcconfig.example and set DEVELOPMENT_TEAM (ipad/README.md)." >&2
  exit 1
fi

echo "==> Getting $ref"
git fetch --quiet --tags origin
if git rev-parse --verify --quiet "refs/tags/$ref" >/dev/null; then
  git checkout --quiet --detach "refs/tags/$ref"
else
  git checkout --quiet --detach "origin/$ref"
fi
version="${ref#v}"
[[ "$version" =~ ^[0-9]+(\.[0-9]+){1,2}$ ]] || version="0.0.0"
build="$(git rev-list --count HEAD)"
echo "    $(git log -1 --format='%h %s'), version $version ($build)"

echo "==> Servers"
account="$root/ipad/App/MotiffLab/Account.json"
servers="${MOTIFF_SERVERS:-}"
if [[ -z "$servers" ]]; then
  if git merge-base --is-ancestor HEAD origin/main 2>/dev/null; then servers=main; else servers=develop; fi
fi
# The address after "Api": or "Scan": in a settings file, when it is a plain https address.
address() { sed -nE "s#.*\"$1\"[[:space:]]*:[[:space:]]*\"(https://[A-Za-z0-9.-]+)\".*#\1#p"; }
if [[ "$servers" == develop && -f "$account" ]]; then
  site="${MOTIFF_TEST_SITE:-delightful-ground-046924d0f-develop.eastus2.4.azurestaticapps.net}"
  settings="$(curl -fsS --max-time 20 "https://$site/appsettings.json" 2>/dev/null || true)"
  api="$(address Api <<< "$settings")"
  scan="$(address Scan <<< "$settings")"
  if [[ -n "$api" && -n "$scan" ]]; then
    # Only this build's copy changes: the committed file comes back when the script ends, built or not.
    trap 'git -C "$root" checkout --quiet -- ipad/App/MotiffLab/Account.json' EXIT
    sed -E -e "s#(\"Api\"[[:space:]]*:[[:space:]]*\")[^\"]*#\1$api#" -e "s#(\"Scan\"[[:space:]]*:[[:space:]]*\")[^\"]*#\1$scan#" \
      "$account" > "$account.tmp" && mv "$account.tmp" "$account"
  else
    echo "    The web test site ($site) did not give its servers: this build uses main's."
    servers=main
  fi
fi
[[ -f "$account" ]] && echo "    $servers: $(address Api < "$account"), $(address Scan < "$account")"

echo "==> music-core (2-4 minutes)"
ipad/Core/build.sh > /tmp/motiff-core.log 2>&1 || { tail -20 /tmp/motiff-core.log; exit 1; }
if [[ ! -d ipad/build/Verovio.xcframework ]]; then
  echo "==> Verovio (5-10 minutes, only this once)"
  ipad/Verovio/build.sh > /tmp/motiff-verovio.log 2>&1 || { tail -20 /tmp/motiff-verovio.log; exit 1; }
fi
echo "==> The score's scripts"
dotnet build src/MotiffLab.Web -v quiet -nologo > /tmp/motiff-web.log 2>&1 || { tail -20 /tmp/motiff-web.log; exit 1; }

echo "==> The app"
cd ipad/App
xcodegen --quiet
xcodebuild build -project MotiffLab.xcodeproj -scheme MotiffLab -configuration Release \
  -destination 'generic/platform=iOS' -derivedDataPath ../build/derived -allowProvisioningUpdates \
  MARKETING_VERSION="$version" CURRENT_PROJECT_VERSION="$build" > /tmp/motiff-app.log 2>&1 \
  || { grep -E "error:|\*\* BUILD" /tmp/motiff-app.log | tail -20; exit 1; }
app="$(pwd)/../build/derived/Build/Products/Release-iphoneos/MotiffLab.app"

ipads="$(xcrun devicectl list devices 2>/dev/null | awk '/iPad/ && /(available|connected)/ { for (i = 1; i <= NF; i++) if ($i ~ /^[0-9A-F]{8}-[0-9A-F-]{27}$/) print $i }')"
if [[ "$(wc -l <<< "$ipads" | tr -d ' ')" == 1 && -n "$ipads" ]]; then
  echo "==> Installing on the iPad"
  xcrun devicectl device install app --device "$ipads" "$app"
  echo "Done: Motiff Lab $version ($build) is on the iPad."
else
  echo "No single connected iPad found: Xcode opens; choose the iPad and Product > Run."
  open MotiffLab.xcodeproj
fi
echo "To go back to your branch: git switch -"
