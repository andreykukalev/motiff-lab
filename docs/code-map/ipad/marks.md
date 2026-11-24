# Code map: iPad `Marks/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Marks`.

## Files

`Bookmarks`, `Check`, `EditsLayer`, `HighlightPaint`, `Marks`, `MarksModel`, `ScanReviewBlock`, `SegmentedChoice` (`.swift`).

- `HighlightPaint.swift` (v0.10 d-5, finding 31): what the pen's colour bars and the eraser do to the highlights, as
  `highlight.remove` and `highlight.add` (span and colour): a passage coloured lays it in its colour (other colours give
  way there, the same colour joins it), one all that colour already loses it, the eraser takes the colour where it
  passed. Pure; `Workspace/ScoreColouring.swift` sends the calls. Tests: `MotiffLabTests/HighlightPaintTests`. The
  selection menu's «Выделить цветом» (`HighlightRow`) is gone with it.

- `MarksModel.swift` (S6, ADR 0031 section 3, v0.10 d-4): `MarksModel`, the marks' state and their own rules; it reads
  `SelectionModel` and is given the device's settings resolved for a piece (`DeviceSettings.resolved`). The lists of
  layers and of the kinds of extra information (`layers`, `extra`) are here, which `tests/shared/i18n.test.mjs` reads.
  Tests: `MotiffLabTests/MarksModelTests`.

- `SegmentedChoice.swift`: the choice of two or three (ds.css `.seg`: `SegmentedChoice`, `SegmentFill`,
  `SegmentedCapsule`), which other folders use too; out of `Bookmarks.swift` in S7b.

- `Check.swift`: `CheckShelf` (`check`), the Check drawer, and Fix notes: `NoteCorrections`, a bar's notes built from
  `CorrectionsAnswer` (`PieceCore.corrections`), listed by `CorrectionsList`.
- `ScanReviewBlock.swift` (v1.3 i-2, experiment `experimental.scanJobs`): in the Check entry of the bar selected, an
  EnginesDisagree bar's crop of the page and the engines' other readings, one tap each (`ScoreController.chooseVariant`,
  `corrections.variant`; `undoVariant`, `corrections.undoDraft`), from `ScoreController.scanReview`; without it a short line.
- `EditsLayer.swift` (v0.6.5, «Мои правки»): `EditsLayerState`, built from `EditsLayerAnswer` (every corrections
  answer, and the open answer's "edits": "count", "layer", "moved", "unfit"), `EditsLayerSection` in More (the switch,
  its line, the misfits with «Убрать»). `OpenPiece.openRequest` opens a project file with "edits": "piece" (the file keeps "off");
  `ScoreController.switchEdits` / `dropUnfitEdits` (`corrections.layer` / `.drop`) engrave again and save at once
  (`editsKept`).

## The contract others may rely on

While «Мои правки» is off, `editsOn` is false: no Fix notes, no note editor, no draft Accept (music-core refuses them
too). `NoteCorrections` and `EditsLayerState` hold only what music-core answered
(`tests/contracts/facade-answers/corrections*.json`, decoded by `FacadeAnswerFixturesTests`). The rest: not recorded yet.

`MarksModel`: `show` takes what a mark call answered, `keepBookmarks` the bookmarks the page draws; `showLayers`,
`clearOverrides` change the piece's own settings at once (only `layers.*` are read as the piece's), `showPhraseMarks` the
device's choice of the phrases' marks, and `keepLayers`, `forgetOverrides` and
`loadSettings` keep or read them through music-core (`settings.override`, `settings`). Changing a note or a highlight
(drawn on the page), going to a bookmark or a note and what the page hides are `ScoreController`'s, which calls these.

## The state it owns

`EditsLayerState`. `MarksModel`: the marks (`MarksShelf`, `markCount`), the note being written (`noteDraft`), the
layers hidden, the piece's own settings, whether the phrases' marks are hidden (`phraseMarksHidden`, kept on the device,
`motiff.phraseMarks.hidden`; the old `motiff.phrases.hidden` is not read), the bookmarks as drawn and the one gone to. The rest: not recorded yet.

## Baseline entries that protect it

UX baseline: not recorded yet. Quality ratchet: `Bookmarks.swift` file_length.
