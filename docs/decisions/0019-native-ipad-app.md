# 0019 · The native iPad app: SwiftUI over the one C# music-core, and iPad releases

Status: proposed, 2026-11-04. Andrei chose a fully native Swift iPad app for the redesign (2026-10-31) and kept one C# music-core for it after the spike (2026-11-01); this record is item 25 of the redesign preparation and supersedes [0015](0015-ipad-app.md). It also settles item 15 (the web site as the development environment, the iPad app as production).

## Context

The iPad app today is the Swift shell of 0015: one `WKWebView` that opens the deployed web app, plus sound with the silent switch on, a screen that stays on, the Apple Pencil's double tap and squeeze, files and offline start. Every merge to main reaches the iPad on its next start.

The redesign ([0018](0018-redesign.md)) changes both halves of that. The iPad app becomes production and gets releases, not single features (section 15); the redesign is built in both the web app and a native iPad app (section 14), each screen web first. Three spikes answered whether a native app can share the engine:

- **music-core through NativeAOT** (PR #176, item 20). The unchanged C# music-core builds for iOS into `MotiffLabCore.xcframework` with no AOT warnings, about 10 MB. Für Elise through the whole pipeline in the iPad simulator on Andrei's Mac: 67 ms the first time, 29 ms after, against 1079 and 324 ms in Blazor WebAssembly.
- **The facade** (PR #177, item 21, [0020](0020-music-core-facade.md)). Five C functions, `ml_version`, `ml_open`, `ml_call`, `ml_close` and `ml_free`, carry one JSON contract; six Swift tests pass in the simulator.
- **Verovio** (PR #176, item 22). Verovio 6.3.0 builds from source as a dynamic `Verovio.xcframework` with Leland; Für Elise lays out on three A4 pages in 50 ms in the simulator, once a one-line patch removes a `std::locale("C")` built for every number, which Apple's libc++ makes about 30 times slower. Three pages draw in a `WKWebView` in 71 ms warm and 2.5 s on the very first start, which is the web view's process starting.

Options weighed for the app around them:

| Option | Verdict |
| --- | --- |
| Keep the shell and the web app | Rejected by Andrei: the iPad is the instrument the app is used at, and the redesign is native there |
| Rewrite music-core in Swift | Rejected by Andrei (2026-11-01): two engines to keep equal |
| .NET MAUI (PR #120) | Rejected: a second UI framework in C#, still not native controls, and the Mac workload to keep building |
| **SwiftUI over the C# core through the facade** | **Chosen**: native screens, one engine, one project file |

## Decision

### 1. One engine, Swift for the screens

1. **The iPad app is SwiftUI**, iPadOS 17 and later, iPad only (iPhone later, if ever). Swift draws the screens, plays the sound, takes the Pencil and talks to the API; **everything musical comes from music-core through the facade**: import, phrases, facts, Try chips, variations, Keep checks, notation (MEI), playback scores, the project file, the settings model and every musical text in both languages. The rule of [CLAUDE.md](../../CLAUDE.md) and `ui-conventions.md` holds as it does in the web: the UI only displays music-core output.
2. **`MotiffLabCore.xcframework`** is music-core built with NativeAOT for `ios-arm64` and `ios-arm64-simulator` (Apple silicon only, so a simulator Release build excludes `x86_64`). The bridge project exports only the five facade functions of 0020; the spike's benchmark exports stay in the spike. The app calls them from one serial queue off the main thread, so a slow call never blocks a gesture, and the facade's pieces are never touched from two threads.
3. **`Verovio.xcframework`** is Verovio built from source at a pinned version with the locale patch (`verovio-classic-locale.patch`) and its C API (`tools/c_wrapper.h`), with Leland, Bravura and Leipzig data in the app. It stays a **dynamic framework** because Verovio is LGPL-3.0: the app ships the licence texts and a screen naming Verovio, its version and where its source and the patch are, and the patch is offered upstream. Before the App Store, Andrei asks the authors or a lawyer whether that is enough for a store build.
4. **The score is Verovio's SVG in one `WKWebView`**, as in the web app, created when the app starts so the first score does not wait for the web view's process. The web view loads the web app's score scripts from the app bundle (compiled from the same `Scripts/score` sources), so the same `score-geometry` module ([0021](0021-excerpt-ossia-and-score-geometry.md)) reads bars, systems and pages from that SVG, so brackets, bands, rings, flags, fingering and pencil marks land in the same places on both platforms; SwiftUI draws the controls around the page and the pencil layer over it. Native drawing of the notation is not planned: the spike showed the web view fast enough, and native drawing would only be worth it for overlays.

### 2. What the native app adds (preparation item 23)

1. **Sound: `AVAudioEngine`** with an `AVAudioSourceNode` that renders the playback score the facade answers (`playback`) the way the web's AudioWorklet does ([0007](0007-playback-engine.md)): the same Salamander Grand Piano V3 samples (CC BY 3.0, `tools/piano`), pitch-shifted from the nearest sample, velocity, damping, 120 ms release, 96 voices, the clock in canonical ticks so a loop wraps on its frame and a tempo change applies at its tick, the pedal and the as-played tempos as in the web. The audio session's playback category plays with the silent switch on; the screen stays on while the app is open.
2. **The Apple Pencil: PencilKit** for the live stroke (low latency, prediction, pressure), turned into a stroke of the pencil layer when it ends, anchored to the bar it starts in exactly as in the web (`marks.ink` in project file version 12, [docs/project-file.md](../project-file.md)), with the four inks and the highlighter of 0018 section 5. Saved marks are drawn from the project file, never kept as a `PKDrawing`, so a mark made on the iPad shows in the web and back. A finger never writes; double tap and squeeze follow the player's choice as in 0015.
3. **Projects are files on the device**: one `.motiff.json` project file per piece in the app's Documents, shown in the Files app, opened and shared from there. Device settings stay on the device (`UserDefaults`, music-core's device settings document); setting overrides for a piece travel in its project file.
4. **Sign-in, only for Pro: MSAL for iOS** against the same Entra External ID tenant and scopes as the web (0014: `Projects.ReadWrite`, `Scan`), with an `msauth.<bundle id>://auth` redirect added to the web app's registration by `infra/azure/setup.ps1`. The free tier has no account and sends nothing; signed in with Pro, saving online goes to the same API with the same project file, and scanning and transcription to the same scan server. The Pro flag and the testers' switch are item 14.
5. **Import** opens MIDI, MusicXML and project files from Files and the share sheet. Scanning a page takes a photo with VisionKit's document camera, and transcription picks a recording; both go to the hosted scan server only, as in the web, and are Pro.

### 3. Building, checking and releasing (items 15 and 24)

1. **The web site is the development environment**: every merge to main deploys it (0014) and every feature is tried there first. **The iPad app is production**: it gets releases, built in Xcode by Andrei from a tagged commit, not single features.
2. **Releases are tags.** `ipad-v<major>.<minor>.<patch>` names a commit of main; the app's `MARKETING_VERSION` is that number and `CURRENT_PROJECT_VERSION` counts builds. A release note lists the redesigned screens and features in it. Signing stays in the Mac's `Local.xcconfig`: a free Apple ID signs for 7 days at a time; TestFlight and the App Store need the paid Apple Developer Program, which Andrei decides when the first release is ready.
3. **Swift is built and tested on GitHub's macOS runners**, in its own workflow, not in CI: it builds both frameworks (cached by the music-core and Verovio sources they come from), generates the project with XcodeGen, runs the Swift tests in the iPad simulator and builds for a device without signing. It runs on pull requests that touch the iPad app, music-core or the facade, and when asked. The repository is public, so the macOS minutes are free; the five-minute limit of CI does not apply to it. The cloud sessions that write the code cannot compile Swift, so this workflow is where Swift is first checked; a run on Andrei's iPad is the last check before a release.
4. **The Swift shell of 0015 stays in `ios/` until the first native release replaces it**, then it is deleted together with its configuration. Until then it opens the web site; what it shows while the redesign lands on main is Andrei's choice (open question below).

### 4. Where it lives

- `ipad/Core` — the NativeAOT bridge project (the facade's five exports and their C header) and its build script.
- `ipad/Verovio` — the Verovio build script, the pinned version and the locale patch.
- `ipad/App` — the SwiftUI app, generated by XcodeGen from `ipad/App/project.yml` (the Xcode project is not committed), with `Config/Base.xcconfig` and a per-Mac `Local.xcconfig` as the shell has.
- `ipad/App/Tests` — the Swift tests: the facade round trip, the pencil layer's anchors, the audio renderer's timing against the playback score.
- `.github/workflows/ipad.yml` — the macOS workflow.

The spike (`spikes/ios-core`, PR #176) moves into these folders and is closed.

### 5. What stays in the web app only

Nothing is meant to stay web only: both apps get every screen of the redesign, web first. Until a screen is native, the web app is where it is used. The web keeps what only a browser needs: the service worker and offline start, the PWA manifest and Web MIDI (Stage 2). The PDF writer (`Scripts/score/pdf-document.ts`) runs in the score's web view on the iPad too, and the share sheet offers its file.

## Open question

**What the iPad shows while the redesign lands on main.** The shell opens the site, so half-done redesigned screens would reach the iPad Andrei plays at. The options: a second, release copy of the site deployed from the `ipad-v*` tags (a named Static Web Apps environment with its own stable address, which the sign-in redirects and the services' allowed origins then name; projects saved only on the iPad move once, online or by export); keeping the shell on main; or the published web app inside the shell's bundle. Recommended: the release copy, because it needs no new code in a shell that is about to be replaced.

## Consequences

- One engine, one project file, one notation engine and one geometry module for both apps; a fix in music-core reaches both. A change to the facade's contract is a change for two clients, so its version (0020) is checked by both.
- Each redesigned screen is built twice, in Razor and in SwiftUI, from the same tokens, strings and icons (0018 section 14, item 26). The web version is the reference the SwiftUI one is checked against.
- The app is about 25 MB with both frameworks and the piano samples. A NativeAOT build takes a few minutes on the macOS runner; the cache keeps that to changes in music-core.
- Swift written in the cloud is unchecked until the macOS workflow runs, so each Swift change waits for that workflow before it merges.
- Verovio's LGPL is met by the dynamic framework, the published source and patch and the licence screen, but the App Store question stays open until it is asked.
- iPad releases are as frequent as Andrei builds them; a fix on main reaches the web at once and the iPad at the next release.
