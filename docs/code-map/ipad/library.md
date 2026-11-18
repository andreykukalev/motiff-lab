# Code map: iPad `Library/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Library`.

## Files

`AwayPieces`, `LibraryShelf`, `LibraryShelfView`, `LibrarySwipe`, `LibraryTools`, `LibraryView`, `PieceCard`,
`PiecePropertiesSheet` (`.swift`).

## PDF | Notes (lifecycle, `../ui-review-2026-11.md`)

- `LibraryView.switchView` → `openFile` → `show`: opens the other view, reads only that file's summary, closes the old
  piece (`ClosedPieces`), swaps the navigation item. The screen's model is set in its `init` (`ScoreController()` in
  Navigate, the handed `SourcesHandover.layers`), never after an await; `sources.reopen` (it holds the screen) is set
  only while the screen's `.task` is not cancelled, and `onDisappear` clears it.
- `MotiffLabScreens/SwitchUITests` (with `SWITCH_TEST`) switches it again and again; the Debug probe `ScreensProbe`
  (`Platform/ScreensHook.swift`) gives each switch's phases.
- `MotiffLabTests/SwitchReleaseTests` switches in one window: through the screens' objects (the workspace, its
  controllers and web views, the Pencil watched; the PDF's controllers and pages' view, attached), each handed over by
  its switch; and the real `ScoreScreen` and `PdfScreen` hosted and swapped as `navigationDestination(item:)` does, once
  while the PDF still loads. It checks those objects are freed and the window's recognizers do not grow; not SwiftUI's
  own memory beyond them, WebKit's or music-core's, nor a switch's time.

## The contract others may rely on

Not recorded yet.

## The state it owns

Not recorded yet.

## Baseline entries that protect it

UX baseline: `VIEW-01`, `VIEW-02` (switching PDF | Notes). Quality ratchet: none.
