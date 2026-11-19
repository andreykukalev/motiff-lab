# Code map: iPad `Import/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Import`.

## Files

`AddPiece`, `FolderFoundSheet`, `FolderImport`, `FolderPlan`, `FolderReview`, `FolderScan`, `ImportBanner`,
`ImportFolders`, `ImportJob`, `PartHearing`, `PhotoCapture`, `PhotoPages`, `PhotoReading`, `PhotoReadings`, `PhotoReview`,
`Recognition`, `RecognitionViews`, `Recording`, `Scan`, `ScanParts`, `VectorPages` (`.swift`).

- `PhotoReview.swift`: the photos of pages put in order and made one PDF piece (`PhotoPiece.make(pdf:pages:title:language:)`,
  opened as "photos", saved, closed); since v1.1 (finding 29) «Открыть файл» on Add piece also takes a JPEG or PNG
  (`PhotoPiece.images`, `isImage`), made the same piece of one page by `PhotoPiece.make(image:text:)`, titled by its file
  name, and opened as the project file it was saved to.
- `Recording.swift`: a recording heard opens as a new piece with `HeardRecording.transcription` ("tempo": "live", no
  "starts", no "hint": there is no score yet); a version from a recording keeps `request` (`performance.add`).

## The contract others may rely on

Not recorded yet.

## The state it owns

Not recorded yet.

## Baseline entries that protect it

UX baseline: `SCAN-01`. Quality ratchet: none.
