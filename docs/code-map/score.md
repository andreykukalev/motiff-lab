# Code map: the score's selection (`src/MotiffLab.Web/Scripts/score`, the iPad's `ipad/App/Web/score.js`)

The web and the iPad run the same TS modules; the iPad's page (`ipad/App/Web/score.js`, l. ~484) builds the layers with
its host options and posts what the player did to Swift. Node tests: `tests/web/selection-*.test.mjs` (after `tsc`).

## Modules

- `selection-gesture.ts`: v0.6.1's hold rule (`pressGesture`, `heldGesture`, `movedGesture`), `snapEdge`,
  `editedRange`, `extendedRange`, `staleGesture`, `DRAG_DISTANCE`, `HOLD_MS`.
- `selection-places.ts` (v0.6.2, #601, behind `experimental.placeSelection`, the iPad only): the rule of the three places
  for the score and the roll: `placeAt(span, x, y, boxes, onMusic)` → `edge-start | edge-end | inside | outside | margin`
  (`EDGE_REACH` 20); `placePress(place, "now" | "longPress")`, `placeMoved`, `placeHeld`, `placeEnded`, `placeChange`,
  `doubleTap`, `drawnRange`, `movedEdge`, `targetLines`, `strokeHand`, `headAt`, `scrollStep`. Pure; the roll uses it.
- `selection-place-layer.ts` (v0.6.2 S2): `PlaceLayer` takes the presses while `placesOn()` (score.js: `input.places`
  and Select), shows the gesture through `SelectionLayer.showPlaced`, draws `.sel-edge` (full height, circles),
  `.sel-ghost`, `.sel-add`, `.sel-target`; posts `chosen` (`places`, `hand`, `how`), `picked`, `cleared`,
  `zoomSelection`; autoscroll; prevents a finger's touchmove only. `decorate()` runs from score.js' `placeMenu`.
- `selection-boxes.ts`: `bandBoxes`, `setBox`, `ordered`, `handStaves`/`handRuns` (a hand's staff), `onMusic` (margin).
- `selection-layer.ts` (near the 600-line limit: add new modules): band, dot handles, v0.6.1 gesture, menu place.
  `selection-handles.ts` dots; `selection-beats.ts` beats; `selection-menu.ts`, `menu-drag.ts` the menu.

## Swift side (ipad/App/MotiffLab)

- Page messages: `received` → `chosenByPlace`, `receivedPlace` (`picked`, `zoomSelection`, `zoomedFrom`); a refusal
  with the switch on puts the band back and says `selection.tooLong` / `selection.noNotes` (`softlyRefused`).
- `ScoreController.select(request:gesture:step:picked:replaces:)` (music-core `select`); `PlaceSelection.swift`:
  the switch, `selectNotes(_:add:gesture:replaces:)` (the roll's and a note tap's API), `selectSpan(_:_:hand:...)`.
- The one selection: `ScoreSelection` (`Phrases.swift`) with `hand` (music-core's filter) and `picked` (notes picked);
  `SelectionSpan` (`SelectionHistory.swift`) keeps both and `request` selects it again; `record(..., replaces:)`.
- `InputRules.page` tells the page who selects; `places` is the switch.

## Rules not to break

- Switch off = v0.6.1 exactly; the web passes no `places` and keeps its behaviour.
- music-core's `select` with `"picked": true` (the iPad only) takes one note or more; a pick a motif of single notes
  cannot hold (1, or over 32) is saved as no selection is (`Motif.Holds`), so no project file changes shape.
