# Motiff Lab

Musical exploration workspace: help musicians move from reproducing music to understanding, transforming and creating it. Product and technical specs live in `docs/` (start with `docs/README.md`). The current stage is **Stage 1, notation-based motif exploration** (`docs/01-mvp-stage-1.md`).

## Stack

See `docs/decisions/0001-stack.md` (stack), `0002-canonical-time.md` (musical time), `0003-ai-integration.md` (AI assistance), `0004-note-event-and-meter-map.md` (note event, bars and beats) and `0007-playback-engine.md` (browser playback).

- `src/MotiffLab.MusicCore` — C# music-core: shared event model, parsing, analysis, variation operations. Pure and deterministic; no UI, I/O or browser dependencies.
- `src/MotiffLab.Web` — Blazor WebAssembly PWA. Browser-only APIs (Web Audio, Web MIDI, canvas, IndexedDB) go through a thin TypeScript layer via JS interop: sources in `Scripts/`, compiled to `wwwroot/js/` (not committed) during `dotnet build`.
- `tests/MotiffLab.MusicCore.Tests` — xUnit tests for music-core.
- `tests/web` — Node tests for the TypeScript layer, run on the compiled output.

## Commands

```bash
dotnet build MotiffLab.slnx
dotnet test MotiffLab.slnx
node --test "tests/web/*.test.mjs"     # after a build; Node 22+
dotnet format MotiffLab.slnx            # CI runs with --verify-no-changes
dotnet run --project src/MotiffLab.Web  # local dev server
```

Warnings are errors (`Directory.Build.props`).

## Conventions

- Reference requirement IDs (`S1-…`, `V1–V12`, `FR-…`) in commit messages and PR descriptions when code implements them.
- Musical time: absolute `Tick` positions and `TickSpan`s at 480 ticks per quarter (`MotiffLab.MusicCore.Time`). Importers convert source units through `MidiTimeBase` or `MusicXmlDivisions` to exact `QuarterFraction`s, accumulate positions exactly, and round only via `CanonicalTime` (onset and end separately). Never treat MusicXML divisions as MIDI PPQ, and never use floating point for timing.
- Every event carries provenance and confidence. Anything inferred (hands, alignment, keys, chords) is marked inferred, never presented as imported fact.
- Variation operations are pure: same events, context, parameters and seed give the same events and diff. Explanations state only facts present in the diff.
- Nothing the player imports leaves the device in Stage 1. Analytics carry no note content.
- AI interprets engine results; it never produces notes, timing, analysis or Keep checks. music-core has no AI or Azure dependency. AI output must cite fact IDs from the digest and propose only catalogue operations with in-range parameters; anything else falls back to template text. Every feature must work with AI off.
