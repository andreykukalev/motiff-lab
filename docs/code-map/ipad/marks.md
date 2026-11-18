# Code map: iPad `Marks/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Marks`.

## Files

`Bookmarks`, `Check`, `EditsLayer`, `Marks`, `SegmentedChoice` (`.swift`).

- `SegmentedChoice.swift`: the choice of two or three (ds.css `.seg`: `SegmentedChoice`, `SegmentFill`,
  `SegmentedCapsule`), which other folders use too; out of `Bookmarks.swift` in S7b.

- `Check.swift`: `CheckShelf` (`check`), the Check drawer, and Fix notes: `NoteCorrections`, a bar's notes built from
  `CorrectionsAnswer` (`PieceCore.corrections`), listed by `CorrectionsList`.
- `EditsLayer.swift` (v0.6.5, «Мои правки»): `EditsLayerState`, built from `EditsLayerAnswer` (every corrections
  answer, and the open answer's "edits": "count", "layer", "moved", "unfit"), `EditsLayerSection` in More (the switch,
  its line, the misfits with «Убрать»). `OpenPiece.openRequest` opens a project file with "edits": "piece" (the file keeps "off");
  `ScoreController.switchEdits` / `dropUnfitEdits` (`corrections.layer` / `.drop`) engrave again and save at once
  (`editsKept`).

## The contract others may rely on

While «Мои правки» is off, `editsOn` is false: no Fix notes, no note editor, no draft Accept (music-core refuses them
too). `NoteCorrections` and `EditsLayerState` hold only what music-core answered
(`tests/contracts/facade-answers/corrections*.json`, decoded by `FacadeAnswerFixturesTests`). The rest: not recorded yet.

## The state it owns

`EditsLayerState`. The rest: not recorded yet.

## Baseline entries that protect it

UX baseline: not recorded yet. Quality ratchet: `Marks.swift` jscpd (same file); `Bookmarks.swift` file_length.
