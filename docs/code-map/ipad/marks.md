# Code map: iPad `Marks/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Marks`.

## Files

`Bookmarks`, `Check`, `CheckModel`, `EditsLayer`, `HighlightPaint`, `Marks`, `MarksModel`, `ReadingMarks`, `ScanReviewBlock` (`.swift`).

- `HighlightPaint.swift` (v0.10 d-5, finding 31): what the pen's colour bars and the eraser do to the highlights, as a
  `Recolouring` (Platform: the highlights taken and laid, by value): a passage coloured lays it in its colour (other
  colours give way there, the same colour joins it), one all that colour already loses it, the eraser takes the colour
  where it passed; `ids(of:over:)` finds by value the ids of what a change takes, so the Pen's Undo and Redo (v1.4 i-5)
  find highlights music-core laid again under new ids. `HighlightMark.notes` keeps an older file's notes for that. Pure; `Workspace/ScoreColouring.swift` sends the calls. Tests: `MotiffLabTests/HighlightPaintTests`. The
  selection menu's «Выделить цветом» (`HighlightRow`) is gone with it.

- `MarksModel.swift` (S6, ADR 0031 section 3, v0.10 d-4): `MarksModel`, the marks' state and their own rules; it reads
  `SelectionModel` and is given the device's settings resolved for a piece (`DeviceSettings.resolved`). Since v1.6
  i-10b it draws and changes the marks (`draw`, `changeMarks`), the bookmarks and margin notes, the layers and the
  settings followed, and what the page hides (`applyLayers`), through `MarksScore` (what Marks may not name: Check's
  drawer, slate, Expand, the versions). The lists of layers and of the kinds of extra information (`layers`,
  `extra`) are here, which `tests/shared/i18n.test.mjs` reads. Tests: `MotiffLabTests/MarksModelTests` (fake score);
  the baseline's old name through `MotiffLabTests/MarksBaselineNames`.

