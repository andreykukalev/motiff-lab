# Code map: iPad `Sources/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Sources`.

## Files

`KeptCopies`, `Merge`, `MergeModel`, `OriginalSounds`, `Originals`, `OriginalsController`, `OriginalsViews`, `SourceLayers`,
`SourcesController`, `SourcesModel`, `SourcesSheet`, `SourcesSwitch`, `Versions` (`.swift`).

- `SourceLayers.swift`: the sources by the layers of 0030 as the app shows them, built from `SourceLayersAnswer`
  (`init(_:)`, `PieceCore.sourceLayers`) or from the text of a sheet operation's answer (`init?(answer:)`).
- `SourcesController.swift`: `refresh` reads `sources.layers` through `PieceCore`; the sheet's operations
  (`sources.view.hide`, `.original.add`, `.original.remove`, `.notes.remove`, `.notes.recognise`) answer the same layers.
- `MergeModel.swift` (S4b, ADR 0031 section 3): the merge draft's state and its rules (the bars a choice goes for, the
  span heard, ‹ ›, the bars Compare versions shows, one `performance.merge` call). Tests: `MotiffLabTests/MergeDraftTests`.
- `SourcesModel.swift` (S4, ADR 0031 section 3): the versions, their words and page, the score as last engraved; reads a
  version and the list through music-core. Tests: `MotiffLabTests/SourcesModelTests`.
- `Versions.swift`: `MergeInfo` (the draft, from `MergeAnswer`: `PieceCore.merge`), `VersionShelf` (the versions and the
  one compared, from `PerformanceAnswer`: `PieceCore.mergeApply` and `.mergeUndoApplied`, or from the text of the version
  calls not yet in the port, `read(performance:)`, `read(list:)`), `VersionEntry` (`init?(_: VersionHead)`), the views.

## The contract others may rely on

- `SourceLayers` holds only what music-core answered (`tests/contracts/facade-answers/sources.layers.json`, decoded by
  `FacadeAnswerFixturesTests`); every request of the sheet carries `"device": "ipad"` and `"here"`.
- `MergeInfo` and `VersionShelf` likewise (`performance.merge.json`); the draft's calls are made only by `MergeModel.call`
  and `ScoreController`'s `applyDraft`, `unapply` and `hearMerge`, through `PieceCore`.

## The state it owns

`SourcesModel`: `versions` (`VersionShelf`), `status`, `page` and `engraved`. `MergeModel`: `draft`, `at`, `phrase`,
`view`, `hearing`, `comparing`, `toast`, `markerPick`. Both are set only through their methods; the views observe
them (Bar | Phrase and Compare versions bind `scope(phrase:)` and `compare(_:)`), and `ScoreController` keeps `sources`
(`SourcesInfo`) as [ADR 0031](../../architecture/adr/0031-ipad-score-modules.md), the notes of S4, S4b and S7a, say. `SourcesController`: the
layers of 0030.

## Baseline entries that protect it

UX baseline: not recorded yet. Quality ratchet: `Versions.swift` file_length.
