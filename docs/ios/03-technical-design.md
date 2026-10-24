# iOS technical design

Status: proposal, 2026-10-24. The decision it asks for is in [../decisions/0010-ios-app-approach.md](../decisions/0010-ios-app-approach.md). Nothing here changes code on the web track; the phased plan at the end says when each piece would start.

## Starting point

What exists today, and what an iOS app can reuse:

| Layer | Today | Reusable on iOS |
| --- | --- | --- |
| music-core (`src/MotiffLab.MusicCore`) | C# on .NET 10, pure, no I/O, no browser dependency | As is. Runs in any .NET host, including .NET for iOS with AOT |
| Razor UI (`src/MotiffLab.Web/Pages`, `Roll`, `Score`, `Facts`, `Playback`, `Projects`) | Blazor WebAssembly components; the workspace is one large page (`PlaybackLab.razor`) | Yes, in a Blazor Hybrid web view, once moved to a Razor class library |
| TypeScript layer (`Scripts/playback`, `roll`, `score`, `storage`) | AudioWorklet engine, canvas roll and keyboard, Verovio score view, IndexedDB store | Playback, roll and score run in WKWebView unchanged. Storage gets a native implementation |
| Assets | Salamander piano samples (CC BY 3.0, 1.7 MB), Verovio (LGPL-3.0, 7.3 MB) | Bundled in the app instead of cached by the service worker |
| AI gateway and analytics (Azure Functions, West Europe) | HTTPS endpoints | Same endpoints, same rules |
| Project file (`ProjectFile`, `.motiff.json`, version 3) | Export and import on the web | The interchange format between web and iOS |

## Approach options

| Option | How it works | Reuse | Native audio and MIDI | Effort for one .NET developer | Verdict |
| --- | --- | --- | --- | --- | --- |
| A · PWA on iOS Safari | Add to Home Screen; the web app as it is | Everything | None: no Web MIDI in Safari, no control over the audio session or buffer size, storage that Safari can evict | Nothing beyond touch fixes in the web track | Good enough for Stage 1 trials on an iPad today; not an App Store product and a dead end for Stage 2 |
| B · Capacitor (or a thin Swift shell) around the published WebAssembly app | A native shell loads the published `wwwroot` from the bundle in WKWebView; native features through Capacitor plugins in Swift | Everything, with no refactor | Through Swift plugins over a JavaScript bridge | Low to start; adds a Node and Swift toolchain; music-core still runs as WebAssembly with the .NET interpreter inside the web view | Fastest path to TestFlight. A fallback if B's measured cost is far lower than C's |
| **C · .NET MAUI Blazor Hybrid** | A MAUI app hosts a `BlazorWebView` (WKWebView). Razor components run natively in the app's .NET process (AOT compiled), render into the web view, and call the same TypeScript modules through JS interop | Razor components, TypeScript layer, music-core (native, not WebAssembly) | Native services in C# (CoreMIDI, AVAudioEngine, AVAudioSession, haptics) injected into the same components | Medium: extract a Razor class library, add platform service interfaces, a MAUI head project | **Recommended.** One language for the developer, one engine, native speed for music-core, a clean path to native audio in Stage 2. Matches decision record 0001 |
| D · .NET MAUI with native controls | XAML or a SkiaSharp canvas for the roll and score; music-core in process | music-core only | Native | High: every view rewritten; the score needs Verovio compiled natively or a web view anyway | Rejected for now. Two UIs to keep in step for no Stage 1 gain |
| E · SwiftUI app | Native Swift UI; music-core either ported to Swift or exposed from a NativeAOT C library | UI: none. Engine: none if ported, or through a C ABI if not | Native, best in class | Very high, and a port breaks "one engine" | Rejected. Revisit only if C cannot reach the UX or latency targets after the spikes |

Why C over B, given B is cheaper to start:

