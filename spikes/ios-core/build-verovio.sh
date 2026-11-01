#!/usr/bin/env bash
# Builds Verovio (the engraver of the web app's score view, decision record 0008) from its source, with
# one small patch (below), for the iPad and the iPad simulator as a dynamic framework,
# build/Verovio.xcframework, and copies the font data the app needs to build/verovio-data. Runs on a Mac
# with Xcode and CMake (brew install cmake). Usage: ./build-verovio.sh
set -euo pipefail

version=6.3.0   # the version the web app ships (tools/verovio/README.md)
here="$(cd "$(dirname "$0")" && pwd)"
build="$here/build"
src="$build/verovio-src"
name=Verovio
mkdir -p "$build"

if [ ! -d "$src" ]; then
  git clone --quiet --depth 1 --branch "version-$version" https://github.com/rism-digital/verovio.git "$src"
fi

# One change to Verovio's source: its tuning code builds std::locale("C") for every number it reads, and
# every score definition reads the default tuning. With Apple's C++ library that rebuilds a whole named
# locale each time (time names, time zone), which made layout in the iPad simulator 20 times slower. The
# classic locale is the same "C" locale without building it. To be offered upstream.
patch="$here/verovio-classic-locale.patch"
if git -C "$src" apply --check "$patch" 2>/dev/null; then
  git -C "$src" apply "$patch"
fi

framework() {
  local sdk=$1 platform=$2 slice=$3
  local out="$build/verovio-$sdk"
  cmake -S "$src/cmake" -B "$out" \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_SYSTEM_NAME=iOS \
    -DCMAKE_OSX_SYSROOT="$sdk" \
    -DCMAKE_OSX_ARCHITECTURES=arm64 \
    -DCMAKE_OSX_DEPLOYMENT_TARGET=17.0 \
    -DBUILD_AS_LIBRARY=ON \
    -DNO_HUMDRUM_SUPPORT=ON \
    > "$out.cmake.log"
  cmake --build "$out" --config Release --parallel "$(sysctl -n hw.ncpu)" > "$out.build.log"

  local lib
  lib=$(find "$out" -name 'libverovio*.dylib' -type f | head -1)
  local fw="$build/$slice/$name.framework"
  rm -rf "$fw"
  mkdir -p "$fw/Headers" "$fw/Modules"
  cp "$lib" "$fw/$name"
  install_name_tool -id "@rpath/$name.framework/$name" "$fw/$name"
  cp "$src/tools/c_wrapper.h" "$fw/Headers/verovio_c.h"
  # c_wrapper.h uses bool without including stdbool.h (Verovio's own Swift binding adds it the same way).
  printf '#include <stdbool.h>\n#include "verovio_c.h"\n' > "$fw/Headers/$name.h"
  cat > "$fw/Modules/module.modulemap" <<EOF
framework module $name {
  umbrella header "$name.h"
  export *
}
EOF
  cat > "$fw/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>$name</string>
  <key>CFBundleIdentifier</key><string>com.andreykukalev.motifflab.verovio</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleExecutable</key><string>$name</string>
  <key>CFBundlePackageType</key><string>FMWK</string>
  <key>CFBundleSupportedPlatforms</key><array><string>$platform</string></array>
  <key>MinimumOSVersion</key><string>17.0</string>
</dict>
</plist>
EOF
  echo "verovio $slice: $(du -sh "$fw/$name" | cut -f1) ($(lipo -archs "$fw/$name"))"
  echo "verovio $slice flags: $(grep -h '^CXX_FLAGS' "$out"/CMakeFiles/verovio.dir/flags.make 2>/dev/null | cut -c1-60 || true)"
}

framework iphoneos iPhoneOS verovio-ios-arm64
framework iphonesimulator iPhoneSimulator verovio-iossimulator-arm64

rm -rf "$build/$name.xcframework"
xcodebuild -create-xcframework \
  -framework "$build/verovio-ios-arm64/$name.framework" \
  -framework "$build/verovio-iossimulator-arm64/$name.framework" \
  -output "$build/$name.xcframework" > /dev/null

# Font data: Bravura and Leipzig are always loaded, Leland is the font of the redesign, text/ holds the
# metrics of the text fonts. The other music fonts are left out to keep the app small.
data="$build/verovio-data"
rm -rf "$data"
mkdir -p "$data"
for font in Bravura Leipzig Leland; do
  cp -R "$src/data/$font" "$src/data/$font.xml" "$src/data/$font.css" "$data/"
done
cp -R "$src/data/text" "$data/"
cp "$src/COPYING" "$src/COPYING.LESSER" "$data/"
echo "verovio data: $(du -sh "$data" | cut -f1)"
echo "Built $build/$name.xcframework"
