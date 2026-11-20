# Motiff Lab

Musical exploration workspace: help musicians move from reproducing music to understanding, transforming and creating it. Specs: `docs/` (start with `docs/README.md`). Current stage: **Stage 1, notation-based motif exploration** (`docs/01-mvp-stage-1.md`), framed by `docs/08-central-product-model.md`: the loaded piece is the context for a work session, the phrase is the unit of work.

**iPad first** (`docs/architecture/adr/0032-ipad-first-web-archived.md`): the iPad app is the only actively developed client. The web app is archived in `archive/web`: it gets no changes, features, fixes, ports or browser tests, and is not built or deployed. What moved to `src/shared` because the iPad bundles it, and how the web comes back: `docs/web-archive.md`. Where everything lives and where new code and tests go: `docs/repository-structure.md` (0033). Never read, search or cite `archive/`: archived code stays out of an agent's context (`governance/roles/common.md`, "Platform focus").

This file holds the rules of the product and its code. How the work is run (versions on `develop` and `main`, experiments behind a switch, the six things only Andrei decides, roles and models) is in `governance/README.md`: read it, `governance/roles/common.md` and your role's file before you start. `CLAUDE.md`, `governance/`, `.claude/`, `.github/`, `tests/backend/Acceptance/` and the UX baseline (`docs/ux-baseline.md`, `tests/ipad/Baseline/`, `tests/contracts/page-scenarios/`) are the agents' own rules: a pull request that changes them is merged by Andrei.

## Stack

Decision records: `docs/architecture/adr/`, listed with what each decides in `docs/README.md`.

- `src/backend/MotiffLab.MusicCore`: C# music-core: shared event model, parsing, analysis, variation operations. Pure and deterministic; no UI, I/O or browser dependencies. Tests: `tests/backend/Unit/MotiffLab.MusicCore.Tests`.
- `src/backend/MotiffLab.Api`: ASP.NET Core API saving a signed-in player's projects in Cosmos DB (0014); tests in `tests/backend/Integration/MotiffLab.Api.Tests`. Deployed by the `API` workflow, not CI.
- `src/shared`: what the iPad bundles from the former web project: the TypeScript modules `Scripts/score`, `roll`, `layout`, `storage`, `playback` (compiled to `wwwroot/js/`, not committed, by `dotnet build src/shared`), `wwwroot/i18n`, `wwwroot/audio/piano`, `wwwroot/lib/verovio`, `Playback/FurElise.musicxml`; `tests/shared` node tests run on the compiled output.
- `src/backend/omr`: the scan server: homr (AGPL-3.0, unmodified) and Basic Pitch (Apache-2.0) behind a Python HTTP service, run by the player in Docker (0011) or hosted in Azure Container Apps (0014, `Scan server` workflow, not CI). Tests: `python -m pytest` there.
- `src/ipad`: native iPad app (0019, `src/ipad/README.md`): SwiftUI in `src/ipad/App` (XcodeGen, Mac); music-core as a NativeAOT framework with the facade's C functions in `src/ipad/Core` (in the solution, so CI compiles it); Verovio from source in `src/ipad/Verovio`; Swift tests in `tests/ipad`. Swift is built and tested by the `iPad` workflow, not CI.
- `design/kit`: the redesign kit (0018); the iPad reads `tokens.css` and the fonts from it; icons as `docs/ui-conventions.md` says.
- `archive/`: **archived**, not built, tested or deployed (0032, 0033): the Blazor WebAssembly PWA in `archive/web/src/MotiffLab.Web`, its Playwright specs and web-only node tests in `archive/web/tests`, its tools and its browser-tests workflow.

## Redesign

The web Music Studio and the iPad Practice Studio are two workspaces of one product with no parity (`docs/architecture/adr/0025-two-studios.md`); while the web is archived (0032) only the iPad Practice Studio is built, and a function is not ported to or from the web. Weigh product decisions by 0025's six questions. The iPad UI is being rebuilt to 0018, replacing the old screens directly.

- Read `design/kit/HANDOFF.md` before any UI work. What the mockups invent (demo score, `phraseRanges()`, facts, counts, texts) is not data.
- Source of truth in `design/kit`, highest first: `docs/01-ux-concept.md` and `docs/02-design-system.md` (text) → `tokens.css` (values) → `ds.css` (components, score marks) → `screens/*.png` (look) → `docs/wireframes/*.png` (structure only). Text beats a picture.
- Never hard-code a colour, size, radius, shadow, font or timing: use a `tokens.css` token (a canvas reads them via `Scripts/layout/tokens.ts`). Port tokens, `ds.css` rules, icons and Verovio options, never the mockup HTML (`s*.html`).
- Non-negotiables: the score is the whole screen, nothing docked; one continuously scrolling document of A4 pages (no page jumps); the score is the selection surface (no phrase list or strip); one Insights stream (no Understand/Explore/Build tabs); any iPad, no stylus needed; one accent (cobalt = pointing and selection only; plum = what the app composed and Pro; amber = what the player marked); Russian and English for the interface and all generated content; on the device = free, cloud or AI = Pro, except sync between the workspaces, free with a quota (0025).

## Writing code

Every agent that changes code follows these rules (Andrei, 2026-11-16); `docs/guidelines.md` names the official guides
behind them and the pull request reviewer (`governance/roles/pr-reviewer.md`) checks them.

