# Code map: the score's selection (`src/MotiffLab.Web/Scripts/score`, the iPad's `src/ipad/App/Web/score.js`)

**Status (2026-11-16):** the web app is archived (`docs/decisions/0032-ipad-first-web-archived.md`); these modules stay live in the web project's folder because the iPad bundles them, and iPad work owns them. The web's own callers are frozen.

The web and the iPad run the same TS modules; the iPad's page (`score.js`) builds the layers with its host options and
posts what the player did to Swift. Node tests: `tests/web/selection-*.test.mjs` (after `tsc`).

## Modules

- `selection-gesture.ts`: v0.6.1's hold rule (`pressGesture`, `heldGesture`, `movedGesture`), `snapEdge`,
  `editedRange`, `extendedRange`, `HOLD_MS`.
- `selection-places.ts` (v0.6.2, the iPad only): the rule of the three places for the score and the roll: `placeAt` →
  `edge-start | edge-end | inside | outside | margin` (`EDGE_REACH` 20), `placePress`, `placeMoved`, `placeEnded`,
  `placeChange`, `drawnRange`, `movedEdge`, `strokeHand`, `headAt`, `scrollStep`. Pure.
- `selection-rect.ts` (v0.6.3): the drawn rectangle `traceBox` and what it covers, `rectPassage`. Pure.
- `selection-grid.ts` (v0.6.4): the one grid of the score and the roll, `gridLines` (every sixteenth from each bar's start,
  a tuplet one block: `tupletBlocks` from score.js's `g.tuplet`); PlaceLayer's `grid` option. Pure.
- `selection-place-layer.ts` (v0.6.2 S2): `PlaceLayer` takes the presses while `placesOn()` (score.js: `input.places`
  and Select), shows the gesture through `SelectionLayer.showPlaced`, draws `.sel-edge` (full height, circles),
  ghost, addition, targets, `.sel-trace`; posts `chosen`, `picked`, `cleared`, `zoomSelection`; autoscroll in `view()`;
  prevents the stylus' touches while on; `edgeAt` (an edge beats a playhead knob); a span let go is read as the selection
  until `settle()` (score.js `select`). Handles: score.css `.sel-edge` (ds `.handle` at the edge's middle). `decorate()` from `placeMenu`.
- `selection-boxes.ts`: `bandBoxes`, `setBox`, `ordered`, `handStaves`/`handRuns` (a hand's staff), `onMusic` (margin).
- `selection-layer.ts` (near 600 lines: add modules): band, dot handles, v0.6.1 gesture, menu place.
  Also `selection-handles.ts`, `selection-beats.ts`, `selection-menu.ts`, `menu-drag.ts`.

## Swift side (src/ipad/App/MotiffLab)

- Page messages: `receivedPlace` (`chosen` with `places` → `chosenByPlace`, `picked`, `zoomSelection`); a refusal
  with the switch on puts the band back and says `selection.tooLong` / `selection.noNotes` (`softlyRefused`).
- `ScoreController.select(request:...)` (music-core `select`); `PlaceSelection.swift`: `selectNotes`, `selectSpan`,
  `selectRollSpan`, `RollController.sharePlaces` (span and tuplets between the views).
- The one selection: `ScoreSelection` (`Phrases.swift`) with `hand` (music-core's filter) and `picked` (notes picked);
  `SelectionSpan` (`SelectionHistory.swift`) keeps both and `request` selects it again; `record(..., replaces:)`.
- `InputRules.page` tells the page who selects; `places` is `PlaceSelection.on` (always since v0.6.3). The Pencil's «switch to previous»:
  `StylusRules.selectTurn`, `toolBeforeSelect`. Kept on the device: `SelectionSpanKept` (`ViewStates`, optional fields).

## Rules not to break

- The iPad always passes `places` (PlaceSelection.on, v0.6.3); the web passes none and keeps v0.6.1's ways.
- v0.6.4: no bars/beats choice; Swift selects the span exactly as drawn (`SelectionSpan.request`, no snap); taps unchanged.
- music-core's `select` with `"picked": true` (the iPad only) takes one note or more; a pick a motif of single notes
  cannot hold (1, or over 32) is saved as no selection is (`Motif.Holds`), so no project file changes shape.
