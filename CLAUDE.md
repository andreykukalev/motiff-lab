# Motiff Lab

Musical exploration workspace: help musicians move from reproducing music to understanding, transforming and creating it. Product and technical specs live in `docs/` (start with `docs/README.md`). The current stage is **Stage 1, notation-based motif exploration** (`docs/01-mvp-stage-1.md`).

## Stack

See `docs/decisions/0001-stack.md`.

- `src/MotiffLab.MusicCore` — C# music-core: shared event model, parsing, analysis, variation operations. Pure and deterministic; no UI, I/O or browser dependencies.
- `src/MotiffLab.Web` — Blazor WebAssembly PWA. Browser-only APIs (Web Audio, Web MIDI, canvas, IndexedDB) go through a thin TypeScript layer via JS interop.
- `tests/MotiffLab.MusicCore.Tests` — xUnit tests for music-core.

## Commands

```bash
dotnet build MotiffLab.slnx
dotnet test MotiffLab.slnx
dotnet format MotiffLab.slnx            # CI runs with --verify-no-changes
dotnet run --project src/MotiffLab.Web  # local dev server
```

Warnings are errors (`Directory.Build.props`).

## Conventions

- Reference requirement IDs (`S1-…`, `V1–V12`, `FR-…`) in commit messages and PR descriptions when code implements them.
- Musical time uses 480 ticks per quarter note (`TickResolution`); parsers rescale from the file's own resolution.
- Every event carries provenance and confidence. Anything inferred (hands, alignment, keys, chords) is marked inferred, never presented as imported fact.
- Variation operations are pure: same events, context, parameters and seed give the same events and diff. Explanations state only facts present in the diff.
- Nothing the player imports leaves the device in Stage 1. Analytics carry no note content.
