# Code map: iPad `Ink/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Ink`.

## Files

`InkGeometry`, `InkInput`, `InputRules`, `InteractionMode`, `ModeGroup`, `PdfInk`, `PenModel`, `PenPalette`, `PenState`,
`PenTools`, `PencilPalette` (`.swift`).

- `PenModel.swift` (S6, ADR 0031 section 3, v0.10 d-4): `PenModel`, the score's pen (a `PenHolder`, as `PdfController`
  is for a PDF's pages), and `PenScore`, what it needs of the score (`ScoreController`): the page as it is now, the piece,
  `changeMarks` and `penTurned`. Tests: `MotiffLabTests/PenModelTests` (a fake score).

- `InkInput.swift`, `InputRules.swift`, `PencilPalette.swift`: who writes and who scrolls, Pencil gestures.
- `InteractionMode.swift`: the modes in the bar; who writes, selects and scrolls in each mode is `InputRules`'.
- `InkGeometry.swift`: `InkSplit`, the eraser's cut of a stroke, as `Scripts/score/pencil-marks.ts` does it.

## The contract others may rely on

`PenModel`: `keep` anchors a finished stroke on the page (`anchor`) and keeps it (`ink.add`) as one step; `rub` sends
the eraser's points as it moves (`rub`) and, when it lifts, saves the press as one step (`rubEnd`, `ink.add` for the
pieces left without drawing, then `ink.remove`); `undo` and `redo` go through `InkStep`; all of it in one queue
(`InkQueue`). Setting `penOn` calls `PenScore.penTurned`, where the root applies the input rules, tells the roll, the
page's `pen` and its mode, in that order. Who writes and scrolls (`InkInput`, `InputRules`) and where a point is on the
page stay the root's, which hands the points in in the page's pixels.

## The state it owns

- `PenModel`: the pen (`PenState`, kept on the device by `PenMemory`), `penOn`, the pencil palette, the Pen's history
  (`InkHistory`, `canUndo`, `canRedo`), the eraser's points waiting and the ink queue.

- The Pencil's hover over a window (`WindowHover`, `PencilPalette.swift`): one recognizer on the window, put on it by
  the first score's screen that watches it and shared by the next ones, so the window keeps no recognizer per visit
  (#681).

## Baseline entries that protect it

UX baseline: `INK-01`. Quality ratchet: none.
