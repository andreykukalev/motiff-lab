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
it moved, updates the code map and gets the pull request reviewer. *Amended in version 0.7* (#642, 2026-11-16): the label
is retired; the iPad workflow runs the Swift tests on every push to such a pull request, and its `ipad-gate` is required
on `develop`.

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

## Note, 2026-11-17 (step B5)

`PieceCore` was built as a struct over `MusicCore.run` and `MusicCore.call` (`Platform/PieceCore.swift`), not as a protocol:
it has one implementation, and the features' tests call the facade itself. Its answers are typed (`Platform/PieceAnswers.swift`)
and pinned by `tests/contracts/facade-answers`. A protocol, with a fake for a model's tests, comes with the first model
(steps 3–9) that is tested without the framework. The decision above is unchanged.

## Note, 2026-11-18 (step S1, `SelectionModel`)

`SelectionModel` (`Selection/SelectionModel.swift`) owns the selection's state: the selection, its history, the toast,
the hold hint, the menu's place, the phrases shown and whether they are hidden. Its state is set only through its own
methods; `ScoreController` hands in what music-core and the page answer (`answered`, `show`, `placeMenu`).

Two things stay in the root for now, and each has a step that removes it:

- **Selecting** (`select`, `clearSelection`, `showLevel`, `resetLevel`): each runs, after music-core's answer, the page,
  the play range and Expand in one order. It moves into `SelectionModel` in **S3**, once `FocusModel` owns Expand and
  the play range and the root only connects the two.
- **The relay and the forwarders**: `ScoreController` sends each `objectWillChange` of the model on as its own and
  forwards `selection`, `phrases` and `phrasesHidden` read-only, so the views and reactions that observe the controller
  update at the same moment as before and the tests (the protected `tests/ipad/Baseline` among them) read the same
  names. This is against section 3's «views observe the model they show», for one step. They go in **S7** (blueprint
  phase C: `ScoreScreen`'s view types into their files, this record's step 10), which moves each view onto the model it
  shows; the readers in other features (Marks, Roll, Playback, Assistant) and the tests then read `selectionModel`.

## Note, 2026-11-18 (step S3, `FocusModel`)

