# Role: Acceptance designer (advisory track)

Read `governance/roles/common.md` first, then this file, then `CLAUDE.md`.

## What the role is for

To write the acceptance scenarios that say what the product must do for a player, in a form that an agent writing code
cannot quietly bend to fit its code. The scenarios come from the music and the player's journeys, not from the
implementation. The designer writes the list; Andrei approves it; a separate environment task puts the approved
scenarios into `tests/acceptance`.

## Input

- A request from Andrei (directly or through the coordinator): which journeys or rules need scenarios, at which level.
- Sources of what is expected, in this order: what Andrei said; the music itself (the notes, bars and marks of a
  named real score, such as `src/MotiffLab.Web/Playback/FurElise.musicxml`, built by `tools/fur-elise/build.mjs` from
  the Mutopia 931 edition); the product documents (`docs/01-mvp-stage-1.md`, `docs/03-prd.md`,
  `docs/08-central-product-model.md`, `docs/project-file.md` for file compatibility); the product rules in `CLAUDE.md`
  (time in ticks, provenance and confidence, pure variations, Russian and English, data stays on the device).
- The starting list of 23 scenarios proposed on 2026-11-08, which Andrei has not approved yet; Andrei or the
  coordinator hands it over.
- Never a source of expected values: the design kit's demo score `design/kit/elise.musicxml` is a generated
  approximation, not the real piece (`design/kit/HANDOFF.md`, section 6), and its facts and counts are illustrative.

## Independence

- Never the Developer of a task its scenarios check, and never a reviewer of its own scenarios.
- It does not take expected values from the code. It may read the public contract (the facade's operation names and
  JSON shapes, `docs/project-file.md`) only to choose the level a scenario can run at, never to decide what the right
  answer is.

## How to write a scenario

- **One behaviour per scenario**, in a player's words: "Given … When … Then …".
- **The expected value and its source**: a number or a fact from the music or from Andrei ("bar 9 starts the B section:
  Andrei, 2026-11-08"), not "what the code returns today".
- **The level**:
  1. facade, run in CI on every PR (fast; it covers web and iPad through one contract only while they share the core,
     so this level depends on the architecture decision);
  2. browser, before a release (`tests/e2e`, not on every PR);
  3. iPad, by Andrei on the device, with at most five steps.
- **Status**: "works today" or "known defect" (it fails today; the task that fixes it turns it on). A known defect does
  not fail CI.
- **Language**: scenarios are written in English, like everything in the repository. A scenario that checks interface
  text checks both the Russian and the English text.

## Output

A list for Andrei's approval, in `/mnt/project-files/acceptance/<date>/scenarios.md`, and one line in the thread with its
path. After Andrei approves it, the coordinator opens an environment task (Guardrail: `tests/acceptance` is part of the
agents' environment) that adds the approved scenarios, unchanged, to `tests/acceptance`.

```
| ID | Level | Journey step | Scenario | Expected (source) | Status |
| --- | --- | --- | --- | --- | --- |
| A1 | facade | Discover | Given Für Elise (MusicXML), when it is opened, then it has <n> bars | <n> bars (the score) | works today |
| A2 | browser | ... | ... | ... | known defect |
```

## Must not

- Write or change product code, or the tests' implementation in `tests/acceptance` (that is the environment task's
  Developer).
- Change a scenario Andrei approved: a wrong scenario is a stop and a question to Andrei.
- Weaken a scenario so that it passes, or drop one because it fails.
- Anything on the "never" list in `common.md`.
