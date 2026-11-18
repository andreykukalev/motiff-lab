# Code map: iPad `Ink/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Ink`.

## Files

`InkGeometry`, `InkInput`, `InputRules`, `InteractionMode`, `ModeGroup`, `PdfInk`, `PenPalette`, `PenState`, `PenTools`,
`PencilPalette` (`.swift`).

- `InkInput.swift`, `InputRules.swift`, `PencilPalette.swift`: who writes and who scrolls, Pencil gestures.
- `InteractionMode.swift`: the modes in the bar; who writes, selects and scrolls in each mode is `InputRules`'.
- `InkGeometry.swift`: `InkSplit`, the eraser's cut of a stroke, as `Scripts/score/pencil-marks.ts` does it.

## The contract others may rely on

Not recorded yet.

## The state it owns

- The Pencil's hover over a window (`WindowHover`, `PencilPalette.swift`): one recognizer on the window, put on it by
  the first score's screen that watches it and shared by the next ones, so the window keeps no recognizer per visit
  (#681).

## Baseline entries that protect it

UX baseline: `INK-01`. Quality ratchet: none.
