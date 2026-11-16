# 0031 · The iPad score screen and the roll as modules: owners of state, typed ports, one-way dependencies

Status: accepted, 2026-11-16 (Andrei, 12:31 UTC). Andrei asked (thread «Разбить крупные файлы кода», 10:30 UTC) that SOLID, modularity, strict
contracts, an ADR and decomposition come before new work. Applies `CLAUDE.md` «Writing code» rules 3 and 4 to the iPad
score screen and the roll. Changes no saved data, no facade contract and no behaviour; builds on
[0019](0019-native-ipad-app.md) (the native app) and [0021](0021-excerpt-ossia-and-score-geometry.md) (one geometry module).

## Context

Measured on `develop` after v0.6.5 (d8050f4):

- `src/ipad/App/MotiffLab/ScoreController.swift`, 2,402 lines: one class holds the pen, the selection and its history,
  phrases, bookmarks, margin notes, highlights, layers, Check, corrections, the note editor, versions, sources, merging,
  Expand, resume, beats, strokes and the web page's plumbing. About 200 of its members are used from 35 files; 48 views
  hold it as `@ObservedObject`, so any of its ~45 `@Published` values re-evaluates every one of them.
- Every section reaches into the shared stored state (13–23 properties each) and calls into 3–8 other sections:
  there are no boundaries, only `// MARK` comments. A change to merging means reading the whole file.
- Three ports are used with no contract: the web page (38 `callAsyncJavaScript` calls by function-name strings into
  `window.motiff`, 41 functions; one untyped message bag decoded by a chain of `if let` in `received`), music-core
  (`MusicCore.call(piece:)` with hand-built JSON, and five private copies of a `json` helper in five files), and
  SwiftUI (published values plus a dozen callback closures set by `ScoreScreen.wire()`).
- `ScoreScreen.swift`, 1,960 lines; `src/ipad/App/Web/score.js`, 1,631 lines of hand-written JavaScript outside the quality
  checks; `Scripts/roll/piano-roll.ts`, 4,077 lines, one class `PianoRoll` with ~100 fields and ~170 methods, shared by
  the web (`Roll/PianoRoll.razor`) and the iPad (`src/ipad/App/Web/roll.js`).
- What already works and is kept: feature files hold pure value types decoded from facade answers (`CheckShelf`,
  `MarksShelf`, `VersionShelf`, `MergeInfo`, `BookmarkShelf`, `EditsLayerState`) with their views; `RollController`
  (550 lines) and `ContentsController` are separate owners; the deployment target is iOS 17.

## Decision

### 1. Layers and the direction of dependencies

```
Views (SwiftUI)                ScoreScreen, panels, sheets: display a model, send intents to it
   ↓
Feature models (@Observable)   each owns its state; no SwiftUI views, no WKWebView, no MusicCore calls of its own
   ↓
Ports (protocols)              ScorePage (the score's web page), PieceCore (music-core for the open piece)
   ↓
Adapters                       WKWebView + score.js; MusicCore.call + facade JSON
```

A layer uses only the one below it. A feature model may depend on another model only as listed in section 3, through a
read-only protocol given in its initialiser, never by reaching through `ScoreController`. `ScoreController` becomes the
composition root: it builds the ports and the models, routes page events to their models, and runs the render order
(`render()`), which stays as it is today.

### 2. Two ports with typed contracts

- **`ScorePage`** (protocol; adapter over the web view): `PageCommand`, an enum with one case per `window.motiff`
  function and a typed payload, is the only place that names a JavaScript function; `PageEvent`, an enum with one case
  per message kind, is decoded from the message in one function. The TypeScript side declares the same two lists as
  types (`Scripts/score/page-contract.ts`). The two lists are kept in one shared JSON file
  (`tests/contracts/score-page.json`), checked by a node test and by an XCTest, so a renamed function or message fails
  both suites instead of failing silently on the device; fixtures for the payloads join it as the payloads get types.
- **`PieceCore`** (protocol; adapter over `MusicCore.run` and `MusicCore.call`): `call(_ name:, _ request:) async ->
  FacadeAnswer`, with the one JSON encoding and the facade's error shape decoded once. It replaces the five `json`
  copies. A fake of each port lets a model be tested without a web view or the framework.

