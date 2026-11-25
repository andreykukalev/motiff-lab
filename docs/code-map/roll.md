# Code map: the piano roll (`src/shared/Scripts/roll`, the iPad's drawer)

**Status (2026-11-26):** live in the iPad's drawer; the web's Blazor host is archived (ADR 0032).

One roll for the web and the iPad: `piano-roll.ts` on canvas, driven by Blazor on the web and by `src/ipad/App/Web/roll.js`
(`window.roll`, messages to Swift) on the iPad. Tests: `tests/shared/roll-*.test.mjs` (node, on the compiled modules);
`roll-paint.test.mjs` (audit C6) drives `PianoRoll` over a fake page as roll.js does and compares what it draws and posts
with `roll-paint.kept.json` (`ROLL_PAINT_WRITE=1` writes it again, for a deliberate change).
What protects the page as a whole: the page scenarios `tests/contracts/page-scenarios/roll-*.json` (SEL-01, SEL-02, SEL-03,
PLAY-02, ROLL-01), replayed on `roll.html` in Chromium by CI (`node tests/pages/run.mjs`) and in the app's WKWebView
(`PageScenarioTests.swift`). The roll's selection handles are drawn on its canvas, so the scenarios check what it posts.

## Modules

- `piano-roll.ts`: `PianoRoll` (~3100 lines, baselined): the API roll.js calls, the state shown, `draw` (a `NotesScene`
  and a `NotesFrame` to the painter, then the gestures' layers and the playhead), the legend, the floating zoom, the
  editor and the input (`onPointerDown` › `roll-press.ts` › one `begin*` per gesture; `onPointerMove`, `pointerUp`,
  `cancelPresses`). Editor, legend and input router leave in W3.12 part 2.
- `roll-viewport.ts`: `RollViewport` owns the zoom, the view, glide, following and Expand's passage; `xOf`, `tickAtX`,
  `yOf`, `pitchAt`, `pitchRange`, limits, `zoom*`, `axisZoom` (OnRollZoom), `follow`, `byHand`, `bind`. Reads the roll
  through `ViewportHost`; its rules are `roll-view.ts`'s and `roll-zoom.ts`'s.
- `roll-painter.ts` (`RollPainter.paint`: grid, layers, notes, tags, Check flags with `checkPlaces`; `drawPlayheadLine`),
  `roll-keys-draw.ts` (the keys down the left, the keyboard, `keyAt`), `roll-palette.ts` (the tokens read once per theme:
  `palette`, `measures`, written only by `readPalette`).
- `roll-press.ts` (S2, the input router): `rollPressFor`, the press table: gutter (keys) › an edge by the place rule ›
  a playhead (R7) › Select by place › touch-reading pan › the old edge › strips or the loop lane › the editor › the modes
  (R8) › a note. Facts with a hit test are asked lazily, in that order. Test: `tests/shared/roll-press.test.mjs`.
- `roll-model.ts`: notes and bars from music-core's packed payload; `noteAt`, `inRange`, `grid`.
- `roll-modes.ts`: what a press begins under the bar's tool (`gestureFor`, `selectsOnRoll`, `navigatesOnRoll`), the
  Edit notes box (`notesInBox`, overlap rule), the one playhead.
- `roll-strip-gesture.ts`: the strips' hold-based selection gesture of 0.6.1 (`RollStripGesture`, `rangeBars`).
- `roll-place.ts`, `roll-place-gesture.ts`, `roll-place-draw.ts` (#601): selection by where you touch in Select. Hit
  tests (box and lasso by note centre, `pickedSpan`, move steps), the press state (`RollPlaceGesture` over the score's
  `score/selection-places.ts`: edge, inside = add, outside = new, taps, double tap = Fit, drag of picked notes = move) and
  its drawing (`HANDLE`, rectangle, lasso). v0.6.4: a drag is a span on the grid (`score/selection-grid.ts`); only the lasso
  picks notes; a span let go is held (`placed`) until Swift's selection comes back; only an edge's handle beats a playhead.
- `roll-view.ts`, `roll-zoom.ts`, `roll-playhead.ts`, `roll-head-drag.ts`, `roll-strips.ts`: view and zoom rules, playhead, lanes.

## Not reached by the iPad's roll (W0.6, measured 2026-11-16; written down, not cut)

- Members (the Blazor host's API): `PianoRoll.loadKept`, `setNamesKept`, `setChecks`, `pointAt`, `setSelection`,
  `setPhrases`, `setFilter`, `setVariation`, `clearVariation`, `setGhosts`, `setAudible`, `dispose`; `HAND_NONE`, `fitZoom`
  and `fitRow` of `roll-model.ts`; `ticksIn`; `narrowedPicks`; `RollPlaceGesture.active`. Several have node tests only.
- Branches: `roll.js` sets `setTouchReading(true)` and `setAxisZoom(true)` at load, so the `!touchReading` and
  `!axisZoom` sides are the web's (`zoomBy`, `reportZoom`, `pointerUp`, `movedByFingers`, `onDocumentPointerUp`). The
  `mode === null` (rules before the modes) and `!placeOn` sides run only before Swift's first `input`: `setRules`,
  `heldModel`, `beginRulePress`, `modelChanged`, `pressBarNumbers`, `stripHost`, `drawRoll`, `drawPlace`.

## iPad

- `Web/roll.js`: `window.roll`, the 17 host calls (`load`, `selection`, `input`, `edit`, `zoom`, `frame`, …), and the 13
  messages (`range`, `span`, `notes`, `moveBegan`, `moved`, `edit`, `seek`, `drag`, `zoom`, …) it posts from PianoRoll's
  host callbacks (15 passed on; `OnRollCheckPicked`, `OnRollNotesPicked`, `OnRollPlace`, `OnRollSelection` dropped, the
  web's). The contract: `tests/contracts/roll-page.json` (names, each argument's and message's fields, the answers),
  checked against roll.js by `tests/shared/roll-page-contract.test.mjs` and against Swift by `RollPageContractTests`.
- `Platform/RollPage.swift` (B7): the one place that builds a call (`RollCommand`, `RollPage`) and decodes a message
  (`RollEvent`, `RollRange`, `RollZoom`, `RollSpan`, `RollNotesPick`, `RollNoteEdit`); `drag` goes on to Playback's
  `PlayheadGesture`. `load` takes music-core's `roll` answer typed (`RollAnswer`, read by `PieceCore.roll`; fixture
  `tests/contracts/facade-answers/roll.json`) and passes every field of it on (`RollAnswer.argument`). `RollController.swift`: the web view, `received` → callbacks; `RollPlace.swift`, `RollTools.swift`,
  `RollDrawer.swift`: [`ipad/roll.md`](ipad/roll.md); wiring in `ScoreWorkspace.wire()`.

## Rules not to break

- With the host's place selection off (`setPlaceSelection`; always on the web, which passes no `place`), Select is as
  in 0.6.1. The switch `experimental.placeSelection` that once chose it is gone from the catalogue (v0.7).
- The roll never changes a selection by itself: it sends picks and spans; Swift's ScoreController owns the selection.
- Moving picked notes is the note editor's draft (`corrections.*`); nothing new is saved in the project file.
- In Navigate the stylus moves the roll; in Edit notes a tap on empty space adds a note; a finger beside a stylus moves it.
- With the lanes the stylus in Select takes the playhead by its handle in the strip alone and draws a frame down its line
  (`lineTakesHead`, `laneHeadAt`'s `line`, Andrei 2026-11-17, `roll-sel-on-playhead`); a finger, Navigate and the Pencil
  without the stylus mode (`placeStylus` false, «Только Apple Pencil» off) take it down the line too.
