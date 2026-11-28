# Code map: iPad `Workspace/`, the score's screen

Part of [`workspace.md`](workspace.md) (its contract and the order of the reactions are there).

- `PanelState.swift`: `PanelState`, a pure value: the panel open (roll, Insights, Contents, Check, Sources,
  Settings, Reference, More), what More opened, what Expand and Sources keep. `asked(_:)` is what an action writes (an
  `Intent`), `following(_:)` one rule (a `Rule`). Tested by `PanelStateTests` against `Golden/panel-states.txt`.

- `LibraryView.swift`, `LibraryOnline.swift` (v1.6 i-11b): the library's screen ([`library.md`](library.md)): `LibraryModel`
  with the account, the imports, the Reference and `PieceScreen`; `LibraryOnline` syncs and lists the copies online, shows the pieces a pass resolved with a conflict
  (`conflictLines`, E-sync-4) and deletes a piece here and online (`deleteHereAndOnline`).
- `PieceScreen.swift`: the screen the library opens, by what the piece is: `PdfScreen`, `RecordingScreen`, else
  `ScoreScreen`; `reopen` opens the piece's own project file again in its place, `openFile` another (v1.2, collage).
  It follows `RecognitionQueue` and puts `Platform/BackgroundReading` in its screens' environment (1.3 hotfix), and
  `readOnlyLine` for a project a newer version saved (v1.6 c-14a).
- `PdfScreen.swift` with `PdfScreenBar`, `PdfScreenReading`, `PdfScreenSources` (v1.6 i-11): the PDF piece's screen,
  mapped in [`pdf.md`](pdf.md).
- `RecordingScreen.swift` (v1.1, finding 29): a piece that is only its recording: in the top strip the score's bar (Back,
  Contents with a line that there are none yet, the title, Sources), the waveform in the room (or why not: the sound is
  missing or unreadable), `RecordingViewCapsule` on the bottom shelf (v1.7 e-2). «Распознать · Pro» in Sources makes its notes,
  and it opens again as notes.
- `RecordingWave.swift`: the levels of every 100 ms (`RecordingSound.levels(of:)`) as columns, the play cursor, a tap
  seeking there (`moment`, `share`); one adjustable VoiceOver element, ±5 s. Tests: `RecordingWaveTests`.

- `ScoreWorkspace.swift`: `ScoreWorkspace`, a main-actor class: owns the screen's controllers
  (`player`, `score`, `roll`, `insights`, `assistant`, `contents`, `originals`, `sources`, `markedReading`), `panels`. `wire()` hands
  the controllers their callbacks, each a named method here (`blankTapped`, `rollPicked`, …); `change`, `apply` and
  `backToMore` write `panels`; `assistantPressed` is the pill's assistant (its last conversation, or closed; an Ask
  opens it empty). Tests: `ScoreWorkspaceTests`, `DraftsTests`; sessions: `WorkspaceScenarioTests`.
- `ScoreWorkspaceReactions.swift`: the view's `.onChange` reactions on the panels and Expand, in the
  view's order (`contentsChanged`, `focusChanged`, …), with `syncPlayheads`, `showRollPosition`, `rollPlace`.
- `ScoreWorkspaceFollowing.swift`: `following`'s reactions (`selectionChanged`, …), and
  `playRangeChanged`, `applyModel`.
- `ScoreWorkspaceLife.swift`: the screen's life: `appeared` (`wire`, the loads, the resume point, the view kept),
  `disappeared`, `scenePhaseChanged`, `save`, `keepNotesView`, the hand-over (`showPdf`, `handOverForLanguage`, v1.2
  `openElsewhere`: another piece's file in its place, for the collage toast's «Открыть»; the draft's view handed by
  `SourcesHandover.show`).
- `ScoreWorkspaceSources.swift`: `loadOriginals`, `loadSources`, `pickView` (a draft: `showDraft`), `toPdf`.
- `ScoreScreen.swift`: `ScoreScreen` holds the workspace; `ScoreScreenContent` shows it: what only the view keeps,
  `body` (v1.7 e-1: the top strip, the room, the bottom shelf), `page` (the layers in the modifiers, in their order) and
  `layers` (the web view in the room, then the selection menu, the card at a mark, the toasts, the roll in focus,
  `underStrip`, the drawer, the sheet, the Contents; `footBottom`, `footTrailing`).
- Its parts by concern, extensions of `ScoreScreenContent` (v1.6 i-9, ADR 0031 section 4):
  `ScoreScreenBars.swift` (`topStrip`: the bar or the focus bar, a draft's pill under it; `underStrip`: the recording
  heard, `ModeOptions`; `bottomShelf`: the pill, `backToBar` for its chip; `askAboutPiece`, `setPieceTempo`); `ScoreScreenPanels.swift` (`SheetPlace`, `sheetPlace`, `mapOwner`, `sideSheet` and
  what it holds, `contentsDrawer`, `PanelState.binding`); `ScoreScreenRoll.swift` (`rollDrawer`, its height by the
  handle, `rollSpan`, `expandFromDrawer`); `ScoreScreenSources.swift` (the switch of views and Sources: `ShownView`,
  `sourcesSheet`, the «В черновик» sheet, `toastsOverSelection`, `sourcing`, `pickView`, `shownViewScreen`);
  `ScoreScreenReactions.swift` (`wired`, `watching`, `checking`, `following`, with the covers and sheets they present:
  Piece DNA by `openDna`, the linked PDF, the Reference, Settings).
- Its views, each in its own file, handed what they show and the actions they call, so a new layout can place them
  elsewhere without touching what they do: `ScoreBar.swift` (`TopBar` docked: Back, Contents, the title, the switch,
  the pages, the modes, ⓘ and its list, the Circle, Insights, More); `Pill.swift` (Play, the chip back to the playing, Цикл, ‹ › | the tempo, the
  hands, the metronome | the roll, the assistant; full, compact with «⋯», tight, the chip in each; 44 pt by `itemReach`, 0 in the tempo's popover); `PillItems.swift` (`iconItem`, `stepItem`, `ItemDivider`, also `DraftAssemblyPill`'s);
  `SelectionMenu.swift` (with `ClearSelectionButton`; `offered`; «Что не так?» in More on notes read from a PDF, v1.6 c-14b3); `FocusBar.swift` (with `FocusShowAs`,
  `FocusRollCard`); `HearingSwitch.swift` (also the Insights sheet's); `InsightsSheet.swift`
  ([`insights.md`](insights.md)); `SourcesSheet.swift` ([`sources.md`](sources.md), v1.6 i-13); `SettingsSheet.swift`
  (also the library's and the PDF's, [`account.md`](account.md), v1.6 i-14); `MorePanel.swift`; `ScoreWebView.swift`
  (the page's web view, `prewarm` at launch).
- `TopBar.swift`: the bar's layout over the score and over a PDF alike: the same start in both, then the pages, then
  each view's actions; where it is short the view's note goes first, then the pages, then the actions turn compact;
  the top strip's row on every view (v1.7). `dockedAtTop`: the top strip (`Platform`'s `DockedStrip`) with the
  read-only line (`readOnlyLine`, v1.6 c-14a) under it; `hungUnderTopStrip`: what hangs under it.
