# Code map: iPad `Marks/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Marks`.

## Files

`Bookmarks`, `Check`, `EditsLayer`, `Marks` (`.swift`).

- `EditsLayer.swift` (v0.6.5, «Мои правки»): `EditsLayerState` (the open answer's "edits" and every corrections
  answer: "count", "layer", "moved", "unfit"), `EditsLayerSection` in More (the switch, its line, the misfits with
  «Убрать»). `OpenPiece.openRequest` opens a project file with "edits": "piece" (the file keeps "off");
  `ScoreController.switchEdits` / `dropUnfitEdits` (`corrections.layer` / `.drop`) engrave again and save at once
  (`editsKept`).

## The contract others may rely on

While «Мои правки» is off, `editsOn` is false: no Fix notes, no note editor, no draft Accept (music-core refuses them
too). The rest: not recorded yet.

## The state it owns

`EditsLayerState`. The rest: not recorded yet.

## Baseline entries that protect it

UX baseline: not recorded yet. Quality ratchet: `Marks.swift` jscpd (same file); `Bookmarks.swift` file_length.