### 3. Feature models and what each owns

| Model | Owns (moved out of `ScoreController`) | May read |
| --- | --- | --- |
| `PenModel` | pen, penOn, tool, ink history and undo/redo, rubbing, the ink queue, live style | — |
| `SelectionModel` | selection, its history, toast, hold hint, menu place, phrases shown, level, hidden phrases | — |
| `MarksModel` | marks, margin notes and the draft, highlights, layers hidden, piece overrides, bookmarks shown | Selection |
| `CheckModel` | Check shelf and drawer, corrections, «Мои правки», the roll's note editor | Selection |
| `SourcesModel` | versions, version status and page, sources, the engraved view, version playback | — |
| `MergeModel` | the merge draft, bar, view, hearing, comparing, toast, marker pick | Selection, Sources |
| `FocusModel` | Expand (entry, roll, card), play range, practice view, focus version, beats, resume | Selection, Sources |

What stays in `ScoreController`: the piece, the pages in view, the playhead away, the problem and notice lines, the
rendered flag and the render order. A change of the notes (`notesDidChange`) is one event the root sends to the models
that redraw on it, instead of each section calling the others.

Views observe the model they show (`SelectionMenu` the selection, `MergeBar` the merge), so a change re-evaluates only
the views that read it (`@Observable` tracks each property), and a panel's dependencies are visible in its initialiser.

### 4. The screen, the page and the roll

- `ScoreScreen` keeps the layout of the layers; its wiring moves to one file per concern beside it (`ScoreScreen+Roll`,
  `+Sources`, `+Expand`, `+Playback`), each the only place that connects its models to the player and the roll.
- `src/ipad/App/Web/score.js` becomes TypeScript modules in `Scripts/score/ipad-page/` behind `page-contract.ts`, compiled
  and checked like the rest; `score.js` stays only as the entry the app loads. The `dependencies` check keeps what it
  loads inside the bundled folders.
- `RollController` gets the same kind of typed port for `roll.js`. `PianoRoll` keeps its public methods, the contract
  of both apps, as an interface `RollApi`; its internals move into collaborators by state (viewport and zoom, painter,
  note editor, legend, input routing) beside the `roll-*` modules that already exist.

### 5. Rules from now on

- No new member on `ScoreController` except to compose: new behaviour goes to the model that owns its state.
- A model, a port adapter and a view each stay within the quality thresholds (600 lines a file, 80 a function).
- The code maps (`docs/code-map/ipad.md`, `score.md`, `roll.md`) name each model, what it owns and its port.

## How it is done

One pull request a step, in this order; each keeps the behaviour, keeps `MotiffLabTests` green (expectations change
only in type names), runs the iPad workflow (label `ipad-tests`) before the merge, removes the baseline entries of what
it moved, updates the code map and gets the pull request reviewer.

1. a) The one JSON encoding of a facade request; the five copies go. b) `PieceCore`.
2. `ScorePage`: `PageCommand` and `PageEvent` in Swift, the root routes events; fixtures and tests on both sides.
3. – 9. One model a step: Pen, Selection, Marks, Check, Sources, Merge, Focus. Each moves its state and methods out of
   `ScoreController`, gets tests against fake ports, and switches the views that show it.
10. `ScoreScreen` wiring by concern.
11. `score.js` to TypeScript behind the contract.
12. The roll's port; then `PianoRoll`'s collaborators, one a step, the web's roll tests and browser specs (label
    `browser-tests`) on each, because the web runs the same module.

The web's `PlaybackLab` and `app.css` are not in this record (platform focus is the iPad).

## Consequences

- An agent changing merging reads `MergeModel`, its two ports and the views that show it: a few hundred lines
  instead of 2,400; a test can pin a rule of a model without the web view.
- Risks: SwiftUI update timing changes with `@Observable` (checked on the device at the next version); hidden order
  between sections (kept by leaving `render()` in the root); no Swift compiler in a cloud thread, so each step waits for
  the macOS workflow.
- Undo: each step is one revert. Renaming `ScoreController` to say what it now is, is left for the end.
