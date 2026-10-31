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

## Results so far (Linux container, 4 cores; ms for the whole pipeline)

| | Für Elise, 636 notes, 10 phrases: first run | then (median of 19) | 5000 notes, 39 phrases: first run | then |
|---|---:|---:|---:|---:|
| .NET JIT (as a server would run it) | 599 | 85 | 1208 | 281 |
| **NativeAOT (as on the iPad)** | **40** | **26** | **447** | **314** |
| Browser, Blazor WebAssembly (the web app today, Chromium) | 1079 | 324 | 2335 | 2247 |

- NativeAOT starts fast: the first run is already close to the warm one, because nothing is compiled at run time. The JIT catches up only after warming up.
- One phrase picked by the player (its analysis, about 1 ms natively and 14 ms in the browser, plus starting its Try experiments, about 7 ms and 88 ms) takes about 9 ms natively on Für Elise, against about 100 ms in the browser.
- Native library: about 11 MB for linux-x64 (whole music-core and the .NET runtime parts it uses), without debug symbols. The iOS size is in the workflow summary.
- The iPad numbers come from the simulator in the workflow and from Andrei's iPad Pro (below); an M-series iPad is expected to be faster than this container.

## Build and run on the iPad (Mac)

You need Xcode 16 or later, XcodeGen (`brew install xcodegen`) and the .NET 10 SDK (https://dot.net, or `brew install --cask dotnet-sdk`).

```bash
cd motiff-lab
git fetch origin && git checkout claude/project-thread-f5sbg8
cd spikes/ios-core
./build-ios.sh            # about 2-4 minutes the first time: NativeAOT for the iPad and the simulator
cd ios && xcodegen        # makes CoreSpike.xcodeproj
open CoreSpike.xcodeproj
```

Signing uses the team from the iPad app's `ios/Config/Local.xcconfig`; without it, choose your team under Signing & Capabilities. The spike installs as a separate "Core Spike" app (`com.andreykukalev.motifflab.corespike`) and never replaces Motiff Lab. Choose the iPad as the destination and Product > Run (the Run action builds Release). On the iPad: pick a piece, Run music-core, and read the table; "First call" is the time the .NET runtime takes to start.

Product > Test (⌘U) runs `CoreSpikeTests` on the chosen device or simulator; the reports are printed in the test log between `BENCHMARK-BEGIN` and `BENCHMARK-END`.

## Run it on Linux

```bash
dotnet run -c Release --project spikes/ios-core/Bench -- spikes/ios-core/assets/fur-elise-97-bars.musicxml 20
dotnet publish spikes/ios-core/Bench -c Release -r linux-x64 -p:PublishAot=true -o /tmp/bench && /tmp/bench/MotiffLab.CoreBench spikes/ios-core/assets/large-5000-notes.mid 20
dotnet publish spikes/ios-core/CoreBridge -c Release -r linux-x64 -o /tmp/lib
clang -I spikes/ios-core/CoreBridge/include spikes/ios-core/native-check/main.c -L /tmp/lib -l:MotiffLabCore.so -Wl,-rpath,/tmp/lib -o /tmp/check && /tmp/check spikes/ios-core/assets/fur-elise-97-bars.musicxml
dotnet publish spikes/ios-core/WebBench -c Release -o /tmp/web   # serve /tmp/web/wwwroot and open it
```

## Open questions for the real thing

- The facade: which calls and which JSON (the project file v12 shapes plus facts, phrases, variations), and how Swift decodes it (`Codable`).
- Verovio for the score: its own native framework (C++), LGPL terms.
- App Store: the framework is a normal embedded dynamic framework; check the privacy manifest and the size of a release build.
