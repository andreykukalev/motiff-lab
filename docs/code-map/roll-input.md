# Code map: the roll's input router (`src/shared/Scripts/roll`)

Part of [`roll.md`](roll.md). The router takes the notes canvas's pointers and hands each press to one gesture; drawing
stays in the painter. Tests: `tests/shared/roll-press.test.mjs` (the table) and the C6 pointer scenarios in
`roll-paint.test.mjs` (`press*`, recorded on the code before the router moved; the page's timers are faked there).

## Modules

- `roll-input.ts`: `RollInputRouter` (built by `PianoRoll` over `InputHost`): the canvas's listeners; `onPointerDown` ›
  `rollPressFor` › one `begin*` per press kind; `onPointerMove` asks each gesture in turn (fingers, the hold, the
  editor, the strips, the place, a playhead, a range, the note's keys); `pointerUp`, `onPointerCancel`,
  `onLostCapture`, `cancelPresses`. Owns the range dragged (`rangeDragged`) and the selection by place's press.
- `roll-press.ts` (S2): `rollPressFor`, the press table: gutter (keys) › an edge by the place rule › a playhead (R7) ›
  Select by place › touch-reading pan › the old edge › strips or the loop lane › the editor › the modes (R8) › a note.
  Facts with a hit test are asked lazily, in that order; `CLICK_SLOP`, the distance a tap may move.
- `roll-pointer-rules.ts`: `RollPointers`, the host's pointer rules (`setRules`, `setPlace`) and what they say of a
  pointer: `editsWith`, `selectsWith`, `navigates`, `penHolds`, `waitsForLift`, `placeSelects`.
- `roll-fingers.ts`: `RollFingers`, two fingers pinch the zoom (on each axis in touch reading), one finger moves the
  roll (`movePan`, shared with the Navigate hand); a second finger lets go of what the first began; a lost first
  finger's end is cleared (`staleGesture`).
- `roll-head-press.ts`: `RollHeadPress`, a playhead taken by its line or handle (`laneDragTick`), the hold that puts
  the black playhead down (`HoldToPlace`), the view going along at an edge (`stepEdgeScroll`).
- `roll-rule-press.ts`: `RollRulePress`, a press under the modes (`gestureFor`): the hand, a range from outside the
  selection, the Edit notes box (`boxDrawn`, picked by `RollEditor.pickBox`), a click (`OnRollPlace`, or a note).
- `roll-note-press.ts`: `RollNotePress`, a press on a note or the keys that sounds it, and turns into a range drag.
- `roll-strip-gesture.ts`: the strips' hold-based selection gesture of 0.6.1 (`RollStripGesture`, `rangeBars`).
- `roll-place.ts`, `roll-place-gesture.ts`, `roll-place-draw.ts` (#601): selection by place in Select. Hit tests (box,
  lasso by note centre, `pickedSpan`, move steps), the press (`RollPlaceGesture` over `selection/selection-places.ts`: edge,
  inside = add, outside = new, taps, double tap = Fit, drag of picks = move), its drawing (`HANDLE`, rectangle, lasso).
  v0.6.4: a drag is a span on the grid (`selection/selection-grid.ts`); only the lasso picks notes; a span let go is held
  (`placed`) until Swift's selection comes back; only an edge's handle beats a playhead.

## Rules not to break

- One owner per press: a gesture that takes a pointer makes the router drop what else that pointer began
  (`dropPresses`, `letGoForPinch`); a cancelled or lost pointer sends nothing more (`cancelPresses`).
- The host callbacks the roll posts are scanned by `tests/shared/roll-page-contract.test.mjs` from every
  `Scripts/roll/*.ts`. `RollInputRouter` is the TS router; Swift's `RollInput` (InputRules.swift) is the pointer rules it reads.
