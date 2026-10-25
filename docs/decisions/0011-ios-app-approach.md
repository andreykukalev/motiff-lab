# 0011 · iOS app approach

Status: proposed, 2026-10-24. To be accepted, changed or rejected after the spikes in phase 1 of [../ios/03-technical-design.md](../ios/03-technical-design.md). The web app remains the primary Stage 1 track.

## Context

Andrei plans to deliver Motiff Lab as an iPad and iPhone app. Decision record 0001 already says music-core runs "on mobile (.NET MAUI, from Stage 2)" and that low-latency mobile audio needs a spike before Stage 2. Since then the web workspace has grown: a Blazor WebAssembly page with a canvas piano roll, a Verovio score view, an AudioWorklet playback engine, reshape and variation controls with Keep chips, a facts panel and hand edits, all over music-core. The question is how an iOS app reuses that without a second UI to maintain, while reaching what Stage 2 needs (CoreMIDI input, low-latency native audio) and keeping every Stage 1 rule (one engine, nothing leaves the device, AI optional).

## Options considered

| Option | Verdict |
| --- | --- |
| PWA on iOS Safari | Kept as the zero-cost way to try Stage 1 on an iPad. Rejected as the iOS product: no Web MIDI, no audio session control, evictable storage, no App Store presence |
| Capacitor or a thin Swift shell around the published WebAssembly app | Fallback. Fastest to TestFlight, but music-core stays interpreted .NET in WebAssembly, native features cross a JavaScript bridge, and it adds Node and Swift toolchains |
| **.NET MAUI Blazor Hybrid over a shared Razor class library** | **Proposed.** Reuses the Razor components and the TypeScript views in WKWebView, runs music-core natively (AOT) in the app process, and adds native services in C# |
| .NET MAUI with native controls | Rejected for now: rewrites every view and still needs a web view or a native Verovio build for the score |
| SwiftUI | Rejected for now: no reuse of the UI, and either a port of music-core (breaks "one engine") or a C ABI bridge to a NativeAOT library. Revisit only if the hybrid misses the UX or latency targets |

## Decision (proposed)

1. **Shared UI library.** Move the Razor pages, components, TypeScript sources and shared assets from `MotiffLab.Web` into a Razor class library, `MotiffLab.Ui`. `MotiffLab.Web` becomes the WebAssembly host; a new `MotiffLab.App` (.NET MAUI, iOS first) hosts the same library in a `BlazorWebView`. This is one mechanical PR, scheduled when the web backlog is quiet.
2. **Platform services behind interfaces** in `MotiffLab.Ui`: project store, file exchange, audio session, haptics, idle timer; later playback engine and MIDI input. Each has a web and an iOS implementation. No musical logic lives in a service.
3. **Audio in two steps.** Stage 1 on iOS keeps the AudioWorklet engine (0007) inside WKWebView with a native `AVAudioSession` set to playback. Before Stage 2, a native engine on AVAudioEngine implements the same algorithm behind `IPlaybackEngine`, tested against the same cases as the TypeScript engine, with CoreMIDI input.
4. **Projects on the device as files**, in Application Support, in the existing `ProjectFile` format, so web and iOS exchange projects by export and import. No Motiff Lab sync or cloud copy in Stage 1.
5. **Touch is designed, not emulated**: the gesture map and hover-free hints in [../ios/02-ux.md](../ios/02-ux.md). The actions and their results are the ones music-core defines for the web.
6. **Separate iOS CI** on a macOS runner with path filters and manual TestFlight upload; the existing `ci.yml` is unchanged.

## Spike results to record here before acceptance

| Measure | Target | Result |
| --- | --- | --- |
| Start to first sound, iPad (A12 or newer) | Under 3 s | — |
| Variation preview after a tap | Under 300 ms (S1-E3) | — |
| Loop wrap | No audible gap | — |
| Touch to sound, roll key, WKWebView | Under 30 ms | — |
| MIDI in to sound, AVAudioEngine | Under 10 ms | — |
| AudioWorklet module and Verovio load from the app scheme | Both load and run | — |
| Verovio LGPL in an App Store build | Exception granted or legal opinion obtained | — |

If the hybrid misses the first five targets badly, switch to the Capacitor fallback for Stage 1 and decide Stage 2 audio separately. If the Verovio question cannot be settled, use a permissive engraver on iOS for the score as written until it is.

## Consequences

- Extends 0001: MAUI arrives as a Blazor Hybrid host, earlier than Stage 2 if the iPad alpha proves useful for the Stage 1 pilot, and with native controls only where the platform needs them.
- The web app gains platform service interfaces and a cleaner split between host and UI; its behaviour does not change.
- A Mac with Xcode, an Apple Developer Program membership and a macOS CI workflow become requirements for the iOS track.
- The App Store privacy label, privacy manifest, EU trader declaration and Verovio licence check are prerequisites for external TestFlight and release.
- `docs/ui-conventions.md` gains a touch section when the iPad alpha is built, taken from [../ios/02-ux.md](../ios/02-ux.md).