1. **Read before writing.** Read the area's code map (`docs/code-map/`), then find how the code base already does this
   (search the names, helpers and similar features) and follow it. No new helper, utility, type or file when one that
   does the same exists: reuse or extend it. Read the files you change whole and every user of what they export, and
   start `Explore` sub-agents for a survey of callers, state and tests that spans more than a few files
   (`governance/roles/common.md`, "Context and completeness").
2. **The smallest diff.** Change the existing function rather than add a second one beside it; touch only what the task
   needs (a refactoring task aside). Extend or replace, never stack: replaced code is deleted in the same pull request,
   no new layer beside one that still runs.
3. **One model per concept, one owner per state.** Selection, time, structure and playback state each have one model; a
   piece of state is written in one place, the rest read it or get an event, no second copy kept in sync by hand.
4. **The right layer and module.** Analysis, notes and rules in music-core; the apps display and route input; shared score
   and roll behaviour once, in `Scripts/score` and `Scripts/roll`. One responsibility per type and file: a change that
   adds one to a big file moves it to its own file, and the code map names it. Which module may use which: the
   `dependencies` check (`governance/quality/README.md`).
5. **A duplicate** is the same logic in two places, copied or reworded: 10 or more copied lines (jscpd), a second
   implementation of an existing rule (a tick conversion, a hit test, a selection rule, a text format), a helper that
   does what another does. Call the existing one, or move it where both can.
6. **Style:** the official guides in `docs/guidelines.md`, `.editorconfig` and the linters; within a file, its naming and
   layout.
7. **No hidden side effects.** A function that changes shared state, starts a timer or posts an event says so in its
   name or signature; the pull request lists what else reads the state it touches.
8. **A rule has a test** that fails without the change; a test is never rewritten to match a regression.
9. **Delete what is unused** in the change's area: dead code, an experiment called «убрать», a switch with no owner.
10. **A cleanup pass before the push.** Once the change works, read the whole diff again as a reviewer would
    (`git diff origin/develop...`): duplicates, dead code, broken conventions, abstractions the task does not need,
    anything added that existing code already does. Simplify, then run the checks again.
11. **The checks are the limit.** Tests and the quality checks (size, complexity, duplicates, suppressions,
    dependencies) block a merge; they are never loosened to pass.

## Commands

```bash
dotnet build MotiffLab.slnx
dotnet test MotiffLab.slnx
node --test "tests/shared/*.test.mjs"  # after a build (src/shared compiled); Node 22+
node --test "governance/tests/*.test.mjs"  # the guards
node tools/quality/quality.mjs  # code quality against the ratchet baseline (governance/quality/README.md, blocking); before every push of code
src/ipad/build-version.sh v0.1  # on Andrei's Mac: build a version from its tag and install it on the iPad
dotnet format MotiffLab.slnx  # CI: dotnet format whitespace --folder --verify-no-changes
```

Archived with the web (0032, `docs/web-archive.md`), not part of the active workflow and not working until the web is reactivated: `dotnet run --project archive/web/src/MotiffLab.Web`, `dotnet publish archive/web/src/MotiffLab.Web -c Release -o publish` and the Playwright run in `archive/web/tests/e2e`.

Warnings are errors (`Directory.Build.props`). What CI `build-test` runs and when heavy tests may run: `governance/roles/common.md`.

## Conventions

- Musical time: absolute `Tick`s and `TickSpan`s at 480 per quarter (`MotiffLab.MusicCore.Time`). Importers convert source units via `MidiTimeBase` or `MusicXmlDivisions` to exact `QuarterFraction`s, accumulate exactly, round only via `CanonicalTime` (onset and end separately). MusicXML divisions are never MIDI PPQ; no floating point for timing.
- Every event carries provenance and confidence. Anything inferred (hands, alignment, keys, chords) is marked inferred, never shown as imported fact.
- Variation operations are pure: same events, context, parameters and seed give the same events and diff. Explanations state only facts in the diff.
- In Stage 1 nothing the player imports leaves the device, except: a PDF or photo picked for scanning (0011) and a recording's sound picked for transcription (0014) go only to the scan server address the player set, and the notes returned are marked detected, never imported; after sign-in with saving online on, project files go only to the Motiff Lab API (0014); when the player asks the assistant (an experiment, Pro), the notes of the selected passage and its facts digest go only to the Motiff Lab API, which relays them to the model provider, after a one-time consent that lists exactly what is sent (0028); when the player asks the assistant in a draft (Pro), the notes of one place's base and of the variant whose techniques it takes (at most 32 bars each), with the meter, key, those techniques and the place's seams described, go the same way after their own one-time consent; never a recording's sound, a file or its name, the piece's title or other places' notes (0034). Analytics carry no note content: events are built only via `MotiffLab.MusicCore.Analytics.AnalyticsCatalogue` (counts, flags, fixed choices), are off until the player opts in, and the privacy notice lists them from the catalogue.
- UI follows `docs/ui-conventions.md`: it only displays music-core output, never changes analysis or variation code or its data shapes.
- AI interprets engine results; it never produces notes, timing, analysis or Keep checks itself: it calls music-core tools from the assistant's tool registry, music-core produces the notes as a draft, and only the player accepts a draft. music-core has no AI or Azure dependency. AI output cites fact IDs from the digest and proposes only catalogue operations with in-range parameters; else template text. Every feature works with AI off.
