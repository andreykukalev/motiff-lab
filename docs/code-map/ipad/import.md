# Code map: iPad `Import/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Import`.

## Files

`AddPiece`, `FolderFoundSheet`, `FolderImport`, `FolderPlan`, `FolderReview`, `FolderScan`, `ImportBanner`,
`ImportFolders`, `ImportJob`, `PartHearing`, `PhotoCapture`, `PhotoPages`, `PhotoReading`, `PhotoReadings`, `PhotoReview`,
`Recognition`, `RecognitionViews`, `Recording`, `RecordingPiece`, `Scan`, `ScanCrops`, `ScanJobs`, `ScanMarkedPages`, `ScanPageAgain`, `ScanParts`, `VectorPages` (`.swift`).

- `ScanJobs.swift` (v1.3, the engine choice «Четыре движка», `ScanServer.allEngines`, the default since the 1.3 hotfix; decision record 0035 section 6): `ScanHealth` (`/health`'s
  seconds a page and queue, the estimate), `ScanJobPlan` (the pages the vector reader read here and those for the server,
  the line before sending), `ScanJobStatus`, `ScanJobRecord`; `ScanServer.takesJobs` (`/health` before a job, waited for up
  to `wakeSeconds`, 120 s, as the hosted server wakes from zero; «Сервер распознавания просыпается…»
  after `ScanJobs.wakingAfter`, `ScanJobStatus.beforeSending`, "sending" once it answers; no answer is the problem, not a fall back to `/recognise`); `ScanServer.readByJob` (submit, ask every 2.5 s with a
  pause doubling to a minute after a miss: no answer, a 5xx or a 429; a 404 is a job gone; fetch once `ready`, merge by
  `scan.join` with readings and evidence, engines in `ScanJobs.engineOrder`, each page's report passed on), `submitJob`,
  `deleteJob`. Calls go through `ScanTarget` (`Scan.swift`), which signs for Motiff Lab's server and carries the
  `URLSession` (a stub's in `ScanJobsTests`). `ScanServer.recognise` uses a job with «Четыре движка» when the server takes jobs, else
  «Авто»'s way (`choseAuto`). The batch (`Recognition.swift`) keeps a piece's job in `recognition.json` (`RecognitionItem.job`,
  device state, not the project file), asks again after a relaunch for those pieces only (`restoreAndFollowJobs`; the
  others wait for «Продолжить»), fetches only once the piece is not open on screen,
  deletes the job once the notes are written, and keeps the pages no engine read (`unread`); the banner shows the job's
  pages and engines on a tap (`ScanJobDetail`) and «Повторить» for pieces that did not work.

- One recognition at a time and the open PDF in the background (1.3 hotfix, Andrei 2026-11-25): `RecognitionQueue.busy`
  (the unfinished batch's piece; while there is one, the PDF's `ReadNotesPanel(busy:)`, Sources' panel and the library's
  picking start none); «Распознать ноты» on an open PDF with «Четыре движка» saves the piece and hands it to the queue
  (`append`), which may send the open piece (`next(jobs:)`) and waits for the screen only before fetching
  (`RecognitionJob.ready`), then writes the notes into the file as it is then (the piece opened again when the screen
  saved it meanwhile). `BackgroundRecognitionLine` (`RecognitionViews.swift`) under the PDF's bar says how far it is and,
  once the job is done, «Ноты готовы» with «Открыть ноты»: the screen saves, `released(_:)` lets the queue write,
  `finished(_:)` waits for it, and the piece opens on its notes. The banner names a batch of one piece. The PDF and
  Sources read all this as `Platform/BackgroundReading`, which `Workspace/PieceScreen` builds from the queue.
- `ScanPageAgain.swift` (v1.3 i-1b): «Повторить» on a piece's «стр. N не распознана» (`RecognitionTag`,
  `RecognitionQueue.retryUnread`): only those pages go as a new job (`ScanServer.readJob`, followed and resumed as any
  job), each read page put into the same notes at its place by `scan.insert` (`place(of:read:)`: after the PDF pages the
  notes have before it, kept as `RecognitionItem.read` at the first read; the file read again once the job is done), the bars to check carried to the new fingerprint (`keepReview`'s "was"); the file written
  (`RecognitionQueue.recognise(again:)`), then the job deleted; a page again unread keeps the line with «Повторить» and the reason (`RecognitionItem.reason`, «Не вставлено: …»).
- `ScanMarkedPages.swift` (v1.6 c-14b3): the pages of a PDF's notes with marks «что не так», and only those, read again
  for `scan.join`'s "replace": a page the vector reader reads stays on this iPad unless a bar of it is marked as read
  badly (`split`, E-scan-19); the others go as one job (`ScanServer.readJob`, the target asked only then) with their
  "bad" bars; a marked page no engine read fails the whole reading (E-scan-18). Run by `Workspace/ScoreMarkedReading`.

- v1.5 i-7 (the v1.4 plan's answer 9): a page no engine read is marked in the notes (`Workspace/ScoreUnreadPages.swift`:
  `UnreadPage`, from `RecognitionItem.read` and `check`'s "pageStarts", drawn with the Check flags) and listed in Check
  with «Показать» and «Повторить» (`UnreadPagesCheck`, `UnreadPagesRetry` in `ScanPageAgain.swift`); the server's estimate before sending
  in the batch's consent (`ScanHealth.seconds(pieces:)`) and in Sources (`SourcesController.jobPlanLine`); photos of
  pages with «Четыре движка» go to the background recognition as a PDF does (`Workspace/PdfScreenReading.swift`), the job's page reports kept
  with the notes; the progress «страница 3 из 5 · около 1 мин 40 с» (`ScanProgress.secondsLeft`, `about(seconds:)`,
  `RecognitionTop.jobLine`).
- v1.5 r-2b: the batch writes a piece's notes only by `RecognitionQueue.write`, never into a piece deleted meanwhile
  (E-scan-8); `ScanProblem` says every problem in the interface's language (`.slow` for a server that answered late); a
  folder import names the piece's file by its title as music-core gives it (`FolderPlanning.confirm`'s title).
- `ScanCrops.swift` (v1.3 i-2, 0035 section 6): `ScanCrop` (a bar's evidence box on the server's turned image as a rect on the PDF page, and its turn), `ScanCrops.keepReview`, run by `readByJob` after `scan.join`: the Check bars' variants and crops cut from the PDF kept on this iPad (`ScanReviewStore`); nothing is sent. Tests: `ScanReviewTests`.

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
- `Recording.swift`: the sound read on the iPad a block at a time (`RecordingSound.read`, and `levels(of:)`, which keeps
  only the levels, for the waveform); hearing a recording kept in the piece part by part (`RecordingHearing.hear` →
  `HeardRecording`, its `request`) for its Notes (`SourcesController.hearFields` adds "tempo": "live");
  `RecordingChooser.blocker`, what hearing needs first, said in Sources.

## The contract others may rely on

Not recorded yet.

## The state it owns

Not recorded yet.

## Baseline entries that protect it

UX baseline: `SCAN-01`. Quality ratchet: none.