- In C, music-core runs as AOT-compiled native code, not as .NET interpreted inside WebAssembly. Variation previews must play within 300 ms (S1-E3, NFR-5); a native engine gives that margin on older iPads without turning on WebAssembly AOT for the web.
- Stage 2 needs CoreMIDI input and a native audio engine that share the canonical clock with music-core. In C both are C# services in the same process as the engine; in B every MIDI event and every scheduling call crosses a JavaScript bridge.
- The developer stays in C# and Razor. B adds Capacitor, npm and Swift for plugins.
- The cost of C is one refactor (the Razor class library), which also makes the web app cleaner. It is done once, when the web UI is quiet, so it does not collide with backlog work.

B stays the fallback: if the MAUI spike shows blocking problems with WKWebView hosting (AudioWorklet modules from the app scheme, Verovio size, start time), the same published web app can be wrapped in B in days.

## Recommended architecture

```
┌──────────────────────── iOS app (MotiffLab.App, .NET MAUI) ────────────────────────┐
│                                                                                    │
│  BlazorWebView (WKWebView)                          .NET process (AOT)             │
│  ┌──────────────────────────────┐   JS interop     ┌─────────────────────────────┐ │
│  │ Rendered Razor UI            │◀────────────────▶│ MotiffLab.Ui (Razor class   │ │
│  │ Scripts: roll, score         │                  │ library): pages, components │ │
│  │ (Verovio), playback worklet  │                  ├─────────────────────────────┤ │
│  │ (Stage 1)                    │                  │ MotiffLab.MusicCore         │ │
│  └──────────────────────────────┘                  ├─────────────────────────────┤ │
│                                                    │ Platform services (iOS):    │ │
│                                                    │ project store (files),      │ │
│                                                    │ file exchange (picker,      │ │
│                                                    │ share sheet), audio session,│ │
│                                                    │ haptics, idle timer; later  │ │
│                                                    │ native audio, CoreMIDI      │ │
│                                                    └─────────────────────────────┘ │
└────────────────────────────────────────────────────────────────────────────────────┘

┌──────────── Web app (MotiffLab.Web, Blazor WebAssembly PWA) ────────────┐
│ MotiffLab.Ui + MotiffLab.MusicCore (WebAssembly)                        │
│ Platform services (web): IndexedDB store, download and file input,     │
│ navigator.audioSession, no haptics                                      │
└─────────────────────────────────────────────────────────────────────────┘
```

### Projects

| Project | Contents | New? |
| --- | --- | --- |
| `src/MotiffLab.MusicCore` | Unchanged | No |
| `src/MotiffLab.Ui` | Razor class library: the pages and components now in `MotiffLab.Web` (`Pages`, `Roll`, `Score`, `Facts`, `Playback`, `Projects`, `Import`), the TypeScript sources and the shared static assets (CSS, piano samples, Verovio) as static web assets, and the platform service interfaces | Yes, extracted |
| `src/MotiffLab.Web` | The WebAssembly host: `Program.cs`, `index.html`, service worker, manifest, Static Web Apps config, and the web implementations of the platform services | Slimmed |
| `src/MotiffLab.App` | The MAUI head, iOS only at first (`net10.0-ios`): `MauiProgram`, the `BlazorWebView` page, iOS service implementations, `Info.plist`, entitlements, privacy manifest, app icons | Yes |
| `tests/MotiffLab.Ui.Tests` (optional) | bUnit tests for component behaviour that does not need a browser | Later |

The web build and CI stay as they are; `MotiffLab.App` is excluded from the solution's default build on Linux (it needs macOS and Xcode) and built in its own workflow.

### Platform services

Small interfaces in `MotiffLab.Ui`, each with a web and an iOS implementation. The components depend on the interfaces only.

