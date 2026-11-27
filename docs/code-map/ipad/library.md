# Code map: iPad `Library/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Library`. Cut by model and views (W2.12, ADR 0031,
v1.6 i-11b); the screen that composes it is `Workspace/LibraryView` ([`workspace-screen.md`](workspace-screen.md)).

## Files

- `LibraryModel.swift`: `LibraryModel`, the library's model (main actor, `ObservableObject`): the project files of the
  environment's folder and their summaries (`reload`, the newest reading wins; `ProjectFileCache`), the shelf made from
  them (`entries`, `composers`, `tags`, `playlists`, `query`, `picked`, and `tab`, kept in `library.tab`: another tab
  forgets the group picked and starts its order, Recent the last opened first), the piece on screen (`piece`), a PDF not
  found where it lies (`away`), the line under the header (`problem`, `showProblem`) and `opening`. Made with the folder,
  the defaults, how a file is removed and the recognition's `opened` (Import's queue, handed in), which `piece` alone
  tells the file on screen (and an open under way the file it reads).
- `LibraryShelf.swift`: the shelf's values and rules: tabs, sort, query, `LibraryEntry` (and `LibraryEntry.of`, a line
  from a project file and its summary), `LibraryPage`, search, filters, order, the index's letters, `LibrarySource`.
- `LibraryShelfView.swift`: the tabs, then the list, the groups, the Sessions' cards or the copies online; the empty
  states; `columns`, also the online shelf's (`Account/OnlineProjects`).
- `LibraryTabs.swift`: `LibraryTabs`, `LibraryFilter`, `LibrarySearchField`, `LibraryIndex`: the ways to find a piece.
- `LibraryList.swift`: `LibraryList`, `LibraryLine` (with picking to recognise), `SourceIcons`, `LineField`, `GroupList`.
- `LibrarySwipe.swift`: a piece's actions: `LineSwipe` (the line's swipe) and `PieceActions` (the long press, Delete
  asking once more).
- `PieceCard.swift`: a session's card; a piece that is only its recording is called a recording, resumed «Запись как
  есть» (`PieceSummary.isRecordingOnly`, v1.1). `PiecePropertiesSheet.swift`: title, composer, tags, playlists.
- `AwayPieces.swift`: a piece that is only a PDF whose PDF lies elsewhere: `AwayPdf` reads it there, `AwayPieceSheet`.

## The contract others may rely on

- Every way of opening a piece goes through the model: `open(_:)` (Files, Add piece), `openExample`, `openDownloaded`
  (Get it here, the download run inside the opening), `openProject` (a line, a folder's piece), `openFound`,
  `openFile(_:at:)` (a screen's file in its place, the language's reopen) and `switchView(from:to:)` (PDF | Notes, only
  from the screen shown). One at a time (`openOnce`, E-library-4): an ask meanwhile does nothing.
- A piece opened from Files (or the example) is saved as a new project file named by its title, `OpenPiece.title`
  (`ProjectFiles.newURL`; v1.5 r-2b), not by the file it came from; existing files keep their names. A PDF this iPad
  cannot show is refused by `OpenPiece.open` before it becomes a piece (E-library-2). What Files offers is
  `OpenPiece.openable`.
- `delete(_:)` is the one way a piece is deleted (c-17 adds its copy online there): a file that cannot be removed keeps
  its line and the library says why (E-library-5).

## PDF | Notes (lifecycle, `../ui-review-2026-11.md`)

- `switchView` → `openFile` → `show`: opens the other view, reads only that file's summary, closes the old piece
  (`ClosedPieces`) as its screen gives way, swaps `piece`. The screens' own `handingOver` only keeps their save before
  they ask. The screen's model is set in its `init` (`ScoreController()` in Navigate, the handed `SourcesHandover.layers`),
  never after an await; `sources.reopen` (it holds the screen) is set only while the screen's `.task` is not cancelled,
  and `onDisappear` clears it.
- `MotiffLabScreens/SwitchUITests` (with `SWITCH_TEST`) switches it again and again; the Debug probe `ScreensProbe`
  (`Platform/ScreensHook.swift`) gives each switch's phases.
- `Baseline/SwitchReleaseTests` switches in one window: through the screens' objects (the workspace, its
  controllers and web views, the Pencil watched; the PDF's controllers and pages' view, attached), each handed over by
  its switch; and the real `ScoreScreen` and `PdfScreen` hosted and swapped as `navigationDestination(item:)` does, once
  while the PDF still loads. It checks those objects are freed and the window's recognizers do not grow; not SwiftUI's
  own memory beyond them, WebKit's or music-core's, nor a switch's time.

## The state it owns

`LibraryModel` alone writes the files' list, the summaries, the shelf, the piece on screen and `opening`; the views get
bindings to `tab`, `query`, `picked`, `piece` (Back) and `away`. The screen keeps only what it presents (sheets, the
search's focus, picking to recognise) and the copies online.

## Baseline entries that protect it

UX baseline: `VIEW-01`, `VIEW-02` (switching PDF | Notes), `ADD-01` (reads `LibraryView.openable` through the test-side
`MotiffLabTests/LibraryBaselineNames.swift`), `ADD-03`. Tests: `LibraryModelTests`, `LibraryShelfTests`. Quality ratchet:
the folder edges of the views to Account, Import and Sources, and `LibraryModel`'s three to `Pdf` (`Landing`,
`OtherView`, `PieceFamily`, moved from the screen).
