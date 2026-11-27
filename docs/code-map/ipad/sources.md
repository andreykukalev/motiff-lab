# Code map: iPad `Sources/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Sources`. Cut by model, controller and views
(W2.12, ADR 0031, v1.6 i-13); the sheet that composes them is `Workspace/SourcesSheet`
([`workspace-screen.md`](workspace-screen.md)).

## Model

- `SourceLayers.swift`: the sources by the layers of 0030, built from `SourceLayersAnswer` (`PieceCore.sourceLayers`)
  or a sheet operation's answer (`init?(answer:)`); `groups` (an original with its Notes, one card) and `looseNotes`.
- `SourcesModel.swift` (S4): the score's versions (`versions`, `VersionShelf`), `status`, `markerPick`, `engraved` (even
  bars, the bars the app wrote: `Engraved.note`) and the piece's `info` (`SourcesInfo`). Draws the versions and acts on
  them (show, compare, add, rename, a marker's «Как в версии» and «Источники») through `SourcesScore`, which the score's
  root adopts. Not joined with the controller: it lives only with the score.
- `Versions.swift`: `VersionShelf`, `VersionBar`, `SourcesInfo`, `VersionFiles`, `MarkerPopover`. A version picked opens
  Sources and is compared (`SourcesModel.compareVersion`); the bar-by-bar merge left in v1.1 (ADR 0034 section 8).

## Controller

- `SourcesController.swift`: the layers of the piece on its screen (score, PDF, recording) and the sheet's operations,
  each saved at once and answered as the layers (`change`: why not in `status`; `refusedInWords` for "unavailable"):
  hide a view, add pages or a recording, remove an original or Notes (Undo writes the file back), «Сделать рабочими»
  (E-sheet-1), `undo`, a picked file read (`read(picked:)`, E-sources-2), another PDF or Notes opened only to be shown
  (`openOriginal`, `openNotes`, `engraveShown`). Recognising is one path, one original at a time (E-sources-1):
  `canRecognise`, `recognise` through `setReading` (`reading`, the one owner of which original is with the scan server,
  held also by `Workspace/ScoreMarkedReading`'s «Распознать заново с отметками», v1.6 c-14b3), then `keepRecognised`
  under the literal call name (`sources.notes.recognise`, `.notes.hear`, read by the facade-calls contract test; with
  `keptPdf`, also the marked reading's). The first notes of a piece with none, the first pages of a
  piece that is only its recording, and a removal that takes a version away open the piece again in place (`tell`,
  `reopen`, the toast handed over). The recording heard (`performance`) is handed to the next screen of the file
  (`handOver`, `takeHanded`, `leave`, `switchReturned`, `handedAway`, `putAway`).
- `SourcesRecognising.swift`: the readers of `recognise`, which write no state: `readPages` (the scan server),
  `hearRecording` (progress told back), `hearFields`, `gridHint`, `hint`, `jobPlanLine`, `readingOther`; `Recognised`,
  `Refusal`, `noScanServer`, `toastText` (music-core's lines of the corrections carried, "carried", and of the marks
  «что не так» used, "marked", v1.6 c-14b3).
- `SourcesHandover.swift`: `SourcesToast` and `SourcesHandover`, what the next screen of the same file is handed (the
  toast, the view, the switch, the recording).
- `SourcesDrafts.swift` (ADR 0034): the drafts' steps on the controller (`addToDraft`, `createWithRecording`,
  `findNewOnRecording`, rename, remove, open, the places' and seams' steps undone by `drafts.undo`); one at a time
  (`stepping`, written only there); collage into another file's draft (`addedElsewhere`).
- `OriginalsController.swift`, `Originals.swift` (`OriginalEntry`, `OriginalBadge`, `OriginalAway`, `OriginalLink`),
  `KeptCopies.swift`: where an original's bytes are, a link to it in Files checked by size and date, and «Привязать
  оригинал» (`sources.relink`, `sources.held`).
- `RecordingListening.swift`: the recording's sound (`.shared`): counts the capsules showing it and stops at none unless
  handed over; a call, the background or the headphones gone pause it (`EngineKeeper`).

## Views

- `SourcesRows.swift`: `SourcesRow` (what the sheet hands the rows: the view shown, the scan server, the actions,
  `Picking`), `SourcesGroupCard`, `SourcesOriginalRow`, `SourcesNotesRow`, `SourcesDraftRow`, `SourcesCaption`.
- `SourcesRecognise.swift`: `SourcesRecogniseButton` and `SourcesRecognisePanel` (`Offer`: what it needs, where it
  goes, Send; `pagesOffer`, the words for pages sent, also Check's marked reading's; `hearLine`, `timedBy`), Send →
  `SourcesController.recognise`.
- `SourcesDialogs.swift` (`SourcesDialogs`, `DraftDialogs`), `DeleteOriginalDialog.swift`, `OriginalsViews.swift`
  (`OriginalPlaceLabel`).
- `SourcesSwitch.swift`: the switch of views in the bar (`SourcesViewSwitch`, `SourcesViewMenu`), `SourcesButton`,
  `SourcesToastView`, `UndoToast`.
- `PerformancePlayer.swift`: `PerformanceCapsule` (the recording heard over the view shown, `heardOver`) and
  `RecordingViewCapsule`.
- Drafts: `DraftSheet.swift` (`DraftChoice`, the «В черновик» sheet, `DraftElsewhereToast`), `DraftStructure.swift`
  (`DraftRecordingOffer`, `DraftLengthChoice`), `DraftTechniques`, `DraftSeams`, `DraftAnchors` (the draft view's
  models), `OtherPieceDrafts.swift`.

## The contract others may rely on

- `SourceLayers` and `VersionShelf` hold only what music-core answered
  (`tests/contracts/facade-answers/sources.layers.json`, `performance.taken.json`, decoded by
  `FacadeAnswerFixturesTests`); every request of the sheet carries `"device": "ipad"` and `"here"`.

## The state it owns

`SourcesModel`: its five values, set only by its methods. `SourcesController`: the layers, `status`, `toast`,
`reading` and `progress` (only `recognise`), `fresh`, `performance`, `stepping`. The views observe them.

## Baseline entries that protect it

`VIEW-07`, a recording kept as an original until deleted (`Baseline/SourcesBaselineTests`); `VIEW-06`, a version's marker
opens Sources (`Baseline/WorkspaceScenarioTests`); the drafts' (`DraftsBaselineTests`, `Drafts12BaselineTests`). Tests:
`SourcesModelTests`, `OriginalsTests`, `DraftsTests`; the baseline adds a version through `SourcesBaselineNames`.
