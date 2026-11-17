# Code map: the piano roll (`src/shared/Scripts/roll`, the iPad's drawer)

**Status (2026-11-16):** the web app is archived (`docs/architecture/adr/0032-ipad-first-web-archived.md`); `src/shared/Scripts/roll` stays live because the iPad's drawer runs it, and iPad work owns it. The Blazor side is frozen in `archive/web`.

One roll for the web and the iPad: `piano-roll.ts` on canvas, driven by Blazor on the web and by `src/ipad/App/Web/roll.js`
(`window.roll`, messages to Swift) on the iPad. Tests: `tests/shared/roll-*.test.mjs` (node, on the compiled modules).
What protects the page as a whole: the page scenarios `tests/contracts/page-scenarios/roll-*.json` (SEL-01, SEL-02, SEL-03,
PLAY-02, ROLL-01), replayed on `roll.html` in Chromium by CI (`node tests/pages/run.mjs`) and in the app's WKWebView
(`PageScenarioTests.swift`). The roll's selection handles are drawn on its canvas, so the scenarios check what it posts.

## Modules

- `piano-roll.ts`: `PianoRoll` (the view, ~4000 lines, baselined): `load`/`show`, `setMotif`, `drawRoll` (grid, band,
  notes), `onPointerDown` → `beginPlace`, `pressPanOrEdge` (touch reading pans), `pressBarNumbers` → strips, `editPress`
  (Edit notes), `beginRulePress` (modes 0.5); `onPointerMove`, `pointerUp` (chain of `end*`), `cancelPresses`.
- `roll-model.ts`: notes and bars from music-core's packed payload; `noteAt`, `inRange`, `grid`.
- `roll-modes.ts`: what a press begins under the bar's tool (`gestureFor`, `selectsOnRoll`, `navigatesOnRoll`), the
  Edit notes box (`notesInBox`, overlap rule), the one playhead.
- `roll-strip-gesture.ts`: the strips' hold-based selection gesture of 0.6.1 (`RollStripGesture`, `rangeBars`).
- `roll-place.ts`, `roll-place-gesture.ts`, `roll-place-draw.ts` (#601, `experimental.placeSelection`): selection by
  where you touch in Select. Pure hit tests (box and lasso by note centre, `pickedSpan` out to beats, `narrowedPicks`,
  move steps of a sixteenth and a semitone), the press state (`RollPlaceGesture` over the score's rule
  `score/selection-places.ts`: edge, inside = add, outside = new, taps, double tap = Fit, drag of picked notes = move)
  and its drawing (`HANDLE`: the score's hollow handles at the edges' middles, the dashed rectangle, lasso, old span dashed).
  v0.6.4: a drag is a span of time on the grid (`score/selection-grid.ts`, tuplets from the score), notes or none; only the
  lasso picks notes; a span let go is held (`placed`) until Swift's selection comes back; an edge wins over a playhead.
- `roll-view.ts`, `roll-zoom.ts`, `roll-playhead.ts`, `roll-head-drag.ts`, `roll-strips.ts`: view, zoom, playhead, lanes.

## Not reached by the iPad's roll (W0.6, measured 2026-11-16; written down, not cut)

- Members (the Blazor host's API): `PianoRoll.loadKept`, `setNamesKept`, `setChecks`, `pointAt`, `setSelection`,
  `setPhrases`, `setFilter`, `setVariation`, `clearVariation`, `setGhosts`, `setAudible`, `dispose`; `HAND_NONE`, `fitZoom`
  and `fitRow` of `roll-model.ts`; `ticksIn`; `insideOf` of `roll-strips.ts` (`playback-model.ts` has its own);
  `narrowedPicks`; `RollPlaceGesture.active`. Several have node tests only.
- Branches: `roll.js` sets `setTouchReading(true)` and `setAxisZoom(true)` at load, so the `!touchReading` and
  `!axisZoom` sides are the web's (`zoomBy`, `reportZoom`, `pointerUp`, `movedByFingers`, `onDocumentPointerUp`). The
  `mode === null` (rules before the modes) and `!placeOn` sides run only before Swift's first `input`: `setRules`,
  `heldModel`, `beginRulePress`, `modelChanged`, `pressBarNumbers`, `stripHost`, `drawRoll`, `drawPlace`.

## iPad

- `Web/roll.js`: host calls → messages (`range`, `span`, `notes`, `moveBegan`, `moved`, `edit`, `seek`, …); `tuplets`; `input` carries
  `RollInput.page` plus `place`/`stylus` (`RollPlaceInput`), `shape` the box or the lasso.
- `RollController.swift`: the web view, `received` → callbacks (`picked`, `notesPicked`, `notesMoveBegan`, `notesMoved`).
- `RollPlace.swift`: `RollPickShape` (UserDefaults `roll.pickShape`), `RollPlaceInput`, `RollNotesPick`, `RollPickSwitch`.
- `RollTools.swift` (mode switch, zoom, edit tools), `RollDrawer.swift` (the head), wiring in `ScoreWorkspace.wire()`.

## Rules not to break

- With the host's place selection off (`setPlaceSelection`; always on the web, which passes no `place`), Select is as
  in 0.6.1. The switch `experimental.placeSelection` that once chose it is gone from the catalogue (v0.7).
- The roll never changes a selection by itself: it sends picks and spans; Swift's ScoreController owns the selection.
- Moving picked notes is the note editor's draft (`corrections.*`); nothing new is saved in the project file.
- In Navigate the stylus moves the roll; in Edit notes a tap on empty space adds a note; a finger beside a stylus moves it.
- With the lanes the stylus in Select takes the playhead by its handle in the strip alone and draws a frame down its line
  (`lineTakesHead`, `laneHeadAt`'s `line`, Andrei 2026-11-17, `roll-sel-on-playhead`); a finger, Navigate and the Pencil
  without the stylus mode (`placeStylus` false, «Только Apple Pencil» off) take it down the line too.