`FocusModel` (`Playback/FocusModel.swift`) owns Expand's state but one: where it was opened from (`entry`), whether the
card shows the roll (`asRoll`), the card's options, the version in focus and the play range, with the rules that need
only the selection (the play range, the unit the card is, `canStepBack`/`canStepOn`, the card's position text). It reads
`SelectionModel`. `SelectionModel` now selects and changes the level through music-core itself (`select`, `showLevel`,
`resetLevel`, `loadPhrases`, and `hidePhrases` as the writer of `phrasesHidden`); `show` is gone. The root keeps the
order after each answer: the page, then the play range, then the card.

What stays in `ScoreController`, and the step that removes each:

- **`focused`**: the protected `tests/ipad/Baseline/WorkspaceSession.swift` observes `score.$focused`, a publisher a
  forwarder cannot give. It moves into `FocusModel` with the relay and the forwarders in **S7**, whose change to the
  baseline's harness Andrei merges.
- **`clearSelection`**: it leaves Expand between the step recorded and the selection taken away, so it calls both models;
  it is the root's connecting of the two, as `enterFocus`, `leaveFocus` and `step` are, which open the card on the page.
- **`practice`** (the protected baseline writes `score.practice`) and **`ossia`** (its change engraves the card again,
  which needs the page and music-core): with the root's page calls, in **S7** and step 9 (the composition root).
- The forwarders `focusRoll`, `playRange` and `focusVersion` join S1's, under the same S7 removal.

## Note, 2026-11-18 (step S4, `SourcesModel`)

`SourcesModel` (`Sources/SourcesModel.swift`) owns the other versions (`versions`), what the last thing done with one
came to (`status`), the version whose page Sources shows (`page`) and the score as last engraved (`engraved`: the draft's
view, even bars, the notes it colours). It reads a version and the list through music-core itself (`readVersion`,
`readList`) and has the words on a version just added (`addedStatus`); the root writes the rest through `read`, `say`,
`showPage` and `keepEngraved`, and forwards `versions`, `versionStatus` and `versionPage` read-only under S1's relay.
`MergeModel` follows in its own pull request, after rule tests for the draft (the blueprint's S4 row).

What stays in `ScoreController`, and the step that removes each:

- **`sources`** (`SourcesInfo`): the protected `tests/ipad/Baseline/WorkspaceSession.swift` observes `score.$sources`,
  as it does `score.$focused`. It moves in **S7**, with the harness change Andrei merges.
- **The version's notes to hear** (`versionPlayback`, `versionNotes`): they are `PlaybackNote`s, a Playback type, and
  the folder table (`governance/quality/README.md`) lets Sources use only Platform and Selection. They move to
  `FocusModel` in **S4b**: `versionNotes` names only `PieceCore` (Platform) and Playback's own types, and the root hands
  in the version's id as a string (`versions.shownId`) and asks it to forget them from `notesDidChange`, so no folder
  edge and no change of the table is needed (the lead, 2026-11-18).
- **Drawing the versions on the page and engraving** (`drawVersions`, `engraveScore`, `applyMergeView`): the page's,
  with the root's page calls in step 9.

`engraved` stays in `SourcesModel` in S4b, as section 3's table gives it: it records how the page was engraved (even
bars, the view, the notes coloured), which is not the draft's. `MergeModel` owns the view the draft asks for
(`mergeShow`), and the root keeps comparing the two (`wantedView`) until step 9.

## Note, 2026-11-18 (step S4b, `MergeModel`)

`MergeModel` (`Sources/MergeModel.swift`, reading `SelectionModel` and `SourcesModel`) owns the merge draft (`draft`),
the change it shows (`at`), Bar | Phrase (`phrase`), the view the draft asks for (`view`), what was last heard
(`hearing`), Compare versions (`comparing`), the toast (`toast`) and the marker tapped (`markerPick`), with the rules
that need only those two models: the bars a choice goes for (`choiceBars`), the span heard (`hearSpan`), the change
‹ › go to (`stepped`), the bars Compare versions shows (`seenBars`) and one `performance.merge` call (`call`, music-core's
refusal back for the root to put in the version's words). `MergeDraftTests` were written to pin the draft's
rules through `ScoreController`'s calls before the move, but first ran after it (Swift builds only in the iPad
workflow), and passed there unchanged. `FocusModel` keeps the version's notes to hear
(`versionNotes(of:in:)`, `forgetVersionNotes`), as the S4 note says.

`ScoreController` forwards the draft's state read-only, but for `mergePhrase` and `comparing`, which the views bind
(`$score.comparing`, Bar | Phrase): their forwarders also set, through `scope(phrase:)` and `compare(_:)`. All of them go
in **S7** with S1's. Hearing the draft (`hearMerge`: its notes are Playback's), engraving its view and the card for
Compare versions, and what follows a merge applied or undone (the notes, Check, the version's words) stay the root's,
with its page calls, until step 9.

## Note, 2026-11-18 (step S7a, the sources and merge views)

S7 goes in three pull requests, the protected harness change in the smallest. In **S7a** the views that show the other
versions or the merge draft observe `SourcesModel` and `MergeModel` themselves (`ScoreScreenContent`, `VersionPage`,
`DraftBlock`, `BarStrip`, `MergeScope`, `MergeRow`, `MergeListen`, `MergeShowSwitch`, `MarkerPopover`,
`CompareVersionsScreen`), Bar | Phrase and Compare versions bind to `scope(phrase:)` and `compare(_:)`, and the root's
relay of the two models and its forwarders of their state are deleted. The keyboard's ⌘Z of the selection's toast is
left out while a draft is open by the screen, which observes `MergeModel` (`SelectionToastView`'s `merging`), as
Selection may not name a Sources type. `LayersList` (Marks) reads `pieceExtra`, which asks whether there are versions;
it stays current because every change of the list of versions writes the root's published state in the same task
(`drawVersions` through `sources` and the focus relay, `loadCheck`), not because it is drawn when it opens. **S7b** does the same for
`SelectionModel` and `FocusModel` but for what the protected baseline reads; **S7c** moves `focused` and `sources` out
of the root, with the change of `tests/ipad/Baseline` that Andrei merges.

## Note, 2026-11-18 (step S7b, the selection and focus views)

The views that show the selection, the phrases or Expand observe `SelectionModel` and `FocusModel` themselves
(`ScoreScreenContent` and its reactions, `ScoreBar`, `Pill`, `SelectionMenu`, `FocusBar`, `FocusShowAs`, `PhrasesPanel`,
`SelectionOptionsRow`, `SelectionToastView`, `RollDrawer`, `InsightsSheet`, `CheckDrawer`, `HighlightRow`,
`ContentsList`, `PagesStrip`, `AssistantHereCard`); a host that only hands a model on holds it as a plain `let`
(`ModeOptions`, `ContentsDrawer`). The root's relay of the two models and its forwarders `phrases` and `phrasesHidden`
are deleted, and the root, the workspace and the other tests read the models. `selection`, `focusRoll`, `playRange` and
`focusVersion` stay in `ScoreController` only for the protected harness (it polls them, so it needs no relay); they go
in **S7c**, with the harness's reading lines Andrei approved (2026-11-18 16:55Z). With the focus relay gone, and
`sources` leaving the root in S7c, the publishes that kept `LayersList` current go: «has versions» now comes to it as a
plain value from the screen, which observes `SourcesModel` (`ScoreBar`, `MorePanel`, `ModeOptions`, `PenPalette`), and
`pieceExtra(hasVersions:)` takes it.

## Note, 2026-11-18 (step S7c, `focused` and `sources` out of the root)

Whether Expand is open (`focused`) is `FocusModel`'s, set by `open(from:)` and `close()` in the order the root kept
(the way back closes it before the version goes); the piece's sources (`SourcesInfo`) are `SourcesModel`'s `info`, read
through `read(sources:)` and emptied with the rest of the last piece in `open(evenBars:)`. The forwarders `selection`,
`focusRoll`, `playRange` and `focusVersion` are deleted. The protected harness reads the models: its reading lines
(`WorkspaceSession.swift` 172, 175, 182, 184, 189, 192, 207 and 213, `WorkspaceScenarioTests.swift` 202, which #690
moved from 200) change as Andrei approved on 2026-11-18 at 16:55Z, and no assertion or kept transcript changes. A view
in a folder that may not name `FocusModel` gets Expand as a plain value (`SelectionToastView`'s `focused`); the
practice button and ✕ observe the model. `ScoreController` keeps no relay and no forwarder of a model.

## Note (S6, v0.10)

`PenModel` (`Ink/PenModel.swift`) owns the score's pen: the pen, `penOn`, the pencil palette, the Pen's history with
Undo and Redo, the eraser's points, the ink queue and the live style; it reaches the page, the piece and the marks'
change through `PenScore`, which the root adopts. `MarksModel` (`Marks/MarksModel.swift`, reading `SelectionModel`)
owns the marks and their count, the note being written, the layers hidden, the piece's own settings, the bookmarks as
drawn and the one gone to, and the lists of layers. The views observe the two models. Who writes and scrolls
(`InkInput`, the input rules), where a point is on the page, what follows turning the pen (`penTurned`), changing a note
or a highlight, going to a bookmark or a note and what the page hides stay in the root, with its page calls, until step
9. The risk under Consequences holds: SwiftUI's update timing of the views that now observe these models is checked on
the device at the next version.

## Note (S6, `CheckModel`, v1.6 i-8)

`CheckModel` (`Marks/CheckModel.swift`, reading `SelectionModel`) owns Check: the shelf as music-core answered the last
`check` call, whether the drawer is open, what this iPad kept of the working notes' scan (variants and crops) and the
variant chosen last, the bar listed to correct (Fix notes), «Мои правки» and the roll's note editor, with `editsKept`.
It calls music-core itself and reaches the root through `CheckScore`, as `PenModel` does through `PenScore`: the bar
selected and shown (`showSpan`), the flags drawn (`drawChecks`, which stays in the root because the pages of a scan no
engine read are `Workspace`'s), what the page hides (`applyLayers`), the notes changed (`notesDidChange`) and the
screen's two lines (`showNotice`, `showProblem`). Whether scans go as jobs (`ScanServer.jobs`, Import's) is handed in;
the assistant's draft comes as its request and first bar, so the model names no Assistant type. The views observe the
model (`CheckDrawer`, `EditsLayerSection`, `LayersList`, `MorePanel`, `UnreadPagesCheck`); Roll and Import get what they
show as plain values (`RollDrawer.noteEditor`, `PhotoPagesCheck.pages`), so `NoteEditorState` and `CheckPageEntry` moved
to Platform and five folder edges went. `ScoreController` keeps no forwarder: the protected `tests/ipad/Baseline`
(`ScanCheckBaselineTests`, `WorkspaceSession`'s reactions) reads Check by its old names through a test-side extension
(`MotiffLabTests/CheckBaselineNames.swift`), which goes with the baseline change Andrei merges.

## Note (S7 part 1, the score's screen by concern, v1.6 i-9)

`ScoreScreen.swift` keeps `ScoreScreen`, the state only `ScoreScreenContent` keeps, `body`, `page` and `layers` (the
chain of the layers, each layer's content a function beside it). Section 4's files are extensions of
`ScoreScreenContent`, by the app's naming without `+`: `ScoreScreenBars` (the bar and what hangs under it, the transport),
`ScoreScreenPanels` (the sheet over the right, the Contents), `ScoreScreenRoll` (the roll's drawer), `ScoreScreenSources`
(the switch of views, Sources, the «В черновик» sheet) and `ScoreScreenReactions` (the covers and the reactions, in the
order the code map pins; Expand and playback have no wiring of their own left in the view since `ScoreWorkspace`). The
view's state they share is internal, no longer private. The view types are in their own files; the bar (`ScoreBar`) and
the pill (`Pill`) are handed what they show and the actions they call, so the screen can place them elsewhere without
touching what they do: the pill names no controller, and the bar holds `ScoreController` only to hand it to the modes,
Practice, the layers list and More (since i-10 not even that). `ScoreController` itself is step i-10.

## Note (S7 part 2, the score's controller by concern, v1.6 i-10)

`SourcesModel` draws the versions on the page and acts on them: showing and comparing one, adding and renaming one, the
sources read again, a version's bar shown, a marker's «Как в версии» and «Источники». It reaches the root through
`SourcesScore`, as `CheckModel` does through `CheckScore`: the page, the piece, whether the pages are drawn, whether
extra information of a kind shows, a draft's labels drawn with the markers, the bar shown (`showSpan`), and what
follows (`versionsDrawn`: the version in focus and the page engraved again on even bars; `loadCheck`; `hear(version:)`,
which needs the focus's version notes; `openSources(atBar:)`, the root's `versionPicked`). The Sources views observe
the model (`MarkerPopover`, `SourcesSheet.versions`), so two folder edges to Workspace went. What stays the root's is
split by concern into extension files beside it, each with one responsibility (`ScoreSelecting`, `ScoreMarks`,
`ScoreFocus`, `ScoreInput`, `ScorePageLayout`, `ScorePlayheads`, `ScoreVersions`, resume in `ScoreScreenViews`), and
`BundleSchemeHandler` moved to Platform (the roll's edge to Workspace went): `ScoreController.swift` is 576 lines and its
two quality entries went. The bar holds no controller: the screen makes Practice and the layers list, and the modes get
the mode as a binding. The protected baseline calls `addVersion` on the score through a test-side extension
(`MotiffLabTests/SourcesBaselineNames.swift`), as `CheckBaselineNames.swift` does. Still in the root, for the next step:
Expand's calls into `FocusModel` and the marks' calls into `MarksModel`, each through its own protocol.

## Note (S7 part 3, Expand and the marks through their own protocols, v1.6 i-10b)

`FocusModel` opens and leaves Expand on the page (`enter`, `expandRoll`, `leave`), goes to the previous or the next
phrase (`step`), engraves the card at the page's options with the variation tried as its ossia (`engraveCard`,
`showOssia`; the root's `ossia` went) and keeps the version in focus (`updateVersion`), through `FocusScore`: the page,
the piece, whether the pages are drawn and the Pen is on, the version shown and its differing bars (as values, so
`VersionEntry` moved to Platform), the page held still under the veil (`holdPage`), a passage selected
(`selectPassage`) and a tick revealed, and the problem line. `MarksModel` draws and changes the marks (`draw`,
`changeMarks`), draws the bookmarks and goes to one, opens, keeps and deletes a margin note, shows the layers and the
phrases' marks, follows the device's settings and clears the piece's own, and sends what the page hides
(`applyLayers`), through `MarksScore`: the page, the piece, the pages drawn, the Pen on, Check's drawer open and the
slate appearance (Check's and the device's, as values), a passage selected and revealed, Expand opened (`enterFocus`, the
root connecting the two models), the versions drawn again, and the problem line. The model's former state-only
methods (`open`, `close`, `takeCardOptions`, `showVersion`, `show`, `keepBookmarks`, `wentTo`) were folded into these
or made private, so each state has one writer. The views call the models (`FocusBar`, `SelectionMenu`, `NoteCard`,
`LayersList`, `NotesList`, `ContentsList`, the pill's ‹ ›, the roll's Expand). What stays the root's is its
composition: its state, the page's events routed to the models, the render order, engraving the score, and one line
per protocol member a model may not name; `applyLayers` and `changeMarks` stay on the root only as `CheckScore`'s and
`PenScore`'s. The protected baseline drives Expand and the phrases' marks by their old names through test-side
extensions (`MotiffLabTests/FocusBaselineNames.swift`, `MarksBaselineNames.swift`), as `CheckBaselineNames.swift` does.

## Note (W2.12, the PDF on its model and views, v1.6 i-11)

`Pdf.swift` is cut by the pattern of this record: `PdfController` (the PDF piece's model over music-core's `document`
calls and PDFKit, `Pdf/PdfController.swift`, with `keepView`, which sat in the score's `ScoreScreenViews`), the pages'
view (`PdfPages` beside `BarPdfView` in `PdfPagesView.swift`), More (`PdfMore.swift`), and the screen. The screen
composes the PDF's model with the pen, the metronome, the Reference, Sources, the scan server and the account, as
`ScoreScreen` composes the score's, so it moved to `Workspace/` (`PdfScreen.swift`, with `PdfScreenBar`,
`PdfScreenReading`, `PdfScreenSources` as section 4's extensions), and the 27 folder edges `Pdf/` had for it went;
the model keeps the nine edges of the pen's state to `Ink/`. «Пьесы в этом файле»'s model (`PieceSplitter.swift`) is
made from values and answers the pieces made, so it names no `PdfController`; the sheet writes the PDF's family.
