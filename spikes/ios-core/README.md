# Spike: one music-core for the web and the native iPad app

Question: can the native Swift iPad app of the redesign use the same C# music-core as the web app, and is it fast enough? This spike builds music-core as a native library with .NET NativeAOT, calls it from C and from a SwiftUI app, and times the work the app does when a piece opens and a phrase is picked.

It is a spike: not in `MotiffLab.slnx`, not in CI. The `iOS core spike` workflow runs it on a macOS runner when this folder changes, or by hand.

## How it works

```
Swift (SwiftUI)  ->  MotiffLabCore.xcframework  ->  C functions (include/motifflab_core.h)
                                                    -> CoreBridge/Exports.cs  ->  src/MotiffLab.MusicCore (unchanged)
```

- `CoreBridge/` is a class library compiled to native code with NativeAOT (`PublishAot`, and on iOS `PublishAotUsingRuntimePack`, supported since .NET 9). It exports four C functions with `[UnmanagedCallersOnly]`: `ml_version`, `ml_phrases`, `ml_benchmark` and `ml_free`. Data goes in as bytes and comes out as UTF-8 JSON, which is the shape the real facade would have (decision to come: a JSON facade over music-core and project file v12 as the contract between C# and Swift).
- `CoreBridge/CoreBenchmark.cs` runs the pipeline of `PlaybackLab.razor`: import, suggested phrases, analysis of every phrase (motif, structure and character facts), the Try experiments of the first phrase, the notation (MEI) of the whole piece, and a project file written and read back.
- music-core needs no change: it has no reflection-based JSON and the NativeAOT publish gives no trim or AOT warnings (`-warnaserror`).
- `ios/` is the SwiftUI spike app (XcodeGen): pick a piece, run, see the time of each step; `CoreSpikeTests` runs the same in the simulator.
- `Bench/` runs the benchmark as a console app (JIT, or NativeAOT for this computer); `native-check/` calls the native library from C; `WebBench/` runs it in the browser as the web app does today (Blazor WebAssembly, interpreted).
- `assets/`: Für Elise as 97 bars of MusicXML (from the design kit) and the 5000-note MIDI file of the music-core tests.

## Results so far (ms for the whole pipeline)

| | Für Elise, 636 notes, 10 phrases: first run | then (median of 19) | 5000 notes, 39 phrases: first run | then |
|---|---:|---:|---:|---:|
| .NET JIT, Linux container (as a server would run it) | 599 | 85 | 1208 | 281 |
| **NativeAOT, Linux container** | **40** | **26** | **447** | **314** |
| NativeAOT, iPad simulator on a macOS runner (Xcode 16) | 120 | 32 | 361 | 284 |
| NativeAOT, iPad simulator on Andrei's MacBook Pro (Xcode 27.2) | 67 | 29 | | |
| Browser, Blazor WebAssembly (the web app today, Chromium) | 1079 | 324 | 2335 | 2247 |

- NativeAOT starts fast: the first run is already close to the warm one, because nothing is compiled at run time. The JIT catches up only after warming up.
- One phrase picked by the player (its analysis, about 1 ms natively and 14 ms in the browser, plus starting its Try experiments, about 7 ms and 88 ms) takes about 9 ms natively on Für Elise, against about 100 ms in the browser.
- Native library: about 11 MB for linux-x64 (whole music-core and the .NET runtime parts it uses), without debug symbols. The iOS size is in the workflow summary.
- The iPad numbers come from the simulator in the workflow and from Andrei's iPad Pro (below); an M-series iPad is expected to be faster than this container.

## Build and run on the iPad (Mac)

You need Xcode 16 or later, XcodeGen and CMake (`brew install xcodegen cmake`) and the .NET 10 SDK (https://dot.net, or `brew install --cask dotnet-sdk`). Terminal must use Xcode's tools: `sudo xcode-select -s /Applications/Xcode.app/Contents/Developer`.

```bash
cd motiff-lab
git fetch origin && git checkout claude/project-thread-f5sbg8
cd spikes/ios-core
./build-ios.sh            # about 2-4 minutes the first time: NativeAOT for the iPad and the simulator
./build-verovio.sh        # about 5-10 minutes, once: Verovio from source for the iPad and the simulator
cd ios && xcodegen        # makes CoreSpike.xcodeproj
open CoreSpike.xcodeproj
```

Signing uses the team from the iPad app's `ios/Config/Local.xcconfig`; without it, choose your team under Signing & Capabilities. The spike installs as a separate "Core Spike" app (`com.andreykukalev.motifflab.corespike`) and never replaces Motiff Lab. Choose the iPad as the destination and Product > Run (the Run action builds Release). On the iPad: pick a piece, Run music-core, and read the table; "First call" is the time the .NET runtime takes to start. The Score tab engraves the piece with Verovio on A4 pages in Leland and shows the pages with the time of each step.

Product > Test (⌘U) runs `CoreSpikeTests` on the chosen device or simulator; the reports are printed in the test log between `BENCHMARK-BEGIN` and `BENCHMARK-END`, and the engraving times between `ENGRAVE-BEGIN` and `ENGRAVE-END`.

## Run it on Linux

```bash
dotnet run -c Release --project spikes/ios-core/Bench -- spikes/ios-core/assets/fur-elise-97-bars.musicxml 20
dotnet publish spikes/ios-core/Bench -c Release -r linux-x64 -p:PublishAot=true -o /tmp/bench && /tmp/bench/MotiffLab.CoreBench spikes/ios-core/assets/large-5000-notes.mid 20
dotnet publish spikes/ios-core/CoreBridge -c Release -r linux-x64 -o /tmp/lib
clang -I spikes/ios-core/CoreBridge/include spikes/ios-core/native-check/main.c -L /tmp/lib -l:MotiffLabCore.so -Wl,-rpath,/tmp/lib -o /tmp/check && /tmp/check spikes/ios-core/assets/fur-elise-97-bars.musicxml
dotnet publish spikes/ios-core/WebBench -c Release -o /tmp/web   # serve /tmp/web/wwwroot and open it
# the score pipeline, with a Verovio built as a shared library (build-verovio.sh shows the CMake options):
python3 spikes/ios-core/native-check/engrave.py /tmp/lib/MotiffLabCore.so <libverovio.so> <verovio>/data spikes/ios-core/assets/fur-elise-97-bars.musicxml 5
```

## Verovio: the score on the iPad

The web app engraves with Verovio 6.3.0 compiled to WebAssembly (decision record 0008). The iPad uses the same version, compiled from its unmodified source for iOS:

- `build-verovio.sh` clones the `version-6.3.0` tag, applies one small patch (`verovio-classic-locale.patch`, below), builds it with CMake as a shared library (`BUILD_AS_LIBRARY`, no Humdrum, like the web build) for `iphoneos` and `iphonesimulator`, and packages `build/Verovio.xcframework`. Swift calls Verovio's own C functions (`tools/c_wrapper.h`, the ones its Python and Swift bindings use), so there is no wrapper code of ours in C++.
- `build/verovio-data` holds the font data the app copies: Bravura and Leipzig (Verovio always loads them), Leland (the redesign's music font) and the text metrics, about 8 MB; Verovio's other fonts are left out.
- The Score tab (`ios/CoreSpike/ScoreScreen.swift`, `Engraver.swift`) runs the score view's pipeline: music-core writes MEI (`ml_mei`), Verovio lays it out with the web app's A4 options (`Scripts/score/score-page.ts`) plus `"font": "Leland"`, and draws every page as SVG. The spike shows the SVG in a web view; the real app has to choose between that (simple, and the overlays could stay SVG) and drawing Verovio's paths natively, which is what the overlays on the score (phrase brackets, bookmarks, ink) would want.
- `native-check/engrave.py` runs the same pipeline with the same C calls outside the app (Linux or macOS); the workflow's `verovio-macos` job times and profiles it on macOS, and `native-check/callers.py` reads the profile.

### Engraving times (ms; median of the runs after the first)

| Für Elise: 636 notes, 3 A4 pages | MEI (music-core) | layout (`loadData`) | SVG of all pages | timemap |
|---|---:|---:|---:|---:|
| iPad simulator, Verovio as released | 6 | 1549–1616 | 101–117 | 167–211 |
| **iPad simulator, with the patch** | **7** | **50** | **32–56** | **25–46** |
| macOS on the same runner, as released / with the patch | | 83 / 46 | 59 / 49 | 45 / 36 |
| Linux container, as released | 15 | 37 | 39 | 32 |
| Web app's WebAssembly build (Node on Linux) | | 101–175 | 59–77 | 54–62 |

The 5000-note file (12 pages) takes 338–347 ms of layout and 260 ms for its SVG in the simulator with the patch, against 6742–6841 ms of layout without it; the web build takes 575–617 ms.

### The patch

Verovio as released laid out Für Elise in 1.6 s in the iPad simulator, 20 to 40 times slower than natively elsewhere. The macOS profile showed why: every score definition (`ScoreDef`, one for each system and measure while laying out) reads Verovio's default tuning, and the tuning parser (`include/tuning-library/TuningsImpl.h`) builds `std::locale("C")` for every number it reads. Apple's C++ library builds a named locale from scratch, with its time names and time zone, so that cost is paid thousands of times; on Linux it is nearly free. The patch uses `std::locale::classic()`, which is the same "C" locale already built. Nothing else changes: same pages and SVG. Its version then reads `6.3.0-425dd7b-dirty`. It is small enough to offer upstream, and once a Verovio release has it the patch goes away.

### LGPL

Verovio is LGPL-3.0-or-later. What the spike already does to meet section 4 (combined works): Verovio is built from the tagged public source by a script in this repository, with one published patch (`verovio-classic-locale.patch`, a modified version under the same licence), and shipped as its own dynamic framework that the app loads at run time; the licence texts (`COPYING`, `COPYING.LESSER`) go into the app with the font data. Still to do before an App Store release:

- a credit and licence screen in the app (Verovio, version, licence, link to the source and to the patch), as the web score view has;
- the "Installation Information" LGPL v3 asks for on a consumer device: since this repository is public, the build scripts and a free Apple ID are enough for anyone to build and install the app with another Verovio build; the README of the release should say so;
- a decision whether the App Store's own terms of use are compatible with that. Verovio's maintainers publish it for iOS themselves (`Package.swift`, `Verovio.podspec` in its repository), which suggests they expect App Store use, but it is a legal question: ask them, or a lawyer, before the first App Store release. TestFlight-free development builds signed by Andrei are not affected.

## Open questions for the real thing

- The facade: which calls and which JSON (the project file v12 shapes plus facts, phrases, variations), and how Swift decodes it (`Codable`).
- Drawing the score: Verovio's SVG in a web view, or its paths drawn natively (see above).
- Offer the locale patch to Verovio's maintainers, so that a later release needs no patch.
- App Store: both libraries are normal embedded dynamic frameworks; check the privacy manifest and the size of a release build.
