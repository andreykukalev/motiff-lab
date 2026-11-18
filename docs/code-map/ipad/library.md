# Code map: iPad `Library/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Library`.

## Files

`AwayPieces`, `LibraryShelf`, `LibraryShelfView`, `LibrarySwipe`, `LibraryTools`, `LibraryView`, `PieceCard`,
`PiecePropertiesSheet` (`.swift`).

## PDF | Notes (lifecycle, `../ui-review-2026-11.md`)

- `LibraryView.switchView` → `openFile` → `show`: opens the other view, reads only that file's summary, closes the old
  piece (`ClosedPieces`), swaps the navigation item. The screen's model is set in its `init` (`ScoreController()` in
  Navigate, the handed `SourcesHandover.layers`), never after an await; `onDisappear` clears `sources.reopen` (it held
  the screen).
- `MotiffLabScreens/SwitchUITests` (with `SWITCH_TEST`) switches it again and again; the Debug probe `ScreensProbe`
  (`Platform/ScreensHook.swift`) gives each switch's phases.
- `MotiffLabTests/SwitchReleaseTests` goes notes → PDF → notes five times through both screens' real objects (the
  workspace, its controllers, web views and pages; the PDF's controllers) and checks that each visited screen is let go.

## The contract others may rely on

Not recorded yet.

## The state it owns

Not recorded yet.

## Baseline entries that protect it

UX baseline: `VIEW-01`, `VIEW-02` (switching PDF | Notes). Quality ratchet: none.