| Interface | Web implementation | iOS implementation |
| --- | --- | --- |
| `IProjectStore` (save, load, list, delete, storage use) | `Scripts/storage/project-store.ts` over IndexedDB, as `ProjectStore.cs` does now | Files in `Library/Application Support/Projects/<id>.motiff.json` with an index file; `NSFileProtectionCompleteUntilFirstUserAuthentication`; storage use from the file sizes and the volume's available capacity |
| `IFileExchange` (pick a file to import, receive an opened file, hand over an export) | `<input type=file>`, drag and drop, download through the storage script | `UIDocumentPickerViewController`, "Open in" and AirDrop through declared document types, drag and drop on iPad, `UIActivityViewController` for export |
| `IAudioSession` (category, interruptions, route changes, output latency) | `navigator.audioSession.type = "playback"` where supported; `AudioContext.outputLatency` | `AVAudioSession` category `.playback` (Stage 1) or `.playAndRecord` (Stage 3), preferred IO buffer duration, interruption and route-change notifications that pause the player |
| `IHaptics` | No-op | `UISelectionFeedbackGenerator` and `UIImpactFeedbackGenerator` |
| `IIdleTimer` | No-op (or the Screen Wake Lock API) | `UIApplication.IdleTimerDisabled` while playing or looping |
| `IPlaybackEngine` (Stage 2) | The current AudioWorklet engine through `Player.cs` | Stage 1: the same worklet in WKWebView. Stage 2: a native engine on AVAudioEngine (below) |
| `IMidiInput` (Stage 2) | Web MIDI in Chrome and Edge | CoreMIDI: USB, Bluetooth LE MIDI, network sessions |

`Player.cs` today talks to the TypeScript engine directly. Extracting `IPlaybackEngine` from it is the one seam that matters for audio; the rest of the UI keeps talking to `Player`.

### Rules that keep the platforms honest

- No musical logic in a platform service. Services move bytes, sound and system events; music-core computes everything else.
- The project file is the contract. Both platforms read and write `ProjectFile` version 3 (and later versions) through music-core, so a project exported on the web opens on iOS with identical notes, facts and variations. A cross-platform test opens the same fixture projects in both hosts in CI.
- Determinism holds across platforms: the same inputs and seed give the same notes and diff. music-core already uses integer ticks and no floating point for timing (0002), so AOT on ARM64 and WebAssembly agree; the existing xUnit tests run on both in CI (`dotnet test` on Linux, and the music-core tests on an iOS simulator in the iOS workflow).

## Audio and latency

### What matters in each stage

| Use | Stage | What the player notices | Target |
| --- | --- | --- | --- |
| Loop restart | 1 | A gap or click at the wrap | No audible gap (the engine already wraps on the exact frame) |
| A/B switch and variation preview | 1 | Delay after a tap; a silent held chord | Preview plays within 300 ms (S1-E3); switch at the same tick (0007) |
| Playhead and highlight sync | 1 | Notes lit before or after they sound | Within 20 ms of what is heard, using the reported output latency |
| Touching a key or a note to hear it | 1 | Lag between touch and sound | Under 30 ms touch to sound on a current iPad, wired or built-in speaker |
| Playing a digital piano into the app (MIDI in) and hearing the app's piano | 2 | Feels like a laggy instrument | Under 10 ms MIDI to sound |
| Aligning an attempt to the reference | 2 | Notes judged early or late | Timestamps compensated to within 5 ms using CoreMIDI timestamps and measured output latency |
| Microphone input | 3 | Recorded attempt misaligned | Round-trip latency measured and compensated per route |

### Stage 1: Web Audio inside WKWebView

The AudioWorklet engine (0007) works in WKWebView as in Safari. iOS adds:

