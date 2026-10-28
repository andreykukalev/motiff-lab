# Motiff Lab

Musical exploration workspace: help musicians move from reproducing music to understanding, transforming and creating it. Product and technical specs live in `docs/` (start with `docs/README.md`). The current stage is **Stage 1, notation-based motif exploration** (`docs/01-mvp-stage-1.md`). The product frame for the current work is `docs/08-central-product-model.md`: the loaded piece is the context for a work session and the phrase is the unit of work.

## Stack

See `docs/decisions/0001-stack.md` (stack), `0002-canonical-time.md` (musical time), `0003-ai-integration.md` (AI assistance), `0004-note-event-and-meter-map.md` (note event, bars and beats), `0007-playback-engine.md` (browser playback), `0008-score-view.md` (notation) `0012-workspace-structure.md` (projects, sessions and ideas, the score-first workspace, Focus mode, localization), `0014-hosted-scan-server.md` (the scan server in Azure Container Apps, Motiff Lab's Cosmos DB database) and `0013-interaction-model.md` (one musical object and one primary selection behind Practise, Understand and Explore, claim kinds with evidence, the Try it contract, Russian terms, the Guidance Layer).

- `src/MotiffLab.MusicCore` — C# music-core: shared event model, parsing, analysis, variation operations. Pure and deterministic; no UI, I/O or browser dependencies.
- `src/MotiffLab.Api` — ASP.NET Core API that saves a signed-in player's projects in Cosmos DB (0014), with xUnit tests in `tests/MotiffLab.Api.Tests`. Deployed by the `API` workflow, not CI.
- `src/MotiffLab.Web` — Blazor WebAssembly PWA. Browser-only APIs (Web Audio, Web MIDI, canvas, IndexedDB) go through a thin TypeScript layer via JS interop: sources in `Scripts/`, compiled to `wwwroot/js/` (not committed) during `dotnet build`.
- `tests/MotiffLab.MusicCore.Tests` — xUnit tests for music-core.
- `tests/web` — Node tests for the TypeScript layer, run on the compiled output.
- `services/omr` — the scan server for sheet music recognition: homr (AGPL-3.0, unmodified) behind a small Python HTTP service, run by the player in Docker on their own computer (`docs/decisions/0011-sheet-music-recognition.md`) or hosted in Azure Container Apps (`0014`, deployed by the `Scan server` workflow, not CI). Its unit tests run with `python -m pytest` in that folder.
- `tests/e2e` — Playwright tests of the published app: the core journey in Chromium, Firefox and WebKit (desktop and tablet), offline, and WCAG 2.2 AA with axe.

## Commands

```bash
dotnet build MotiffLab.slnx
dotnet test MotiffLab.slnx
node --test "tests/web/*.test.mjs"     # after a build; Node 22+
dotnet publish src/MotiffLab.Web -c Release -o publish && (cd tests/e2e && npm ci && npx playwright test)  # browser tests, locally
dotnet format MotiffLab.slnx            # CI checks whitespace: dotnet format whitespace --folder --verify-no-changes
dotnet run --project src/MotiffLab.Web  # local dev server
```

Warnings are errors (`Directory.Build.props`).

CI must stay within 5 minutes (the job's timeout): it runs the whitespace check, build, unit and TypeScript tests, then publishes and deploys. The browser tests are heavy and run in CI only when asked: ask Andrei before running them there (the `browser-tests` PR label, or a manual CI run with "Run the browser tests"). Keep CI light; run browser tests locally for the specs you touch.

## Conventions

- Reference requirement IDs (`S1-…`, `V1–V12`, `FR-…`) in commit messages and PR descriptions when code implements them.
- Musical time: absolute `Tick` positions and `TickSpan`s at 480 ticks per quarter (`MotiffLab.MusicCore.Time`). Importers convert source units through `MidiTimeBase` or `MusicXmlDivisions` to exact `QuarterFraction`s, accumulate positions exactly, and round only via `CanonicalTime` (onset and end separately). Never treat MusicXML divisions as MIDI PPQ, and never use floating point for timing.
- Every event carries provenance and confidence. Anything inferred (hands, alignment, keys, chords) is marked inferred, never presented as imported fact.
- Variation operations are pure: same events, context, parameters and seed give the same events and diff. Explanations state only facts present in the diff.
- Nothing the player imports leaves the device in Stage 1. The one exception is scanning (0011): a PDF or photo the player picks for it goes only to the scan server address they set, and the notes it returns are marked detected, never imported. Analytics carry no note content: events are built only through `MotiffLab.MusicCore.Analytics.AnalyticsCatalogue` (counts, flags, fixed choices), are off until the player opts in, and the privacy notice lists them from the catalogue.
- UI work follows `docs/ui-conventions.md`: the UI only displays music-core output and never changes analysis or variation code or its data shapes.
- AI interprets engine results; it never produces notes, timing, analysis or Keep checks. music-core has no AI or Azure dependency. AI output must cite fact IDs from the digest and propose only catalogue operations with in-range parameters; anything else falls back to template text. Every feature must work with AI off.
