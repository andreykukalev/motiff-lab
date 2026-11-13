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
# same network it installs the app there; otherwise it opens Xcode, where Product > Run installs it (Account.json then
# keeps this build's "Default" until the next run of the script puts it back).
# Needs what ipad/README.md lists: Xcode, XcodeGen, CMake, the .NET 10 SDK, and the Apple ID signed in to Xcode once.
#
# Servers (infra/README.md, "The develop environment"): Account.json carries both environments, Main (the players' real
# database) and Test (develop's servers and their test database), and the app switches between them in Settings, each
# with its own library on the iPad. The script only sets where the build starts ("Default", for this build only): a
# version not yet in main (a v0.n tag is a develop commit) starts in Test, where Main is read only until "Allow writing
# to Main" is turned on; a commit already in main (a released version, a hotfix tag) starts in Main and offers Test only
# to a tester. MOTIFF_SERVERS=main or MOTIFF_SERVERS=develop chooses by hand.
set -euo pipefail
# Read whole before it runs: checking out the tag below may replace this very file.
{

ref="${1:?Usage: ipad/build-version.sh <tag or branch>, for example v0.1}"
root="$(git rev-parse --show-toplevel)"
cd "$root"

settings=ipad/App/MotiffLab/Account.json
# A run that left Xcode open for Product > Run keeps this build's start in Account.json (see the end); when that line is
# the clone's only change, it is put back here before anything else.
if [[ "$(git status --porcelain --untracked-files=no)" == " M $settings" ]] \
  && [[ -z "$(git diff -U0 -- "$settings" | grep -E '^[-+][^-+]' | grep -v '"Default"' || true)" ]]; then
  echo "==> Putting back $settings, which the last run left for Xcode"
  git checkout --quiet -- "$settings"
fi
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
# What Settings · About shows: the tag, or the branch and the commit; and when this build was made (UTC).
commit="$(git rev-parse --short=7 HEAD)"
if git rev-parse --verify --quiet "refs/tags/$ref" >/dev/null; then shown="$ref"; else shown="$ref $commit"; fi
built="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

echo "==> Servers"
account="$root/ipad/App/MotiffLab/Account.json"
servers="${MOTIFF_SERVERS:-}"
if [[ -z "$servers" ]]; then
  if git merge-base --is-ancestor HEAD origin/main 2>/dev/null; then servers=main; else servers=develop; fi
fi
case "$servers" in
  main) start=main ;;
  develop | test) start=test ;;
  *) echo "MOTIFF_SERVERS is main or develop, not $servers." >&2; exit 1 ;;
esac
# The addresses after "Api": in the settings file, Main's first, then Test's.
address() { sed -nE "s#.*\"Api\"[[:space:]]*:[[:space:]]*\"(https://[A-Za-z0-9.-]+)\".*#\1#p" "$account"; }
if grep -q '"Default"' "$account"; then
  # Only this build's copy changes: the committed file comes back when the script ends, built or not.
  trap 'git -C "$root" checkout --quiet -- "$settings"' EXIT
  sed -E "s#(\"Default\"[[:space:]]*:[[:space:]]*\")[^\"]*#\1$start#" "$account" > "$account.tmp" && mv "$account.tmp" "$account"
  echo "    Starts in $start. Main: $(address | sed -n 1p); Test: $(address | sed -n 2p)"
else
  # A version from before the switch (0.3) knows only Main's servers.
  echo "    This version knows only Main's servers: $(address | sed -n 1p)"
fi

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
  MARKETING_VERSION="$version" CURRENT_PROJECT_VERSION="$build" \
  MOTIFF_BUILD_VERSION="$shown" MOTIFF_BUILD_COMMIT="$commit" MOTIFF_BUILD_DATE="$built" > /tmp/motiff-app.log 2>&1 \
  || { grep -E "error:|\*\* BUILD" /tmp/motiff-app.log | tail -20; exit 1; }
app="$(pwd)/../build/derived/Build/Products/Release-iphoneos/MotiffLab.app"

ipads="$(xcrun devicectl list devices 2>/dev/null | awk '/iPad/ && /(available|connected)/ { for (i = 1; i <= NF; i++) if ($i ~ /^[0-9A-F]{8}-[0-9A-F-]{27}$/) print $i }')"
if [[ "$(wc -l <<< "$ipads" | tr -d ' ')" == 1 && -n "$ipads" ]]; then
  echo "==> Installing on the iPad"
  xcrun devicectl device install app --device "$ipads" "$app"
  echo "Done: Motiff Lab $version ($build) is on the iPad."
else
  echo "No single connected iPad found: Xcode opens; choose the iPad and Product > Run."
  # Xcode builds again on Product > Run, from the files as they are: Account.json keeps this build's start, or the app
  # would start in Main and not be a version build. The next run of this script puts it back by itself.
  if [[ -n "$(git -C "$root" diff --name-only -- "$settings")" ]]; then
    trap - EXIT
    echo "    $settings keeps \"Default\": \"$start\" for the run. After it: git checkout -- $settings"
  fi
  open MotiffLab.xcodeproj
fi
echo "To go back to your branch: git switch -"
exit
}