- `CheckModel.swift` (S6, ADR 0031 section 3, v1.6 i-8): `CheckModel`, Check's state and calls out of `ScoreController`:
  the shelf (`check`), the drawer open (`drawerOpen`), the scan's variants and crops, the variant chosen, Fix notes
  (`corrections`), «Мои правки» (`editsLayer`, `editsOn`), the roll's note editor (`noteEditor`), the assistant's drafts
  accepted or undone as corrections, and `editsKept`. It reads `SelectionModel` (the editor's bar), is given
  `ScanServer.jobs` (Import's) as `scanJobs`, calls music-core itself (`check`, `check.resolve`, `check.mark`,
  `check.unmark`, `corrections.*`; the three Check changes through `changeCheck`) and asks the root, through `CheckScore`,
  for the page's part: `showSpan`, `selectBars` (a mark's bars: `showMark`, and `showCheckBar` on a mark's flag),
  `drawChecks` (its `flags`, the marks «что не так» among them at their first bar,
  `adding` joining a bar's labels, E-scan-1 and E-scan-12, with the unread pages of `Workspace/ScoreUnreadPages`),
  `applyLayers`, `notesDidChange`, `showNotice`, `showProblem`. Since v1.6 c-14b3 it keeps where the bars start
  (`barStarts`, from `playback`) while the notes have marks, which name bars only, for their flags' ticks. Tests:
  `MotiffLabTests/CheckModelTests` (the fake score of `MotiffLabTests/FakeCheckScore`, shared with
  `ReadingMarksTests`); the protected baseline reads it by its old names through
  `MotiffLabTests/CheckBaselineNames`.

- `Check.swift`: `CheckShelf` (`check`: the flags, a recording's note lengths to check, #352, the marks «что не так»;
  `hasEntries`, which More offers Check for, `takesMarks`, `reviewed`: the reasons `enginesDisagree` and `readAgain` whose
  bar shows `ScanReviewBlock`, and the drawer's `heading` and `how`, E-sheets-1), the Check
  drawer (observes `CheckModel`; generic over what reads the marked pages again, which Workspace builds, and told by
  `marking` whether the notes take marks), and Fix notes: `NoteCorrections`, a bar's notes built from
  `CorrectionsAnswer` (`PieceCore.corrections`), listed by `CorrectionsList`.
- `ReadingMarks.swift` (v1.6 c-14b3, the 1.6 plan's decision 15): `ReadingMarkEntry` (`check`'s "marks", music-core's
  words), `ReadingMarkChoice` (what «Что не так?» offers and `check.mark`'s fields, a meter or key at its first bar),
  `WhatsWrongMenu` (the native menu, in a Check entry and the selection menu's More), `CheckFlagSign` (the amber «?»),
  `ReadingMarksSection` (the marks in Check with «Показать», «Убрать» and the reading view). Tests:
  `MotiffLabTests/ReadingMarksTests`.
- `ScanReviewBlock.swift` (v1.3 i-2, with «Четыре движка», `ScanServer.jobs`): in the Check entry of the bar selected, an
  EnginesDisagree or ReadAgain bar's (`BarFlags.reviewed`; ReadAgain since v1.6 c-14b3, a bar marked as read badly and
  read again, whose earlier reading can be taken back) crop of the page and the engines' other readings, one tap each
  (`CheckModel.chooseVariant`,
  `corrections.variant`; `undoVariant`, `corrections.undoDraft`, held by `ScanVariantChoice`), from `CheckModel.scanReview` and `scanCrops` (read once, off the main thread); without them a short line.
- `EditsLayer.swift` (v0.6.5, «Мои правки»): `EditsLayerState`, built from `EditsLayerAnswer` (every corrections
  answer, and the open answer's "edits": "count", "layer", "moved", "unfit"), `EditsLayerSection` in More (the switch,
  its line, the misfits with «Убрать»). `OpenPiece.openRequest` opens a project file with "edits": "piece" (the file keeps "off");
  `CheckModel.switchEdits` / `dropUnfitEdits` (`corrections.layer` / `.drop`) engrave again and save at once
  (`editsKept`).

## The contract others may rely on

While «Мои правки» is off, `editsOn` is false: no Fix notes, no note editor, no draft Accept (music-core refuses them
too). The note editor's calls send the selection's first bar, else none, and music-core answers for the piece's first
(0 for a pickup; `EditsLayerState.correctionsRequest`), never a guessed bar 1. `NoteCorrections` and
`EditsLayerState` hold only what music-core answered (`tests/contracts/facade-answers/corrections*.json`, decoded by `FacadeAnswerFixturesTests`). The rest: not recorded yet.

`MarksModel`: `draw` keeps and draws what a mark call answered (a refusal goes to the problem line); `showLayers`,
`clearOverrides` change the piece's own settings at once and keep them through music-core (`settings.override`; only
`layers.*` are read as the piece's), `showPhraseMarks` the device's choice of the phrases' marks; a bookmark gone to
again with the selection it gave lifts into focus, but with the Pen on.

## The state it owns

`CheckModel`: the Check shelf (with the marks «что не так» music-core keeps), where the bars start while there are marks, the drawer open, the scan's review and crops, the variant chosen, the bar listed to correct,
`EditsLayerState`, the note editor (`NoteEditorState`, Platform). `MarksModel`: the marks (`MarksShelf`, `markCount`), the note being written (`noteDraft`), the
layers hidden, the piece's own settings, whether the phrases' marks are hidden (`phraseMarksHidden`, kept on the device,
`motiff.phraseMarks.hidden`; the old `motiff.phrases.hidden` is not read), the bookmarks as drawn and the one gone to. The rest: not recorded yet.

## Baseline entries that protect it

UX baseline: `SCAN-06` (`ScanCheckBaselineTests`). Quality ratchet: `Bookmarks.swift` file_length; `Check.swift`'s
folder edges (`Import: PageSummaryLine`, `JoinedPage`, `PhotoPagesCheck`; `Workspace: ScoreController` for the parts
about a scan's pages).
