# Motiff Lab

Musical exploration workspace: help musicians move from reproducing music to understanding, transforming and creating it. Specs: `docs/` (start with `docs/README.md`). Current stage: **Stage 1, notation-based motif exploration** (`docs/01-mvp-stage-1.md`), framed by `docs/08-central-product-model.md`: the loaded piece is the context for a work session, the phrase is the unit of work.

This file holds the rules of the product and its code. How the work is run is in `governance/roles/common.md`: read it, and your task's role file in `governance/roles/`, before you start. `CLAUDE.md`, `governance/`, `.claude/`, `.github/` and `tests/acceptance/` are the agents' environment: they change only in an environment task, which Andrei applies.

## Stack

Decision records: `docs/decisions/`, listed with what each decides in `docs/README.md`.

- `src/MotiffLab.MusicCore`: C# music-core: shared event model, parsing, analysis, variation operations. Pure and deterministic; no UI, I/O or browser dependencies. Tests: `tests/MotiffLab.MusicCore.Tests`.
- `src/MotiffLab.Api`: ASP.NET Core API saving a signed-in player's projects in Cosmos DB (0014); tests in `tests/MotiffLab.Api.Tests`. Deployed by the `API` workflow, not CI.
- `src/MotiffLab.Web`: Blazor WebAssembly PWA. Browser-only APIs (Web Audio, Web MIDI, canvas, IndexedDB) go through a thin TypeScript layer via JS interop: sources in `Scripts/`, compiled to `wwwroot/js/` (not committed) by `dotnet build`; `tests/web` tests run on the compiled output.
- `services/omr`: the scan server: homr (AGPL-3.0, unmodified) and Basic Pitch (Apache-2.0) behind a small Python HTTP service, run by the player in Docker (0011) or hosted in Azure Container Apps (0014, `Scan server` workflow, not CI). Tests: `python -m pytest` there.
- `ipad`: native iPad app (0019, `ipad/README.md`): SwiftUI in `ipad/App` (XcodeGen, on a Mac); music-core as a NativeAOT framework with the facade's C functions in `ipad/Core` (in the solution, so CI compiles it); Verovio from source in `ipad/Verovio`. Swift is built and tested by the `iPad` workflow, not CI.
- `design/kit`: the redesign kit (0018); how the web app uses its tokens, fonts and icons is in `docs/ui-conventions.md`.
- `tests/e2e`: Playwright on the published app (browsers, offline, WCAG 2.2 AA).

## Redesign

The web Music Studio and the iPad Practice Studio are two workspaces of one product with no parity: a function is not built in one app because the other has it; weigh product decisions by `docs/decisions/0025-two-studios.md`. Both UIs are being rebuilt to 0018, replacing the old screens directly.

- Read `design/kit/HANDOFF.md` before any UI work. What the mockups invent (demo score, `phraseRanges()`, facts, counts, texts) is not data.
- Source of truth in `design/kit`, highest first: `docs/01-ux-concept.md` and `docs/02-design-system.md` (text) → `tokens.css` (values) → `ds.css` (components, score marks) → `screens/*.png` (look) → `docs/wireframes/*.png` (structure only). Text beats a picture.
- Never hard-code a colour, size, radius, shadow, font or timing: use a `tokens.css` token (a canvas reads them via `Scripts/layout/tokens.ts`). Port tokens, `ds.css` rules, icons and Verovio options, never the mockup HTML (`s*.html`).
- Non-negotiables: the score is the whole screen, nothing docked; one continuously scrolling document of A4 pages (no page jumps); the score is the selection surface (no phrase list or strip); one Insights stream (no Understand/Explore/Build tabs); any iPad, no stylus needed; one accent (cobalt = pointing and selection only; plum = what the app composed and Pro; amber = what the player marked); Russian and English for the interface and all generated content; on the device = free, cloud or AI = Pro, except sync between the workspaces, free with a quota (0025).

## Commands

```bash
dotnet build MotiffLab.slnx
dotnet test MotiffLab.slnx
node --test "tests/web/*.test.mjs"  # after a build; Node 22+
node --test "governance/tests/*.test.mjs"  # the guards
dotnet publish src/MotiffLab.Web -c Release -o publish && (cd tests/e2e && npm ci && npx playwright test)
dotnet format MotiffLab.slnx  # CI: dotnet format whitespace --folder --verify-no-changes
dotnet run --project src/MotiffLab.Web
```

Warnings are errors (`Directory.Build.props`). What CI `build-test` runs and when heavy tests may run: `common.md`; run the browser specs you touch locally.

## Conventions

- Musical time: absolute `Tick`s and `TickSpan`s at 480 per quarter (`MotiffLab.MusicCore.Time`). Importers convert source units via `MidiTimeBase` or `MusicXmlDivisions` to exact `QuarterFraction`s, accumulate exactly, round only via `CanonicalTime` (onset and end separately). MusicXML divisions are never MIDI PPQ; no floating point for timing.
- Every event carries provenance and confidence. Anything inferred (hands, alignment, keys, chords) is marked inferred, never shown as imported fact.
- Variation operations are pure: same events, context, parameters and seed give the same events and diff. Explanations state only facts in the diff.
- In Stage 1 nothing the player imports leaves the device, except: a PDF or photo picked for scanning (0011) and a recording's sound picked for transcription (0014) go only to the scan server address the player set, and the notes returned are marked detected, never imported; after sign-in with saving online on, project files go only to the Motiff Lab API (0014). Analytics carry no note content: events are built only via `MotiffLab.MusicCore.Analytics.AnalyticsCatalogue` (counts, flags, fixed choices), are off until the player opts in, and the privacy notice lists them from the catalogue.
- UI follows `docs/ui-conventions.md`: it only displays music-core output, never changes analysis or variation code or its data shapes.
- AI interprets engine results; it never produces notes, timing, analysis or Keep checks. music-core has no AI or Azure dependency. AI output cites fact IDs from the digest and proposes only catalogue operations with in-range parameters; else template text. Every feature works with AI off.
