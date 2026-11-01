# 0020 · The music-core facade: one JSON contract for the web app and the iPad app

Status: proposed, 2026-11-01. Andrei asked for one stable API with JSON in and out, shared by the web app and the native iPad app, with the project file as the contract between them (redesign preparation, item 21). Number 0019 (the native iPad app) is reserved for a record still to be written; the redesign is [0018](0018-redesign.md).

## Context

The web app calls music-core directly from Razor code: `PlaybackLab.razor` alone imports a piece, suggests phrases, selects a passage, analyses it, offers experiments, drives a variation chain, generates notation and playback scores and saves the project, with the orchestration spread over two thousand lines of UI code. The spike `spikes/ios-core` (PR #176) showed that the same C# music-core runs natively on the iPad through NativeAOT, and that Verovio engraves its MEI there in about 50 ms. The redesigned iPad app is native Swift ([[0015]] is superseded by the plan in item 25), so Swift needs a way to call music-core, and the redesigned web app needs the same orchestration without copying it from the old page.

Options weighed:

- **Rewrite music-core in Swift.** Two engines to keep equal, tested twice; the analysis, catalogue and project file are the product. Rejected by Andrei.
- **Expose the C# types to Swift one by one.** NativeAOT exports only C functions with plain arguments; every record would need a hand-written marshalling layer, and every change in music-core would change the C surface.
- **A facade with JSON in and out (chosen).** The C surface stays at five functions. The contract is the list of calls and the shape of their JSON, versioned; music-core can change behind it. The web app (Blazor, C#) calls the same facade class, so the two clients exercise one orchestration and one set of tests.

## Decision

1. **`MotiffLab.MusicCore.Facade.MusicCoreFacade`** is the one entry point for an app. It keeps the pieces an app has open in memory, numbered from 1, and answers calls by name with JSON text. It is the only stateful part of music-core: everything under it stays pure, and the facade itself is deterministic (the same calls in the same order give the same answers).
   - `Open(fileName, bytes, language)` imports a MIDI or MusicXML file as a new project, or reads a project file (`.motiff.json`), and returns the piece's number with its summary. A rejected file answers with the importer's reason and tip.
   - `Call(piece, name, request)` runs one call on an open piece; `piece` 0 is for the stateless calls (`version`, `catalogue`, `settings`, `settings.state`). `request` is a JSON object or null.
   - `Close(piece)` forgets it.
   - Every answer is one JSON object. A failure is `{"error": code, "message": text}` and leaves the piece as it was. Codes: `import`, `noPiece`, `unknownCall`, `badRequest`, `noSelection`, `noVariation`, `unavailable`, `internal`.
2. **The contract is versioned.** `version` returns `{"contract": 2, "projectFile": 12, "runtime": …}`. A field is added without changing the contract number; a field removed or renamed, or a call's meaning changed, raises it. The project file version ([[ProjectFile.Version]], 12, described in [docs/project-file.md](../project-file.md)) is part of the contract: the file an app saves is what the other app opens and what the API stores (0014). Contract 2 (2026-11-01, redesign package 2) replaced `sessions` and `observations` in the project summary with `resumePoints`, `ideas`, `marks`, `bookmarks` and `openChecks`, and added the calls below the line in the table. Planned additions (phrase levels, Piece DNA) are already visible as placeholders so that a client can code for them: `phrases` names the levels it knows and which are available; `dna` answers `{"status": "notYet"}` until Piece DNA exists.
3. **JSON is written and read by hand** (`Utf8JsonWriter`, `JsonDocument`), as the project file is, so the output is fixed and NativeAOT needs no reflection. Ticks are integers at 480 per quarter ([[0002]]); enums are lower camel case names; what music-core inferred carries its provenance and confidence as the events do ([[0004]]). The parameters of a variation step use the project file's shape, so a step can be copied between a saved project and a request unchanged.
4. **Words follow the piece.** `Open` takes a language tag (`en`, `ru`); `language` changes it. Every text in an answer (facts, phrase descriptions, operation names, explanations) comes from music-core's `Words`, never from the client.
5. **The web app moves onto the facade in the redesign**, not before: the current page keeps its direct calls until it is replaced. The iPad app calls the facade through five C functions exported by the native build (`ml_version`, `ml_open`, `ml_call`, `ml_close`, `ml_free`), each returning UTF-8 JSON the caller frees. The spike's benchmark exports stay only in the spike.

## The calls

Request fields are optional unless marked; a tick span is `{"onset", "end"}`; a note is `{"id", "pitch", "onset", "duration", "velocity", "track", "voice"?, "hand"?: "left" | "right", "handInferred"?, "provenance"}`.

| Call | Request | Answer |
|---|---|---|
| `version` (piece 0) | – | `contract`, `projectFile`, `runtime` |
| `catalogue` (piece 0) | `language` | `keep.properties` (name, label), `keep.presets` (id, label, keep, operations), `operations` (id, name, whatItChanges, compatible keep, footnotes, scopes) |
| open | file name, bytes, `language` | `piece` (number), `project` (id, fileName, createdAt, lastOpened, counts, origin: `imported` / `scanned` / `transcribed`), `summary` (format, tracks, notes, bars, end, keys, report) |
| `piece` | – | the summary with `notes`, `tracks`, `bars` (number, span, signature, pickup), `keys`, `startChoices`, `report.issues` |
| `language` | `language` (required) | `language` |
| `phrases` | – | `levels` (`phrase` available; `sentence`, `period` not yet), `evidence`, `edits`, `phrases`: number, level, firstBar, lastBar, span, `selectable` span, `fitsSelection`, `start` (at, evidence, provenance, confidence, description) |
| `phrases.edit` | `makePhrase` span, or `joinWithNext` number, or `reset` | as `phrases` |
| `select` | `bars` {first, last} or `notes` [ids] or `span`; `filter` {`hand` / `track` / `voice`} | `selection`: span, firstBar, lastBar, filter, noteIds, handInferred, key |
| `understand` | – | `motif` (line note ids, intervals, contour, key reading, facts), `structure` (recurrences, phrases, newMaterialBars, facts), `character` facts; a fact is id, lens, certainty, text, basis, places |
| `experiments` | – | `experiments`: id, topic, title, change, question, listeningFocus, factIds, passage, filter, keepPreset, alternatives (label, keeps, steps) |
| `experiments.start` | `id` (required), `alternative` | the variation state, with `experiment` |
| `experiments.surprise` | `seed`, `tried` [ids] | the variation state with `experiment`, or `{"none": true}` |
| `variation` | – | the variation state |
| `variation.start` | `keep` [properties], `seed` | the variation state |
| `variation.propose` | `operation` (required), `parameters` (the step's shape in the project file; `E7` with `kind`, `noteId`, `values` edits a note by hand) | the variation state |
| `variation.check` | `operation`, `parameters` | `reason` (null when the step can run) |
| `variation.accept`, `.reject`, `.undo`, `.redo` | – | the variation state |
| `variation.clear` | – | `{"cleared": true}` |
| `notation` | `of`: `piece` (default) / `variation`; `title`; `readable` for transcribed notes | `mei`, `staves`, note counts, `writtenAsHeard` |
| `playback` | `of`: `piece` (default) / `heard` / `inPlace` | `notes`, `pedal`, `tempos`, `beats`, `bars`, `end` |
| `project.save` | `now` (ISO time) | the project file itself (`format`, `version`, …), with the selection as the working motif and the chain as its variation, as the web app autosaves |
| `dna` | – | `{"status": "notYet"}` |
| `marks` | – | `ink` (strokes in the pencil layer's shape), `notes` (id, span, bars, text, createdAt), `highlights` (id, span, bars, noteIds, colour), `fingering` (noteId, finger) |
| `ink.add` / `ink.remove` / `ink.clear` | `strokes` [stroke] / `ids` [ids] / – | as `marks` |
| `note.add` / `note.change` / `note.remove` | a place (`span` {onset, end}, `bars` {first, last} or `phrase` number), `text`, `now` / `id`, `text` / `id` | as `marks` |
| `highlight.add` / `highlight.remove` | a place, `notes` [ids], `colour` (default amber) / `id` | as `marks` |
| `fingering.set` / `fingering.remove` | `noteId`, `finger` 1–5 / `noteId` | as `marks` |
| `bookmarks` | – | `categories` (id, name in the language, ink, builtIn, bookmarks count), `bookmarks` in the player's order (id, title, category, span, bars, fromPhrase, ideas count, createdAt) |
| `bookmark.add` / `.change` / `.move` / `.remove` | a place, `title` (default "bars 1–8"), `category` (default practise) / `id` and any of title, category, place / `id`, `index` / `id` | as `bookmarks` |
| `category.add` / `.rename` / `.remove` | `name`, `ink` / `id`, `name` / `id` (own categories only; their bookmarks move to practise) | as `bookmarks` |
| `resume` | – | `resumePoints`, most recent first: id, name, at, bar, passage (span, bars, filter), focused, overlays, tempoPercent, hands (`both` / `left` / `right`), loop, createdAt, lastActive |
| `resume.save` | `id` (an existing point; none starts a new one), `name`, `at`, `passage` {span, filter}, `focused`, `overlays`, `tempoPercent` 25–150, `hands`, `loop`, `now`; fields not sent keep their values | `saved` (the point), `resumePoints` |
| `resume.rename` / `resume.remove` | `id`, `name` / `id` | as `resume` |
| `ideas` | – | `ideas`: id, name, note, factId, passage, experiment, hasVariation, createdAt |
| `idea.keep` | `name`, `note`, `factId`, `now`; keeps the variation heard now with its experiment, the fact defaulting to the experiment's first | as `ideas` |
| `idea.rename` / `idea.note` / `idea.remove` | `id`, `name` / `id`, `note` / `id` | as `ideas` |
| `idea.open` | `id` | `selection`, `hasVariation`, `variation`: the idea's passage selected and its chain restored |
| `settings` (piece 0) | – | `settings`: id, section, kind (switch / choice), choices, default, scope (device / account / piece), presets (learning, practising, clean) |
| `settings.state` (piece 0) | `values` as stored, then `preset` or `set` {id, value}, then a piece's `overrides` | `preset` (learning / practising / clean / custom), `values` |
| `settings` (on a piece) / `settings.override` | – / `id`, `value` (null clears; piece-scope settings only) | the catalogue with `overrides` / `overrides` |
| `check` / `check.resolve` | – / `bar`, `resolved` (default true) | `flags` (bar, source, reason, resolved, text in the language), `open` count |

The variation state: `keep`, `steps` (operation, parameters, explanation), `pending`, `undone` count, `canUndo`, `canRedo`, `acceptDisabledReason`, `editDisabledReason`, `current` notes, `heard` notes, `result` (operation, notes, changes before/after, added, removed, checks, properties, flags, explanation), `operations` (id, name, disabledReason under the current Keep).

Calls kept for the next contract version: note corrections (S1-I7), MIDI export, the Keep table's parameter choices (today a UI list), and the fact digest for AI (0003).

## Consequences

- One orchestration, tested once: `tests/MotiffLab.MusicCore.Tests/Facade` asserts the JSON of every call on the test pieces; a Swift test in the spike checks the same answers through the C functions in the simulator.
- A request costs a JSON round trip. On the iPad it is tens of microseconds against tens of milliseconds of analysis; in Blazor WebAssembly it is slower, which is one more reason the web app moves only with the redesign, where it can measure.
- The facade holds state, so an app must call it from one thread at a time per piece (the spike runs music-core on its own 8 MB thread).
- Changing a music-core type no longer changes the clients until the contract changes; the record of what changed lives in the contract version's comment, as it does for the project file.