- **Audio session.** Set `AVAudioSession` category `.playback` natively at start, so the silent switch does not mute the piano and other apps' audio is paused while Motiff Lab plays. This replaces the web's `navigator.audioSession` hint.
- **Unlock on first gesture.** The `AudioContext` still starts suspended; the existing resume on first pointer or key gesture covers it.
- **Output latency.** WKWebView's `outputLatency` may be missing or rough; the native `IAudioSession` reports `AVAudioSession.outputLatency + ioBufferDuration` and the TypeScript player uses it for the playhead (the existing "tick being heard" calculation takes a latency figure already).
- **Interruptions and routes.** Calls, Siri, alarms and unplugged headphones pause the player through `Player` so the UI and the engine agree on the position.
- **Background.** Stage 1 does not play in the background: a loop is practice at the instrument with the screen on. The idle timer is disabled while playing. If players ask for background listening, it needs the `audio` background mode and a native engine (a web view's audio is suspended in the background).

Touch to sound through WKWebView is the open question: a touch event reaches the page, crosses into .NET for the selection, and comes back to the worklet. The "live" source path for keys (ui-conventions, Hearing notes by hand) is already handled in TypeScript without a .NET round trip, which keeps it short. The latency spike measures it.

### Stage 2: native engine on AVAudioEngine

- An `AVAudioSourceNode` (or `AUAudioUnit` render block) runs the same algorithm as `Scripts/playback/playback-processor.ts`: one canonical tick clock advanced per render block, split at tempo changes, the loop end and the end of the piece; nearest-sample pitch shift with linear interpolation; velocity scaling; damping of re-struck keys; 120 ms release; 96-voice cap; a limiter or compressor after it.
- Written in C# on the render thread is risky (garbage collection pauses). The render callback must not allocate; the options are C# with preallocated buffers and `UnmanagedCallersOnly` callbacks, or a small C or Swift render kernel that takes a note list and parameters from C#. The spike decides; the rule stays that music-core builds the `PlaybackScore` and the engine only renders it.
- The engine is tested against the same cases as the TypeScript engine (`tests/web/playback-processor.test.mjs`): exact note frames, tempo, loops, metronome, count-in, solo and mute, source switching, layering, position reports, voice cap. A shared set of expected frames in JSON keeps both engines in step.
- Buffer: 128 to 256 frames at 48 kHz (about 2.7 to 5.3 ms) for MIDI-in use; larger when only listening, to save battery.
- MIDI input: CoreMIDI with host timestamps, converted to the canonical timeline through the engine's clock. USB-C MIDI and Bluetooth LE MIDI pianos are the common cases; Bluetooth MIDI adds about 10 to 20 ms, noted in the UI.
- Bluetooth headphones add 150 to 250 ms of output latency. The playhead compensates; for attempts the app recommends wired, USB or built-in speakers and states why.

### Spike to run before committing (from 0001: "prove it with a spike before Stage 2")

1. Play the Für Elise lab in a MAUI Blazor Hybrid build on an iPad (A12 or newer) and an iPhone. Measure: start time to first sound, variation preview time, loop gaps, playhead offset.
2. Measure touch to sound for a roll key with a high-speed phone video (240 fps) or an audio loopback, in Safari, in WKWebView, and with a minimal AVAudioEngine sampler.
3. Measure MIDI in to sound with a USB MIDI piano in the minimal AVAudioEngine sampler.
4. Check that `AudioWorklet.addModule` loads from the Blazor Hybrid app scheme, and Verovio's WebAssembly loads and runs from the bundle.

## Offline and on-device projects

- **Everything is bundled.** The app ships the .NET UI and music-core compiled for iOS, the TypeScript output, the piano samples and Verovio. No service worker, no first-use download, nothing to evict. App size estimate: 30 to 50 MB (to be measured; Verovio 7.3 MB, samples 1.7 MB, .NET runtime and AOT code most of the rest).
- **Projects are files** in Application Support, one `.motiff.json` per project with the source file inside it as the web already stores it, plus a small index for the list. Saves are atomic (write to a temporary file, then replace), with the same autosave points as the web.
- **The imported file stays in the project.** Imports from Files are copied in, so the project does not depend on a security-scoped bookmark to the original.
- **Backups.** Application Support is included in the player's own iCloud or computer backup by default. That backup is the player's, under their Apple account, not a copy held by Motiff Lab; the privacy notice says so. A setting can exclude projects from backup (`isExcludedFromBackup`) for players who want nothing copied.
- **No Motiff Lab sync in Stage 1.** Moving a project between the web and the iPad is an export and an import of the project file. iCloud Drive document sync or accounts with sync arrive with Stage 2 accounts, and need their own privacy review.
- **Document types.** The app declares it opens Standard MIDI files (`public.midi-audio`, `.mid`, `.midi`), MusicXML (`com.recordare.musicxml`, `.musicxml`, `.xml`) and compressed MusicXML (`com.recordare.musicxml.compressed`, `.mxl`), and exports and imports its own project type (`.motiff.json`, an exported type identifier under the app's domain).
- **Data protection.** Files use complete-until-first-unlock protection, so a locked, rebooted device keeps them encrypted.

## Privacy, rights and App Store constraints

### GDPR and the Stage 1 rule

- Nothing the player imports, edits or writes leaves the device. The only network calls are the anonymous analytics endpoint (no note content) and, if the player turns AI on, the AI gateway with the fact digest (0003). Both are in West Europe.
- No accounts, no advertising identifier, no third-party SDKs. App Tracking Transparency is not needed because the app does not track.
- **App Store privacy label:** "Data not linked to you: usage data (product interaction)" for analytics; if AI is on, the digest is processed and not stored, which is declared as "other usage data, not linked, not used for tracking" or left out if Apple's definition of "collected" does not apply to transient processing (to confirm in the legal review).
- **Privacy manifest** (`PrivacyInfo.xcprivacy`): no tracking domains; collected data types as above; required-reason APIs declared for file timestamps, disk space (the storage line) and `UserDefaults` (settings). .NET for iOS and MAUI ship their own manifests; the app's manifest covers its own use.
- **Consent and settings.** AI is off until the player turns it on in Settings, with the "What is sent" panel, as on the web. Analytics follow the web's consent rule.
- **Deleting a project** deletes its file; uninstalling the app deletes everything. There is nothing on a server to delete.
- **EU distribution.** Apple requires a trader status declaration under the Digital Services Act for apps sold or distributed in the EU. Declaring as a trader publishes an address, phone number and email on the App Store page. Decide whether to publish as an individual or through a company before the first public release; TestFlight testing is not affected.
- **Export compliance.** The app uses only standard HTTPS, so `ITSAppUsesNonExemptEncryption` is `NO`.

### Rights

- No import from Apple Music, the music library, streaming or video. The app reads MIDI and MusicXML files only, as on the web; MusicKit is not used.
- No sharing inside the app. Export of MIDI or a project goes to a destination the player picks in the share sheet, for personal practice.
- **Salamander Grand Piano (CC BY 3.0).** Credit shown on the About and credits screen and wherever the piano plays, as 0007 requires.
- **Verovio (LGPL-3.0-or-later) is the main legal risk.** On the web, LGPL section 4 is met because players can replace the separately loaded files. In an App Store app the files sit in a signed bundle the player cannot change, which is the situation that has made (L)GPL software hard to ship on the App Store before. Options, in order of preference:
  1. Ask the Verovio maintainers (RISM Digital Center) for a written App Store exception or a dual licence. Several LGPL projects grant this.
  2. Get a legal opinion on whether shipping the unmodified Verovio WebAssembly as a separate file, with source, version, licence text and our own object files available on request, satisfies section 4 in an App Store build.
  3. Fall back to a permissively licensed engraver on iOS (OpenSheetMusicDisplay, BSD-3-Clause, for MusicXML as written) and keep generated notation in the roll only until a solution exists. 0008 rejected OSMD for good reasons, so this is a last resort.
  This must be settled before the first TestFlight build to external testers.
- The starter library ships with its per-file licences, as on the web.

### App Store review

- Guideline 4.2 (minimum functionality) is the usual risk for web-view apps. Motiff Lab is offline, uses native file handling, the share sheet, the audio session and haptics, and has no web navigation, which is the profile Apple accepts. Keep the app free of any in-app web browsing.
- Guideline 2.5.2 (no downloaded code): all code and Verovio ship in the bundle; nothing is fetched and run at run time.
- AI features: the app states that AI text is generated, cites facts, and can be turned off, in line with Apple's rules on generated content.
- Paid features later (Stage 2 subscriptions) go through in-app purchase; the Small Business Program rate applies to a new developer account.

## Build, CI and distribution

- **Toolchain.** Building for iOS needs macOS with Xcode and the .NET `ios` and `maui` workloads; a Mac (or a cloud Mac) is required for builds, signing and the simulator. Development can still happen on Windows with a paired Mac, but device testing needs Xcode.
- **CI.** A separate workflow, `ios.yml`, on a GitHub macOS runner: restore with the workloads, build `MotiffLab.App` for the simulator, run the music-core tests on the simulator, and on manual dispatch archive, sign and upload to TestFlight through an App Store Connect API key. It runs only when `src/MotiffLab.Ui`, `src/MotiffLab.App` or music-core change, and on manual dispatch, because macOS minutes cost ten times Linux minutes on private repositories. The existing `ci.yml` stays the required check and is unchanged; docs-only changes such as this one still skip its build.
- **Signing.** An Apple Developer Program membership ($99 a year), a distribution certificate and an App Store provisioning profile, stored as encrypted GitHub secrets or fetched with the App Store Connect API at run time.
- **Distribution.** TestFlight internal testing for development, TestFlight external testing for the pilot and study (up to 10,000 testers, a light beta review), then the App Store.
- **Minimum OS.** iPadOS and iOS 17 or later, to be confirmed by the spike: it covers iPads and iPhones from 2018 on, its WKWebView has the AudioWorklet and WebAssembly behaviour the web already relies on in Safari, and it keeps the test matrix small.

## Phased plan

Each phase has an exit check. The web track keeps priority: phases 1 and 2 do not start until the web workspace for the pilot is stable, and phase 2 lands as one PR at a quiet moment so it does not collide with backlog PRs.

| Phase | Contents | Size | Exit check |
| --- | --- | --- | --- |
| 0 · Docs (this PR) | Concept, UX, technical design, proposed decision record 0010 | Done here | Andrei accepts or changes 0010 |
| 0b · Web touch readiness (web track, optional) | Small web changes that help both platforms: show disabled reasons and Keep status as text, not only `title`; `touch-action` on the roll; multi-touch keys. Filed as issues for the backlog, not done here | A few small PRs on the web track | The web workspace works on an iPad in Safari with a finger |
| 1 · Spikes | MAUI Blazor Hybrid shell running the lab; audio and touch latency; MIDI in with AVAudioEngine; AudioWorklet and Verovio loading in the app scheme; Verovio licence question sent | 1 to 2 weeks, part-time | Numbers recorded in 0010; go, switch to option B, or stop |
| 2 · Shared UI library | Extract `MotiffLab.Ui`; add the platform service interfaces with web implementations; no behaviour change on the web | About a week; one PR | Web CI green, preview deploy behaves the same, no open backlog PR conflicts |
| 3 · iPad alpha | `MotiffLab.App` for iPad: iOS services (files, project store, audio session, haptics, idle timer), the iPad layouts and gesture map from [02-ux.md](02-ux.md), document types, credits screen, privacy manifest, `ios.yml` | 3 to 4 weeks | Internal TestFlight: the Stage 1 loop works offline with a finger; a web project opens identically; Verovio licence settled |
| 4 · iPhone | Compact layouts, sheets, Roll or Score switch, landscape "on the stand" | 2 weeks | External TestFlight with pilot participants; usability criteria from the Stage 1 study met on iPad and iPhone |
| 5 · Stage 2 readiness | Native `IPlaybackEngine` on AVAudioEngine; `IMidiInput` on CoreMIDI; latency calibration; App Store submission | 3 to 5 weeks, after the Stage 1 exit gate | Latency targets above met; engine parity tests pass; App Store review passed |

## Risks

| Risk | Likelihood | Mitigation |
| --- | --- | --- |
| Verovio's LGPL blocks App Store distribution | Medium | Ask for an exception early (phase 1); legal opinion; permissive fallback for iOS only |
| Touch-to-sound latency in WKWebView too high for playing along | Medium | Stage 1 needs it only for hearing keys; Stage 2 moves to the native engine anyway |
| MAUI tooling friction (workloads, Xcode versions, hot reload) slows a solo developer | Medium | Keep the MAUI head thin; most work happens in `MotiffLab.Ui` with the fast web loop; option B as fallback |
| Extracting the Razor class library collides with web backlog PRs | Medium | One mechanical PR at a quiet moment, announced in the project chat; moves only, no edits |
| C# render callback glitches from garbage collection | Medium | No allocation on the render thread, or a small native kernel; decided in the spike |
| App size and start time with .NET and Verovio | Low | Trimming and AOT; load Verovio on first score use, as on the web |
| Apple rejects as a "wrapped website" (4.2) | Low | Native file handling, share sheet, audio session, haptics; no web browsing; offline |
| macOS CI cost | Low | Path filters and manual dispatch; most tests stay on Linux |
| EU trader declaration exposes personal contact details | Certain if publishing as an individual | Decide on individual or company before the public release |
