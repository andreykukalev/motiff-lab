# Code map: the score's selection (`src/MotiffLab.Web/Scripts/score`, the iPad's `ipad/App/Web/score.js`)

The web and the iPad run the same TS modules; the iPad's page (`score.js`) builds the layers with its host options and
posts what the player did to Swift. Node tests: `tests/web/selection-*.test.mjs` (after `tsc`).

## Modules

- `selection-gesture.ts`: v0.6.1's hold rule (`pressGesture`, `heldGesture`, `movedGesture`), `snapEdge`,
  `editedRange`, `extendedRange`, `HOLD_MS`.
- `selection-places.ts` (v0.6.2, #601, the iPad only; Select's way there since v0.6.3): the rule of the three places
  for the score and the roll: `placeAt(span, x, y, boxes, onMusic)` → `edge-start | edge-end | inside | outside | margin`
  (`EDGE_REACH` 20); `placePress(place, "now" | "longPress")`, `placeMoved`, `placeHeld`, `placeEnded`, `placeChange`,
  `doubleTap`, `drawnRange`, `movedEdge`, `targetLines`, `strokeHand`, `headAt`, `scrollStep`. Pure; the roll too.
- `selection-rect.ts` (v0.6.3): the drawn rectangle `traceBox` and what it covers, `rectPassage`. Pure.
- `selection-place-layer.ts` (v0.6.2 S2): `PlaceLayer` takes the presses while `placesOn()` (score.js: `input.places`
  and Select), shows the gesture through `SelectionLayer.showPlaced`, draws `.sel-edge` (full height, circles),
  ghost, addition, targets, `.sel-trace`; posts `chosen`, `picked`, `cleared`, `zoomSelection`; autoscroll in `view()`;
  prevents the stylus' touches while on; `edgeAt` (an edge beats a playhead knob). `decorate()` from `placeMenu`.
- `selection-boxes.ts`: `bandBoxes`, `setBox`, `ordered`, `handStaves`/`handRuns` (a hand's staff), `onMusic` (margin).
- `selection-layer.ts` (near 600 lines: add modules): band, dot handles, v0.6.1 gesture, menu place.
  Also `selection-handles.ts`, `selection-beats.ts`, `selection-menu.ts`, `menu-drag.ts`.

## Swift side (ipad/App/MotiffLab)

- Page messages: `receivedPlace` (`chosen` with `places` → `chosenByPlace`, `picked`, `zoomSelection`); a refusal
  with the switch on puts the band back and says `selection.tooLong` / `selection.noNotes` (`softlyRefused`).
- `ScoreController.select(request:gesture:step:picked:replaces:)` (music-core `select`); `PlaceSelection.swift`:
  the switch, `selectNotes(_:add:gesture:replaces:)` (the roll's and a note tap's API), `selectSpan(_:_:hand:...)`.
- The one selection: `ScoreSelection` (`Phrases.swift`) with `hand` (music-core's filter) and `picked` (notes picked);
  `SelectionSpan` (`SelectionHistory.swift`) keeps both and `request` selects it again; `record(..., replaces:)`.
- `InputRules.page` tells the page who selects; `places` is `PlaceSelection.on` (always since v0.6.3). The Pencil's «switch to previous»:
  `StylusRules.selectTurn`, `toolBeforeSelect`. Kept on the device: `SelectionSpanKept` (`ViewStates`, optional fields).

## Rules not to break

- The iPad always passes `places` (PlaceSelection.on, v0.6.3); the web passes none and keeps v0.6.1's ways.
- music-core's `select` with `"picked": true` (the iPad only) takes one note or more; a pick a motif of single notes
  cannot hold (1, or over 32) is saved as no selection is (`Motif.Holds`), so no project file changes shape.
