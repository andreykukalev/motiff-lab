#!/usr/bin/env bash
# Builds music-core for the iPad and the iPad simulator with NativeAOT and packages it as
# build/MotiffLabCore.xcframework, which the Swift app in ios/ embeds. Runs on a Mac with
# Xcode and the .NET SDK from global.json (https://dot.net). Usage: ./build-ios.sh
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
build="$here/build"
name=MotiffLabCore
# Only this script's outputs: build-verovio.sh keeps its source and frameworks in build/ too.
rm -rf "$build/publish" "$build/ios-arm64" "$build/iossimulator-arm64" "$build/$name.xcframework"
mkdir -p "$build"

framework() {
  local rid=$1 platform=$2
  local publish="$build/publish/$rid"
  dotnet publish "$here/CoreBridge/MotiffLab.CoreBridge.csproj" -c Release -r "$rid" -o "$publish" -warnaserror

  local fw="$build/$rid/$name.framework"
  mkdir -p "$fw/Headers" "$fw/Modules"
  cp "$publish/$name.dylib" "$fw/$name"
  # Apple loads a framework's library from the app's Frameworks folder by its install name.
  install_name_tool -rpath @executable_path @executable_path/Frameworks "$fw/$name" 2>/dev/null || true
  install_name_tool -id "@rpath/$name.framework/$name" "$fw/$name"
  cp "$here/CoreBridge/include/motifflab_core.h" "$fw/Headers/"
  cat > "$fw/Modules/module.modulemap" <<EOF
framework module $name {
  umbrella header "motifflab_core.h"
  export *
}
EOF
  cat > "$fw/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>$name</string>
  <key>CFBundleIdentifier</key><string>com.andreykukalev.motifflab.core</string>
  <key>CFBundleVersion</key><string>1.0</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>CFBundleExecutable</key><string>$name</string>
  <key>CFBundlePackageType</key><string>FMWK</string>
  <key>CFBundleSupportedPlatforms</key><array><string>$platform</string></array>
  <key>MinimumOSVersion</key><string>17.0</string>
</dict>
</plist>
EOF
  if [ -d "$publish/$name.dylib.dSYM" ]; then
    cp -R "$publish/$name.dylib.dSYM" "$build/$rid/$name.framework.dSYM"
  fi
  echo "$rid: $(du -sh "$fw/$name" | cut -f1) ($(lipo -archs "$fw/$name"))"
}

framework ios-arm64 iPhoneOS
framework iossimulator-arm64 iPhoneSimulator

xcodebuild -create-xcframework \
  -framework "$build/ios-arm64/$name.framework" \
  -framework "$build/iossimulator-arm64/$name.framework" \
  -output "$build/$name.xcframework"
echo "Built $build/$name.xcframework"
