# Code map: the score's selection (`src/MotiffLab.Web/Scripts/score`, the iPad's `ipad/App/Web/score.js`)

The web and the iPad run the same TS modules; the iPad's page (`ipad/App/Web/score.js`, l. ~484) builds the
`SelectionLayer` with its host options and posts what the player did to Swift (`chosen`, `cleared`, `gestureCancelled`,
`holdHint`, `menu`, `handles`). Node tests: `tests/web/selection-*.test.mjs` (run after `tsc -p src/MotiffLab.Web`).

## Modules

- `selection-gesture.ts`: v0.6.1's rule (hold phases `waiting`/`cleared`/`creating`, `pressGesture` with `barHold`,
  `heldGesture`, `movedGesture`, `gestureChange`), `snapEdge`, `editedRange`, `extendedRange` (#582 item 9),
  `staleGesture`, `DRAG_DISTANCE`, `HOLD_MS`, `HOLD_SLOP`.
- `selection-places.ts` (v0.6.2, #601, behind `experimental.placeSelection`, the iPad only): the rule of the three places
  for the score and the roll: `placeAt(span, x, y, boxes, onMusic)` → `edge-start | edge-end | inside | outside | margin`
  (`EDGE_REACH` 20, an edge wins over inside, short runs keep an inside); `placePress(place, "now" | "longPress")`,
  `placeMoved`, `placeHeld` (a finger beside the stylus only), `placeEnded` → `edge | extend | draw | bar | tap | tapInside
  | clear | none`, `placeChange`; `doubleTap`; `drawnRange` (outward to the lines), `movedEdge`, `targetLines`;
  `staffAt`, `strokeHand` (one staff = that hand); `headAt` (±`NOTE_REACH` 7). Pure.
- `selection-layer.ts` (at the 600-line limit: new behaviour goes into new modules): the band, the dot handles, the
  gesture of v0.6.1, the menu's place. `selection-handles.ts`: the dot handles' drag and keys. `selection-beats.ts`:
  beat lines, the band cut to beats, beat ticks. `selection-menu.ts`, `menu-drag.ts`: the menu's place and drag.

## Swift side (ipad/App/MotiffLab)

- `ScoreController.select(request:gesture:step:picked:replaces:)` (music-core `select`); `PlaceSelection.swift`:
  the switch, `selectNotes(_:add:gesture:replaces:)` (the roll's and a note tap's API), `selectSpan(_:_:hand:...)`.
- The one selection: `ScoreSelection` (`Phrases.swift`) with `hand` (music-core's filter) and `picked` (notes picked);
  `SelectionSpan` (`SelectionHistory.swift`) keeps both and `request` selects it again; `record(..., replaces:)`.
- `InputRules.page` tells the page who selects; `places` is the switch, on the notes only.

## Rules not to break

- Switch off = v0.6.1 exactly; the web passes no `places` and keeps its behaviour.
- music-core's `select` with `"picked": true` (the iPad only) takes one note or more; a pick a motif of single notes
  cannot hold (1, or over 32) is saved as no selection is (`Motif.Holds`), so no project file changes shape.
