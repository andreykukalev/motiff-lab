# Code map: the piano roll (`src/shared/Scripts/roll`, the iPad's drawer)

**Status (2026-11-26):** live in the iPad's drawer; the web's Blazor host is archived (ADR 0032).

One roll for the web and the iPad: `piano-roll.ts` on canvas, driven by Blazor on the web and by `src/ipad/App/Web/roll.js`
(`window.roll`, messages to Swift) on the iPad. Tests: `tests/shared/roll-*.test.mjs` (node, compiled modules);
`roll-paint.test.mjs` (C6) keeps what `PianoRoll` draws and posts over a fake page (`ROLL_PAINT_WRITE=1` rewrites it).
The page as a whole: the page scenarios `tests/contracts/page-scenarios/roll-*.json` (SEL-01..03, PLAY-02, ROLL-01),
run on `roll.html` in Chromium (`node tests/pages/run.mjs`) and the app's WKWebView (`PageScenarioTests.swift`);
the handles are drawn on the canvas, so they check what the roll posts.

## Modules

- `piano-roll.ts`: `PianoRoll` (baselined): the API, the state shown, `draw` (scene and frame to the
  painter, then the gestures' layers and the playhead), the legend, the floating zoom, the editor and the input
  (`onPointerDown` › `roll-press.ts` › one `begin*` per gesture; `onPointerMove`, `pointerUp`, `cancelPresses`).
  Editor, legend and input router leave in W3.12 part 2.
- `roll-viewport.ts`: `RollViewport` owns zoom, view, glide, following and Expand's passage (`xOf`, `tickAtX`, `yOf`,
  `pitchAt`, `zoom*`, `axisZoom` for OnRollZoom, `follow`, `bind`); reads the roll via `ViewportHost`, rules from
  `roll-view.ts` and `roll-zoom.ts`.
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
- `roll-place.ts`, `roll-place-gesture.ts`, `roll-place-draw.ts` (#601): selection by place in Select. Hit tests (box,
  lasso by note centre, `pickedSpan`, move steps), the press (`RollPlaceGesture` over `score/selection-places.ts`: edge,
  inside = add, outside = new, taps, double tap = Fit, drag of picks = move), its drawing (`HANDLE`, rectangle, lasso).
  v0.6.4: a drag is a span on the grid (`score/selection-grid.ts`); only the lasso picks notes; a span let go is held
  (`placed`) until Swift's selection comes back; only an edge's handle beats a playhead.
- `roll-view.ts`, `roll-zoom.ts`, `roll-playhead.ts`, `roll-head-drag.ts`, `roll-strips.ts`: view and zoom rules, playhead, lanes.

## Not reached by the iPad's roll (W0.6, 2026-11-16, kept)

- The Blazor host's members: `loadKept`, `setNamesKept`, `setChecks`, `pointAt`, `setSelection`, `setPhrases`,
  `setFilter`, `setVariation`, `clearVariation`, `setGhosts`, `setAudible`, `dispose`; `HAND_NONE`, `fitZoom`, `fitRow`,
  `ticksIn`, `narrowedPicks`, `RollPlaceGesture.active`.
- roll.js sets touch reading and axis zoom at load, so their off sides are the web's (`zoomBy`, `reportZoom`,
  `pointerUp`, `movedByFingers`, `onDocumentPointerUp`); the rules before the modes and `!placeOn` run only before
  Swift's first `input` (`setRules`, `heldModel`, `beginRulePress`, `modelChanged`, `pressBarNumbers`, `stripHost`,
  `drawPlace`).

## iPad

- `Web/roll.js`: `window.roll`, the 17 host calls (`load`, `selection`, `input`, `edit`, `zoom`, `frame`, …), and the 13
  messages (`range`, `span`, `notes`, `moveBegan`, `moved`, `edit`, `seek`, `drag`, `zoom`, …) it posts from PianoRoll's
  host callbacks (15 passed on; `OnRollCheckPicked`, `OnRollNotesPicked`, `OnRollPlace`, `OnRollSelection` dropped, the
  web's). The contract: `tests/contracts/roll-page.json` (names, each argument's and message's fields, the answers),
  checked against roll.js by `tests/shared/roll-page-contract.test.mjs` and against Swift by `RollPageContractTests`.
- `Platform/RollPage.swift` (B7): the one place that builds a call (`RollCommand`, `RollPage`) and decodes a message
  (`RollEvent`, `RollRange`, `RollZoom`, `RollSpan`, `RollNotesPick`, `RollNoteEdit`); `drag` goes on to Playback's
  `PlayheadGesture`. `load` takes music-core's `roll` answer typed (`RollAnswer`, read by `PieceCore.roll`; fixture
  `tests/contracts/facade-answers/roll.json`) and passes every field on (`RollAnswer.argument`). `RollController.swift`:
  the web view, `received` → callbacks; `RollPlace.swift`, `RollTools.swift`, `RollDrawer.swift`:
  [`ipad/roll.md`](ipad/roll.md); wiring in `ScoreWorkspace.wire()`.

## Rules not to break

- With the host's place selection off (`setPlaceSelection`; always on the web, which passes no `place`), Select is as
  in 0.6.1. Its old switch `experimental.placeSelection` is gone (v0.7).
- The roll never changes a selection by itself: it sends picks and spans; Swift's ScoreController owns the selection.
- Moving picked notes is the note editor's draft (`corrections.*`); nothing new is saved in the project file.
- In Navigate the stylus moves the roll; in Edit notes a tap on empty space adds a note; a finger beside a stylus moves it.
- With the lanes the stylus in Select takes the playhead by its handle in the strip alone and draws a frame down its line
  (`lineTakesHead`, `laneHeadAt`'s `line`, Andrei 2026-11-17, `roll-sel-on-playhead`); a finger, Navigate and the Pencil
  without the stylus mode (`placeStylus` false, «Только Apple Pencil» off) take it down the line too.
