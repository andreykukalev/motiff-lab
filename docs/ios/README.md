# Motiff Lab for iPad and iPhone

Status: proposal, 2026-10-24. Owner: Andrei. Nothing here changes the Stage 1 plan: the web app stays the primary MVP track, and no app code changes until the work below is scheduled.

These documents describe how the current web workspace (playback lab, piano roll, score, reshape and variation controls, Keep chips, facts panel, hand edits) becomes an iPad and iPhone app that shares music-core with the web.

| File | What it covers |
| --- | --- |
| [01-concept.md](01-concept.md) | Why an iOS app, who it is for, what it does in each stage, what stays the same as the web, and what it must never do |
| [02-ux.md](02-ux.md) | Layouts for iPad and iPhone, the touch gesture map for selection and editing, panels as sheets and inspectors, hover-free hints, accessibility, hardware keyboard, Pencil and trackpad |
| [03-technical-design.md](03-technical-design.md) | Approach options compared, the recommended architecture, audio and latency, offline projects on the device, privacy, rights and App Store constraints, build and CI, and the phased plan with exit criteria |
| [../decisions/0011-ios-app-approach.md](../decisions/0011-ios-app-approach.md) | Proposed decision record: .NET MAUI Blazor Hybrid shell over a shared UI library, with a native audio engine before Stage 2 |

## The recommendation in short

1. **Build the iOS app as a .NET MAUI Blazor Hybrid shell** that hosts the same Razor components and TypeScript views as the web app, with music-core running natively in the app's .NET process. This is the path decision record 0001 already points at (MAUI from Stage 2), and it reuses almost everything that exists today.
2. **Put platform differences behind small interfaces** (project storage, file exchange, audio session, haptics, later MIDI and a native audio engine), so the web and the app differ only in their implementations.
3. **Keep Web Audio in the web view for Stage 1 playback on iOS, then move to AVAudioEngine** before Stage 2 needs low-latency input. A spike measures both before anything is committed.
4. **Design touch as its own interaction model**, not as mouse emulation: one finger pans, the bar ruler and the Pencil select, long press selects in the note area, and every hint that is a hover tooltip on the web becomes visible text or a tap.
5. **Everything stays on the device**, as in Stage 1 on the web. The App Store privacy label says "Data not linked to you: usage data" and nothing else. Verovio's LGPL licence in an App Store build needs a legal check before the first TestFlight build.
6. **Phases**: spikes first (a few days each), then a shared UI library extracted when the web UI is quiet, then an iPad TestFlight alpha, then iPhone, then the native audio engine and MIDI for Stage 2.

## How this relates to the rest of the docs

- The domain model (05), the time model (0002), the note event (0004) and every music-core rule are unchanged. The app is another host for the same engine.
- The MVP UX spec (04) already says tablet landscape is the main practice layout and phones stack the same parts; [02-ux.md](02-ux.md) applies that rule to the Stage 1 workspace as it is built today.
- The UI conventions ([../ui-conventions.md](../ui-conventions.md)) still hold. Where touch needs a different gesture for the same action, [02-ux.md](02-ux.md) says so; the action and its result stay the same.
