# Code map: the selection's rules (`src/shared/Scripts/selection`)

**Status (2026-11-27, v1.6 i-15, audit W3.11):** the pure selection rules and the pages' hold timing, moved here from
`Scripts/score` so that no two folders of `src/shared/Scripts` import each other. Pure: no DOM, no import of another
folder. The score's layers that use them: [`score.md`](score.md); the roll's: [`roll-input.md`](roll-input.md).

## Who imports it

The score (`score/selection-*-layer.ts`, `selection-press.ts`, `colour-bars.ts`, `menu-drag.ts`, `phrase-drag.ts`,
`page-contract.ts`, `ipad-page/page-selection.ts`), the roll (`roll-place-gesture.ts`, `roll-strip-gesture.ts`,
`roll-input.ts`, `roll-fingers.ts`, `piano-roll.ts`) and the playheads (`playback/hold-place.ts`, `playhead-press.ts`:
the hold timing). The page replayer reads `HOLD_MS` and `HOLD_SLOP` from the bundled module (`tests/pages/replay.js`).
The iPad bundles the folder as `selection/` (`src/ipad/App/project.yml`). Which folder may import which:
`SCRIPT_FOLDER_USES` in `tools/quality/dependencies.mjs` (rule `script-folder`; `selection` and `storage` import
nothing, `playback` only `selection`).

## Modules

- `selection-gesture.ts`: who selects and how (`dragStartOf`, `handleTakes`, `handlesShown`, `staleGesture`); the
  pages' hold (`HOLD_MS` 450, `HOLD_SLOP` 8, `DRAG_DISTANCE` 6); v0.6.1's hold rule (`pressGesture`, `heldGesture`,
  `movedGesture`, `gestureChange`), `snapEdge`, `editedRange`, `extendedRange`, `nextGestureId`, `TickRange`,
  `SelectionChange`.
- `selection-places.ts` (v0.6.2, the iPad only): the rule of the three places for the score and the roll: `placeAt` →
  `edge-start | edge-end | inside | outside | margin` (`EDGE_REACH` 20), `placePress`, `placeMoved`, `placeHeld`,
  `placeEnded`, `placeChange`, `drawnRange`, `movedEdge`, `targetLines`, `edgeHandles`, `onEdgeHandle`, `strokeHand`,
  `headAt`, `doubleTap`, `scrollStep`.
- `selection-rect.ts` (v0.6.3): the drawn rectangle `traceBox` and what it covers, `coveredSystems`, `rectPassage`.
- `selection-grid.ts` (v0.6.4): the one grid of the score and the roll, `gridLines` (every sixteenth from each bar's
  start, a tuplet one block: `tupletBlocks` from the page's `g.tuplet`).

Node tests: `tests/shared/selection-gesture`, `selection-places`, `selection-rect`, `selection-grid`,
`selection-score-rules` (`.test.mjs`); the page scenarios SEL-01 to SEL-04 run them on the pages.

## Rules not to break

- Nothing here touches the DOM or imports another folder: a rule that needs the score's geometry or the page stays in
  `Scripts/score` and calls these.
- One rule for the score and the roll: a change here changes both pages, and their scenarios must hold.
- The iPad always passes `places` (`PlaceSelection.on`, v0.6.3); the web passed none and kept v0.6.1's ways.
- v0.6.4: no bars/beats choice; Swift selects the span exactly as drawn (`SelectionSpan.request`, no snap); taps unchanged.
- A finger's long press on a bar selects the bar; moved on, it draws the stylus's frame from the press (`placeMoved`:
  `bar` → `draw`, Andrei 2026-11-17), never from the bar's start.
