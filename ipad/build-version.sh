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
