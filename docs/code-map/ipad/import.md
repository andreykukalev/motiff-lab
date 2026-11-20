# Code map: iPad `Import/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Import`.

## Files

`AddPiece`, `FolderFoundSheet`, `FolderImport`, `FolderPlan`, `FolderReview`, `FolderScan`, `ImportBanner`,
`ImportFolders`, `ImportJob`, `PartHearing`, `PhotoCapture`, `PhotoPages`, `PhotoReading`, `PhotoReadings`, `PhotoReview`,
`Recognition`, `RecognitionViews`, `Recording`, `RecordingPiece`, `Scan`, `ScanParts`, `VectorPages` (`.swift`).

- `PhotoReview.swift`: the photos of pages put in order and made one PDF piece (`PhotoPiece.make(pdf:pages:title:language:)`,
  opened as "photos", saved, closed); since v1.1 (finding 29) «Открыть файл» on Add piece also takes a JPEG or PNG
  (`PhotoPiece.images`, `isImage`), made the same piece of one page by `PhotoPiece.make(image:text:)`, titled by its file
  name, and opened as the project file it was saved to.
- `AddPiece.swift`: the ways in. «Из записи» (v1.1, finding 29) picks a recording and adds it as it is, free and on
  this iPad (`RecordingPiece.make`): nothing is heard or sent here, so the card has no Pro tag and no line on where the
  sound goes; that is said when it is recognised in Sources.
- `RecordingPiece.swift`: a new piece that is only the recording picked (`open` with "recording", "device": "ipad",
  its length; project file version 17): its sound kept by `OriginalSounds.keep`, saved by `ProjectFiles.saveNew` titled
  by the recording's name; no sound is kept for a piece not saved.
- `Recording.swift`: hearing a recording part by part (`RecordingHearing.hear` → `HeardRecording`, its `request`), for
  Notes of a recording kept in the piece (`SourcesController.hearFields` adds "tempo": "live") and a version from a
  recording (`performance.add`); `RecordingChooser.blocker`, what hearing needs first, said in Sources.

## The contract others may rely on

Not recorded yet.

## The state it owns

Not recorded yet.

## Baseline entries that protect it

UX baseline: `SCAN-01`. Quality ratchet: none.
