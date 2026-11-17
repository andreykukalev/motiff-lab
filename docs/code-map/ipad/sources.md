# Code map: iPad `Sources/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Sources`.

## Files

`KeptCopies`, `Merge`, `OriginalSounds`, `Originals`, `OriginalsController`, `OriginalsViews`, `SourceLayers`,
`SourcesController`, `SourcesSheet`, `SourcesSwitch`, `Versions` (`.swift`).

- `SourceLayers.swift`: the sources by the layers of 0030 as the app shows them, built from `SourceLayersAnswer`
  (`init(_:)`, `PieceCore.sourceLayers`) or from the text of a sheet operation's answer (`init?(answer:)`).
- `SourcesController.swift`: `refresh` reads `sources.layers` through `PieceCore`; the sheet's operations
  (`sources.view.hide`, `.original.add`, `.original.remove`, `.notes.remove`, `.notes.recognise`) answer the same layers.

## The contract others may rely on

- `SourceLayers` holds only what music-core answered (`tests/contracts/facade-answers/sources.layers.json`, decoded by
  `FacadeAnswerFixturesTests`); every request of the sheet carries `"device": "ipad"` and `"here"`.

## The state it owns

Not recorded yet.

## Baseline entries that protect it

UX baseline: not recorded yet. Quality ratchet: `Versions.swift` file_length.
