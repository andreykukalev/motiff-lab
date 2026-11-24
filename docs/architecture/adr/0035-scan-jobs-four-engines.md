# 0035. Scan jobs read by four engines and merged per staff

Status: accepted, 2026-11-21. Andrei decided it in the thread «Приблизить ноты с PDF к печати» (15:19–15:24 UTC): «давай реализовывать 4 движка и такую схему», the resilience at 15:21, and Transcoda's licence at 15:24. He started the work on 2026-11-24 (version 1.3, issue #768).

This record builds on:
- [0011](0011-sheet-music-recognition.md), [0014](0014-hosted-scan-server.md): the scan server, on the player's own computer and hosted;
- [0029](0029-vector-pdf-reading.md): born-digital PDF pages read on the device;
- [0030](0030-sources-model.md): Notes with Check flags.

It changes how a scanned page is read and how the app waits for it. Born-digital PDFs stay with the vector reader.

## Context

One engine reads a scanned page or a photo right in about four bars out of five. The research measured this on 340 bars of reference pages: homr 0.7 with our repair rules 270, Audiveris 5.11 270, Transcoda 2.1 267, copisteria 236.

The engines fail on different bars. Merging their readings per staff, taking the reading at least two engines agree on, gives 321 bars right, out of a ceiling of 323. The same agreement is an honest confidence:

| Bars | How many | Wrong |
| --- | --- | --- |
| three or four engines agree on every staff | 279 | 8 (three of them errors of the reference) |
| two agree | 41 | 3 |
| no two agree on some staff | 20 | 8 |

The report with the method and its scripts: `product/research/pdf-recognition-1to1-2026-11-21/report.md`, sections 8–11. The research files are kept outside the repository, because the reference pages are Andrei's own files.

Four engines need 60–90 s a page on a CPU, against about 25 s today. A request held open that long breaks:
- the hosted server scales to zero;
- Azure Container Apps ends a request after four minutes;
- the iPad suspends an app that is closed.

The player should start a scan, go on working, see real progress, and find the result after reopening the app.

## Decision

### 1. Four engines on the server

The scan server reads every page it gets with four engines, in parallel:
- homr, AGPL-3.0;
- copisteria, Apache-2.0;
- Audiveris 5.11, AGPL-3.0, a Java program run as published;
- Transcoda 2.1: its code is AGPL-3.0; its weights are CC BY-NC 4.0.

Transcoda's weights are non-commercial. Andrei accepted them for the MVP and the prototype, which are not sold. A commercial licence is asked from its authors before an App Store release. Until then a server setting (`ENGINES`) can switch Transcoda off, and the merge works with the engines that read.

The server only reads. It returns each engine's MusicXML for each page, and the page's evidence:
- staves and bar lines found by image processing;
- per bar and staff, the noteheads and other symbols copisteria's detector found, with their positions.

### 2. A job instead of a request

- `POST /jobs` takes the file, the pages to read and the engines. It answers `202` at once, with the job's id and the address of its status.
- `GET /jobs/{id}` gives the state of every unit: waiting, reading, read, retrying, or not read. It also gives the progress and an estimate of the time left.
- `GET /jobs/{id}/result` gives the readings and the evidence.
- `DELETE /jobs/{id}` removes the job.
- `GET /health` adds the measured seconds a page takes for each engine and the length of the queue, so the app can estimate before sending.

Job ids are random and unguessable. On the hosted server a job is bound to the signed-in player who made it.

`/recognise` stays as it is. The jobs are only added.

### 3. Work units, and resilience by the cloud patterns

The unit of work is one page read by one engine. A job of five pages is twenty units. Each unit is a message in a queue, taken by any free worker.

The patterns are those of the Azure Architecture Center:
- **Asynchronous request-reply:** the job and its status, as in section 2.
- **Queue-based load leveling and competing consumers:** units wait in the queue, and workers take them one at a time.
- **Claim check:** the file and the results lie in storage; the queue holds only references to them.
- **Retry:** a unit that fails or runs past its time limit is retried up to 3 times, after 10, 20 and 40 s. At its time limit the engine's process is stopped.
- **Leases:** a worker renews its lease on the message while it works. A message whose worker died returns to the queue by itself.
- **Poison queue:** after 3 failures the unit is set aside as «not read by this engine». The job goes on.
- **Circuit breaker:** an engine that fails on several pages of a job in a row is switched off for that job.
- **Idempotent results:** a unit's result is written under «job / page / engine», so a repeat overwrites rather than adds.
- **Supervisor:** once a minute it returns stuck units to the queue and closes jobs that can no longer finish.

On the player's own computer, the queue and the storage are in memory and in a folder on disk. On the hosted server they are an Azure Storage queue and blobs. The container app scales on the queue's length, so it stays awake while work is waiting and scales to zero after.

### 4. What is kept on the server

The file and the results stay until the app has fetched the result, and at most 24 hours. Then they are deleted, with the job.

Before this record, the server deleted the upload when the answer was sent. Nothing is logged about the music. Nothing is used for anything but reading the job.

### 5. The merge in music-core

The rules about notes live in music-core (CLAUDE.md), so the merge does too. `scan.join` gains an additive form: several readings per page with the page's evidence. For each reading and each bar:

1. The repair rules run on each reading: the shared notehead of two voices, the whole-bar rest, the bar lengths from the printed meters, and the rules already there. A rule is kept only where the bar then adds up.
2. For each staff of each bar, the reading that two or more engines agree on is taken.
3. Without such a reading, the one that agrees with music-core's own assembly of the bar from the detected symbols is taken.
4. Otherwise, the one that agrees best with the heads found on the page is taken.
5. Each bar gets its level: sure (three or more agree), probable (two agree) or Check (no two agree on some staff). Check bars become the Notes' Check flags (0030). The engines that gave each staff are recorded with the bar's provenance.

The merge is pure and deterministic: the same readings and evidence give the same score. No correction ever copies another bar of the piece. A repeat may differ by one note, and the player's music must not be overwritten (Andrei, 2026-11-21).

### 6. On the iPad

Before sending, the vector reader reads the PDF on the device (0029). The app then shows:
- how many pages it read itself;
- how many go to the server;
- an estimate from the server's `/health`.

For example: «4 страницы прочитаны на iPad. 2 страницы отправим на распознавание, около 3 минут».

While the job runs:
- the piece appears in the library at once, with the job's progress;
- the job's id is kept on the device, so after a relaunch the app asks for the status again;
- a page that no engine read is shown as not recognised, with «Повторить» for that page only. The rest of the piece opens.

A Check bar shows:
- the crop of that bar from the original page, from the evidence's bar bounds;
- two or three engines' variants, one tap each;
- the app's editing tools.

What the player chooses or changes is the player's correction, not recognition (0030).

## Consequences

- Scans are read right far more often: 321 of 340 reference bars, against 270 for the best single engine. The bars that remain doubtful are marked.
- A page costs more server time: about 360 vCPU-seconds, against about 50.
  - The hosted container needs 4 vCPU and 8 GiB, against 2 vCPU and 4 GiB.
  - The job store adds an Azure Storage account: queue and blobs, cents a month.
  - Billing stays per second of work, with scale to zero.
  - Andrei accepted this when he started the version.
- The image grows by about 2 GB: a Java runtime, Audiveris, Transcoda's model.
- The hosted server keeps a scan for up to 24 hours instead of none. The privacy text says so.
- The engines' licences are listed in the server's README. Transcoda's weights need the authors' commercial licence before the product is sold.
- The old request path stays until the jobs have replaced it on the iPad; then `/recognise` is removed in its own change.
