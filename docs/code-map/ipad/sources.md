# Code map: iPad `Sources/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Sources`.

## Files

`DeleteOriginalDialog`, `DraftSheet`, `DraftTechniques`, `KeptCopies`, `Originals`, `OriginalsController`,
`OriginalsViews`, `PerformancePlayer`, `SourceLayers`, `SourcesController`, `SourcesDialogs`, `SourcesDrafts`, `SourcesModel`,
`SourcesRecognise`, `SourcesRows`, `SourcesSheet`, `SourcesSwitch`, `Versions` (`.swift`).

- Drafts (v1.1, ADR 0034 step 5): `SourceLayers.drafts` and the view of kind "draft" (its `name` is its word in the
  switch); `SourcesDrafts.swift` (`addToDraft` on the piece the notes are shown on, `renameDraft`, `removeDraft` with
  Undo, `openDraft` by `drafts.open` through `openShown`); `DraftSheet.swift`: `DraftChoice` (what, into, how, the
  same music first; `drafts.add`'s fields) and the «В черновик» sheet; the group «Черновики» is `SourcesDraftRow`
  (`SourcesRows.swift`) and `DraftDialogs`. Step 6: `DraftTechniquesModel` (`DraftTechniques.swift`, a draft view's
  приёмы); `take`, `leave`, `takeHand`, undone by `drafts.undo` (`.draftStep`). No «расширить» yet.

- `DeleteOriginalDialog.swift`: the warning before an original is deleted, with its two danger styles (moved out of
  `SourcesSheet.swift` in v0.9).
- `SourceLayers.swift`: the sources by the layers of 0030 as the app shows them, built from `SourceLayersAnswer`
  (`init(_:)`, `PieceCore.sourceLayers`) or from the text of a sheet operation's answer (`init?(answer:)`); `groups` (each
  original with its Notes, which the sheet shows in one card, v0.9) and `looseNotes` (Notes of no original).
- `PerformancePlayer.swift` (v0.10 s-2, finding 24): `PerformanceCapsule`, the recording heard over the view shown
  (`SourcesController.performance`) under the bar of `ScoreScreen` and `PdfScreen`, or of a screen over the piece
  (`heardOver`, also the drafts' owner there); `RecordingViewCapsule` (✕ only with `close`), `RecordingListening`:
  `.shared` counts the capsules showing it and stops at none unless handed over (`handOver`, `takeHanded`, `leave`,
  `switchReturned`, `handedAway`); ✕ (`putAway`) silences and withdraws it; picking a view never closes it; `playStretch`.
- `SourcesSheet.swift` (from `Workspace/ScoreScreen`, `Pdf/Pdf`): header, add menu, groups and loose Notes, picking files
  (`Picking`, `take`), camera; owns the rows' view state (`recognising`, `problems`, `deleting`, `deletingNotes`,
  `renaming`), handed down as bindings and `SourcesRow`.
- `SourcesRows.swift` (v0.10 s-1): `SourcesRow` (from the sheet: the view shown, the scan server and whether hosted, the
  actions; a row opens no view since s-2), `SourcesGroupCard`, `SourcesOriginalRow` and `SourcesNotesRow` with their ⋯
  (a version's «Переименовать…», `rename`, only with a score: `ScoreController.renameVersion`), `SourcesRowHead`,
  `SourcesRowBackground`, `SourcesCaption`.
- `SourcesRecognise.swift` (v0.10 s-1): `SourcesRecogniseButton` («Распознать», Pro when hosted or a recording) and
  `SourcesRecognisePanel` (`Offer`: what it needs, where it goes, Send's word; `RecordingChooser.blocker`; `hearLine`:
  beside the working notes, or the piece's own notes when it has none, v1.1), Send →
  `SourcesController.recognise`.
- `SourcesDialogs.swift` (v0.10 s-1): the sheet's dialogs in one modifier: deleting an original (`DeleteOriginalDialog`)
  or Notes, a version's new name (v1.1), an original picked kept where it lies or copied.
- `SourcesController.swift`: `refresh` reads `sources.layers` through `PieceCore`; the sheet's operations
  (`sources.view.hide`, `.original.add`, `.original.remove`, `.notes.remove`, `.notes.recognise`) answer the same layers.
  Recognising is one path: `canRecognise` (pages kept here with working notes, or a recording's sound here, also on a
  piece with no notes yet, v1.1), then
  `recognise` builds a `Recognised` (fields, the field dropped on "badRequest", toast): `readPages`, or `hearRecording`
  (`RecordingHearing.hear`; `hearFields`: "tempo": "live" and the "hint" of `gridHint`, none without working notes,
  dropped when refused); `keepRecognised` keeps it under the literal
  call name (`sources.notes.recognise`, `sources.notes.hear`, read by the facade-calls contract test): layers,
  `fresh`, save, toast; the first notes of a piece that had none are its working notes, and the piece opens again on
  them; the first pages added to a piece that is only its recording open it again as a PDF piece (`tell`: the toast here,
  or handed over with `reopen`). New Notes go under their original; the working notes change only by «Сделать рабочими». A
  removal that takes a version away (`removed`) reopens the piece from the file just saved (`reopen`, toast handed
  over): music-core's open notes still hold its taken bars, the score its markers (v1.1).
- `SourcesModel.swift` (S4, ADR 0031): the versions, their words, a marker tapped (`markerPick`), the score as last
  engraved; reads and renames versions (`performance.rename`). Tests: `SourcesModelTests`.
- `Versions.swift`: `VersionShelf` (the versions and the one compared: `read(performance:)`, `read(list:)`),
  `VersionEntry`, `VersionBar`, `SourcesInfo`, `VersionFiles`, `MarkerPopover` (a marker tapped: «▶ Как в версии»,
  «Источники»). The bar-by-bar merge (0022 section 5) left in v1.1 (ADR 0034 section 8): a version picked opens Sources
  and is compared (`ScoreController.compareVersion`); files with taken bars read as before (music-core).

## The contract others may rely on

- `SourceLayers` holds only what music-core answered (`tests/contracts/facade-answers/sources.layers.json`, decoded by
  `FacadeAnswerFixturesTests`); every request of the sheet carries `"device": "ipad"` and `"here"`.
- `VersionShelf` likewise (taken bars: `performance.taken.json`).

## The state it owns

`SourcesModel`: `versions` (`VersionShelf`), `status`, `markerPick`, `engraved` and the piece's sources (`info`,
`SourcesInfo`, since S7c), set only through its methods; the views observe it:
[ADR 0031](../../architecture/adr/0031-ipad-score-modules.md), the notes of S4 to S7c. `SourcesController`: the layers of 0030.

## Baseline entries that protect it

UX baseline: `VIEW-07`, a recording kept as an original until deleted (`Baseline/SourcesBaselineTests`); `VIEW-06`, a
version's marker opens Sources (`Baseline/WorkspaceScenarioTests`). Tests: `MotiffLabTests/OriginalsTests` (`addWord`,
`canRecognise`).
