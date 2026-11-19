# Code map: iPad `Sources/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Sources`.

## Files

`DeleteOriginalDialog`, `KeptCopies`, `Merge`, `MergeModel`, `OriginalSounds`, `Originals`, `OriginalsController`,
`OriginalsViews`, `PerformancePlayer`, `SourceLayers`, `SourcesController`, `SourcesDialogs`, `SourcesModel`,
`SourcesRecognise`, `SourcesRows`, `SourcesSheet`, `SourcesSwitch`, `Versions` (`.swift`).

- `DeleteOriginalDialog.swift`: the warning before an original is deleted, with its two danger styles (moved out of
  `SourcesSheet.swift` in v0.9).
- `SourceLayers.swift`: the sources by the layers of 0030 as the app shows them, built from `SourceLayersAnswer`
  (`init(_:)`, `PieceCore.sourceLayers`) or from the text of a sheet operation's answer (`init?(answer:)`); `groups` (each
  original with its Notes, which the sheet shows in one card, v0.9) and `looseNotes` (Notes of no original).
- `PerformancePlayer.swift` (v0.10 s-2, finding 24): `PerformanceCapsule`, the recording heard over the view shown
  (`SourcesController.performance`), placed under the bar of `ScoreScreen` and `PdfScreen`, for their own piece while
  the switch shows or for the piece under a screen shown over it (`heardOver`: a view shown in a cover, the PDF linked
  in Files; `PerformanceCapsule.sources(over:own:switchOn:)`); `RecordingViewCapsule` and `RecordingListening` (moved
  from `SourcesSwitch.swift`). The sound is `RecordingListening.shared` (`SourcesController.listening`): it counts the
  capsules showing it (`shown`, `hidden`) and stops at none, letting the file go (`end`), unless the screen handed the
  piece over (`handOver`, from `SourcesController.handOver`). `SourcesController.load` takes the recording handed
  (`takeHanded`) or stops it (not read, not shown, or the screen gone meanwhile); a screen that goes without handing
  over calls `SourcesController.leave`, which withdraws both hand-overs (`cancelHandOver`); a switch that never ran
  takes it back (`switchReturned`: the piece not in `ClosedPieces`, `handedAway` once it ran). Picking a view never
  closes it; ✕ does (`putAway`), silences it and withdraws its hand-over.
- `SourcesSheet.swift`: the Sources sheet (`SourcesSheet`, called by `Workspace/ScoreScreen` and `Pdf/Pdf`): its header,
  the add menu, the list of groups and loose Notes, picking files (`Picking`, `take`) and the camera; it owns the view state
  the rows share (`recognising`, `problems`, `deleting`, `deletingNotes`) and hands it to them as bindings and `SourcesRow`.
- `SourcesRows.swift` (v0.10 s-1): `SourcesRow` (what a row takes from the sheet: the view shown now, the scan server's
  address and whether it is hosted, the buttons' actions; since s-2 a row opens no view: icon, name, eye, ⋯),
  `SourcesGroupCard` (an original with its Notes), `SourcesOriginalRow` and `SourcesNotesRow` with their ⋯ menus,
  `SourcesRowHead`, `SourcesRowBackground`, `SourcesCaption`.
- `SourcesRecognise.swift` (v0.10 s-1): `SourcesRecogniseButton` («Распознать», Pro when hosted and always for a
  recording, s-3) and `SourcesRecognisePanel` (its `Offer`: what it needs first, where it goes, Send's word; a
  recording's from `RecordingChooser.blocker`), whose Send is `SourcesController.recognise`.
- `SourcesDialogs.swift` (v0.10 s-1): `SourcesDialogs`, the sheet's three dialogs as one modifier: deleting an original
  (`DeleteOriginalDialog`), deleting Notes, an original picked kept where it lies or copied.
- `SourcesController.swift`: `refresh` reads `sources.layers` through `PieceCore`; the sheet's operations
  (`sources.view.hide`, `.original.add`, `.original.remove`, `.notes.remove`, `.notes.recognise`) answer the same layers.
  Recognising an original is one path: `canRecognise` (the pages kept in the piece here, or a recording whose sound is
  here, on a piece with working notes), then `recognise` reads the original into a `Recognised` (its fields, the field
  dropped on a "badRequest", the toast): `readPages` for the pages, `hearRecording` for a recording
  (`OriginalSounds.file` → `RecordingHearing.hear`, Motiff Lab's server, `progress` meanwhile, s-3; its fields built by
  `hearFields`: "tempo": "live" and the working notes' metre and tempo as "hint", `hint` from `playback`, sent again
  without it when refused);
  `keepRecognised` keeps it under the call name `recognise` gives it as a literal (`sources.notes.recognise`,
  `sources.notes.hear`; the facade-calls contract test reads them there): layers, `fresh`, save, toast. The new Notes go
  under their original; the working notes change only by «Сделать рабочими».
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

`SourcesModel`: `versions` (`VersionShelf`), `status`, `page`, `engraved` and the piece's sources (`info`,
`SourcesInfo`, since S7c). `MergeModel`: `draft`, `at`, `phrase`, `view`, `hearing`, `comparing`, `toast`, `markerPick`.
Both are set only through their methods; the views observe them (Bar | Phrase and Compare versions bind
`scope(phrase:)` and `compare(_:)`): [ADR 0031](../../architecture/adr/0031-ipad-score-modules.md), the notes of S4 to
S7c. `SourcesController`: the layers of 0030.

## Baseline entries that protect it

UX baseline: `VIEW-07`, a recording kept as an original until deleted (`Baseline/SourcesBaselineTests`). Tests:
`MotiffLabTests/OriginalsTests` (`addWord`, `canRecognise`). Quality ratchet:
`Versions.swift` file_length.
