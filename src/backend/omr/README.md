# Motiff Lab scan server

Reads a PDF or a photo of printed music and returns MusicXML, for Motiff Lab's **Scan printed music** (decision record [0011](../../../docs/architecture/adr/0011-sheet-music-recognition.md)). It runs [homr](https://github.com/liebharc/homr), an open-source optical music recognition engine, unmodified, on your own computer. A second engine, [copisteria](https://github.com/kobimusic/copisteria) with its copista-28m model, reads the same pages when the app asks for it (the experiment «Выбор движка распознавания» in Settings · Experimental), so the two can be compared on your own pages; unlike homr, it reads the printed time signature.

It also transcribes a recording, audio or video, into MIDI notes with [Transkun](https://github.com/Yujia-Yan/Skipping-The-Frame-Level) (decision record [0014](../../../docs/architecture/adr/0014-hosted-scan-server.md)), a piano transcription model that also hears the sustain pedal. On a CPU it takes about a second of two cores for a second of sound, so it hears the first 2½ minutes of a recording. The notes are still a draft for the player to check.

Version 1.3 reads every page of a scan job with four engines, in parallel, in the background (decision record [0035](../../../docs/architecture/adr/0035-scan-jobs-four-engines.md)): homr, copisteria, [Audiveris](https://github.com/Audiveris/audiveris) 5.11 and [Transcoda](https://github.com/btrkeks/transcoda) 2.1 ([Scan jobs](#scan-jobs), and `/jobs` in the API below). The server only reads: it returns each engine's MusicXML and the page's evidence, and the app's music-core merges them.

## Run it

You need [Docker Desktop](https://www.docker.com/products/docker-desktop/) (Windows or macOS) or Docker on Linux. In a copy of this repository:

```bash
docker build -t motiff-omr src/backend/omr
docker run --rm -p 8765:8000 motiff-omr
```

The build takes several minutes and downloads about 4 GB once, including the engines' models and Audiveris's Java runtime (the image is about 7 GB); after that the server works offline. Then in Motiff Lab open **Settings → Sheet music scanning**, keep the address `http://localhost:8765`, and choose **Save and check**. **Import** now has a **Scan printed music** drop zone.

To keep it running in the background, start it with `docker run -d --restart unless-stopped -p 8765:8000 --name motiff-omr motiff-omr`, and stop it with `docker stop motiff-omr`.

A page takes 20 to 60 seconds, depending on the computer. A PDF can have up to 8 pages (`-e MAX_PAGES=12` raises that).

### Scan jobs

`POST /jobs` reads the pages in the background instead of while the app waits (decision record [0035](../../../docs/architecture/adr/0035-scan-jobs-four-engines.md)): each page is read by every engine the server has, and the app asks how far it got. The unit of work is one page read by one engine, a message in a queue that a worker takes. A unit that fails, or runs past its time limit (5 minutes; its engine is stopped then), is tried again after 10, 20 and 40 seconds; after the last try that page is not read by that engine and the job goes on with the others. An engine that fails on 3 pages of a job in a row is switched off for the rest of that job. A worker renews its lease on a unit while it reads it, so a unit whose worker died goes back to the queue. Once a minute a supervisor gives a unit whose message was lost a new one, closes a job that has not finished in 3 hours with what was read, and deletes what has expired (below). The progress and the estimate weigh each unit by its engine's seconds per page, measured on this server, which `/health` shows too.

The jobs lie in a folder, `/tmp/motiff-scan-jobs` in the container (`-e JOBS_FOLDER=…` moves it), so a server that is restarted (`docker restart motiff-omr`, or `--restart unless-stopped` after a crash) goes on with the jobs it had; `docker run --rm` starts with none. As many units are read at once as the server has engines, but no more than it has cores, so a page's engines read it side by side; each engine then uses its share of the cores (4 cores, 4 engines: one each), so none of them takes every core. On a 2-core computer two units are read at a time, so no engine is pushed past a unit's 5 minutes. `-e JOB_WORKERS=1` reads one unit at a time, with every core, for a computer with little memory. On 4 cores a page takes about 2½ minutes with all four side by side (measured: homr 73 s, copisteria 90 s, Audiveris 69 s, Transcoda 141 s), well inside a unit's 5 minutes.

`-e ENGINES=homr,copisteria,audiveris` names the engines the server reads with (homr always); without it, it reads with all it has. A name it does not know stops the server at start, with a message naming the engines it has. Leave `transcoda` out where its non-commercial weights may not be used (see [Licence](#licence)).

#### The page's evidence

Each page of a job's result also carries its evidence, what music-core checks the readings against: the staves and bar lines found on the page image by image processing, and the symbols copisteria's detector found there, each in its system, bar and staff. A page copisteria did not read has none, and so has a page whose evidence could not be made: its readings are kept. `/recognise` makes none.

```jsonc
{ "evidence": 1, "page": 1,
  "detector": { "name": "copisteria", "version": "1.3.4" },
  "image": { "width": 2481, "height": 3508, "dpi": 300, "rotated": 0, "deskewed": 0 },
  "systems": [ { "box": [294.0, 463.5, 2362.0, 738.5],
      "staves": [ { "staff": 1, "lines": [463.5, 484.5, 505.5, 526.0, 546.5], "space": 20.8,
          "barLines": [ { "x": 587.5, "y": [463.0, 739.0] } ] } ],
      "bars": [ { "x": [294.0, 587.5] } ] } ],
  "symbols": [ { "system": 0, "bar": 0, "staff": 1, "class": "noteheadBlack", "duration": "eighth", "position": -1,
      "dots": 0, "grace": false, "x": 512.0, "y": 536.5, "box": [503.0, 531.0, 521.0, 542.0], "confidence": 0.99 } ] }
```

| Field | What it is |
| --- | --- |
| `image` | the page image every coordinate is in: pixels from the top left, after the page was turned the right way up (`rotated`, degrees clockwise); the server never deskews, so `deskewed` is 0; `dpi` for a PDF page, `null` for a photo |
| `systems` | top down; a system's staves are those joined by a line at their left end; `box` spans its staff lines |
| `staves` | numbered from 1 top down within the system, as in a MusicXML part: the five lines' rows, the staff space, and each of the system's bar lines with how far it runs up and down through this staff |
| `bars` | from the system's left end to its first bar line, then between bar lines, left to right; numbered from 0 within the system |
| `symbols` | noteheads (with the note's `duration` as a MusicXML type, its staff `position`, 0 the middle line and 1 a step up, `dots` and `grace`), rests, accidentals, augmentation dots, tuplet numbers and clefs (with their `line`) by their [SMuFL](https://www.w3.org/2021/03/smufl14/) glyph names; key and time signatures, which the detector finds whole, as `keySignature` (`fifths`) and `timeSignature` (`beats`, `beatType`); a whole-bar rest is `restWhole` with `"measure": true`; each in the bar its centre falls in and the nearest staff (at most 7 staff spaces away); `confidence` is the detector's |

## Hosted

The same image also runs in Azure Container Apps for the iOS app and for players without Docker (decision record [0014](../../../docs/architecture/adr/0014-hosted-scan-server.md)). The `Scan server` workflow publishes it as `ghcr.io/andreykukalev/motiff-omr` and deploys it with [`infra/scan.bicep`](../../../infra/scan.bicep) on each change here on `main`. There it takes PDFs of up to 4 pages and recordings as on your own computer, answers browsers only from the web app and its previews (`ALLOWED_ORIGINS`, `ALLOWED_ORIGIN_REGEX`), and scales to zero when idle. With `AUTH_AUTHORITY` and `AUTH_AUDIENCE` set, it reads a page or a recording only for a player signed in through Microsoft Entra External ID whose token carries the `Scan` scope, and answers `401` otherwise; `/health` says so with `"signIn": true`. Unset, as on your own computer, nothing is checked.

Hosted, each copy has 4 vCPU and 8 GiB, so a page's four engines read it side by side, and the scan jobs lie in an Azure Storage account instead of a folder (`app/azure_store.py`): the units' messages in a queue, the pages, units and readings as blobs, shared by every copy of the server, so a copy that is stopped or restarted leaves its units to the others. `JOBS_STORAGE_ACCOUNT` names the account (with `JOBS_QUEUE` and `JOBS_CONTAINER`, by default `scan-units` and `scan-jobs`); the server signs in to it as the container app's managed identity (`AZURE_CLIENT_ID`), and the account takes no keys. The app scales on the queue too: it stays awake while a unit waits or is read, and scales to zero once every job has finished. `infra/scan.bicep` and `infra/scan-base.bicep` set this up ([infra/README.md](../../../infra/README.md#scan-jobs)). Without `JOBS_STORAGE_ACCOUNT`, as on your own computer, the jobs lie in `JOBS_FOLDER`.

## What it does with your pages

Each upload to `/recognise` or `/transcribe` is written to a temporary folder, read by homr page by page (or, for a recording, its sound taken out by ffmpeg and heard by Transkun), and the folder is deleted when the answer has been sent. ffmpeg opens only the upload itself, and only as a common audio or video format, never a playlist or another file. Nothing of it is kept, and the server logs no request details.

A scan job keeps its pages (as the engines read them; the uploaded file itself is not kept) and its readings until the app has fetched the result, and **at most 24 hours** after it was made. They are deleted 10 minutes after the result was first fetched, at once when the app deletes the job, and in any case a day after the job was made, whether or not it finished. On the hosted server they lie in Motiff Lab's own storage account in Poland Central, reached only by the server's identity, and the account also deletes any job left there a day after it was last written, should no copy of the server run to delete it (Azure applies that rule about once a day). The job's id is random and cannot be guessed; on the hosted server only the signed-in player who made the job can ask for it, and the job keeps a hash of that player's id, not the id. Nothing about the music is logged, and nothing is used for anything but reading the job. It answers any web page that calls it, which is fine on your own computer; on a shared one, start it with `-e ALLOWED_ORIGINS=https://<the app's address>`.

## API

| Request | Answer |
| --- | --- |
| `GET /health` | `{"engine": "homr", "version": "0.7.0", "engines": [{"engine": "homr", "version": "0.7.0", "secondsPerPage": 31.4, "pagesMeasured": 12}, {"engine": "copisteria", "version": "1.3.4", "secondsPerPage": 90.0, "pagesMeasured": 0}, {"engine": "audiveris", "version": "5.11.0", …}, {"engine": "transcoda", "version": "2.1.0", …}], "maxPages": 8, "signIn": false, "jobs": {"queue": 3, "workers": 4}, "transcribe": {"engine": "transkun", "version": "2.0.1", "maxMegabytes": 200, "maxSeconds": 150}}`: `secondsPerPage` is the engine's average on this server (of about the last 20 pages it read in jobs), or its usual figure while `pagesMeasured` is 0; `queue` counts the units waiting or being read |
| `POST /recognise`, multipart form with `file` (PDF, PNG or JPEG, up to 50 MB, `MAX_UPLOAD_MB`) and, optionally, `engine` (`homr`, the default, or another engine the server has: `copisteria`, `audiveris`, `transcoda`; `400` with `"code": "engine"` for one the server does not have) | `200` with the MusicXML (`application/vnd.recordare.musicxml+xml`) and the headers `X-Engine: homr 0.7.0` (or `copisteria 1.3.4`), `X-Pages` and `X-Page-Report` (below); otherwise `4xx` with `{"message": "…"}` in plain words, and `X-Page-Report` too when a page could not be read |
| `POST /jobs`, multipart form with `file` (as for `/recognise`) and, optionally, `pages` (the PDF's pages to read, from 1, separated by commas, such as `2,3`; all of them by default) and `engines` (separated by commas; every engine the server has by default; `400` with `"code": "engine"` for one it does not have) | `202` with `{"id": "…", "status": "/jobs/…"}` and the status's address in `Location`; `400` (`"code": "pages"`), `413`, `415` or `422` with `{"message": "…"}` for a file or pages it cannot read |
| `GET /jobs/{id}` | `{"id": "…", "state": "running", "pages": [{"page": 1, "engines": {"homr": "read", "copisteria": "reading"}}], "progress": 0.62, "etaSeconds": 40}`: the job is `queued`, `running`, `done` (every unit finished, at least one page read) or `failed` (no page read by any engine); each unit is `waiting`, `reading`, `read`, `retrying` or `not_read`; `progress` runs from 0 to 1. `404` with `"code": "job"` for a job that does not exist, has been deleted or is another player's |
| `GET /jobs/{id}/result` | `{"id": "…", "pages": [{"page": 1, "engines": {"homr": {"state": "read", "version": "0.7.0", "musicxml": "<?xml …", "report": {…}}, "copisteria": {"state": "read", …}, "audiveris": {"state": "not_read", "message": "Page 1 could not be read.", "report": {…}}, "transcoda": {…}}, "evidence": {…}}]}`, with each page's report as in `X-Page-Report` (below) and its [evidence](#the-pages-evidence) where copisteria read it; `409` with `"code": "running"` until every unit has finished |
| `DELETE /jobs/{id}` | `204`; the job's pages and readings are deleted |
| `POST /transcribe`, multipart form with `file` (MP4, MOV, M4A, MP3, WAV, WebM, OGG, FLAC and the like, up to 200 MB) | `200` with the notes as MIDI (`audio/midi`, at 120 beats a minute, so a beat is half a second, with the sustain pedal as controller 64) and the headers `X-Engine: transkun 2.0.1`, `X-Seconds` (how much was heard) and `X-Cut: true` when the recording was longer than the first 2½ minutes that are heard; otherwise `4xx` with `{"message": "…"}` |

Pages of a PDF are joined into one score: the measures of page 2 follow those of page 1, and measure numbers run on. A page that lies sideways or upside down in the photo itself (the photo's orientation tag is followed first) is turned the right way up before homr reads it: on a page that lies sideways the staff lines run up and down, and each system opens with a brace before its staff lines and ends with a bar line after them. When the signs are not clear, as on a dim photo, the page is read as it is. A PDF page drawn by a program (its text and lines are in the PDF, as an engraving's are) is always read as it is: only photos and scanned pages are turned.

### The page report

`X-Page-Report` is a JSON list with one entry per page, in numbers only, so an app can tell a weak photo from a good one. Apps that do not read it lose nothing.

```json
[{"page": 1, "rotation": 90, "sharpness": 1.2, "contrast": 118, "staves": 10, "noteheads": 226, "barLines": 59, "spacePx": 13.0, "measures": 28}]
```

| Field | What it is |
| --- | --- |
| `rotation` | degrees the page was turned clockwise before reading: 0, 90, 180 or 270 |
| `sharpness` | how steep the edges of the print are against its contrast; about 1 is crisp, below 0.8 soft |
| `contrast` | how much darker the darkest print is than the paper around it, in grey levels (0–255); a page in good light has about 110 or more |
| `staves`, `noteheads`, `barLines` | what the engine found: homr's from its log, copisteria's from its reading (the symbols it judged real); 0 for Audiveris and Transcoda, which do not report them |
| `spacePx` | about one staff space: for homr the median height of a notehead in its working copy of the page (cropped to the paper, 1920 pixels wide); for copisteria a quarter of the median measure box it detected, scaled to a page 1920 pixels wide; 0 for Audiveris and Transcoda |
| `measures` | measures in the music that came out of the page; 0 when the page could not be read |

The first three are measured by this service with Pillow before the engine runs; the rest come from homr's log, or copisteria's reading and detections, which are read for these counts only and never passed on, since they also name the title read. copisteria's reading also holds a probability for every symbol and the readings the context changed; it is deleted with the upload for now. One upload, scan or recording, is read at a time; a second waits its turn. `-e MAX_RECORDING_MB=…` and `-e MAX_RECORDING_SECONDS=…` change the limits for recordings.

## Develop

```bash
cd src/backend/omr
python -m venv .venv && . .venv/bin/activate
pip install -r requirements-test.txt
python -m pytest
```

The tests replace homr, copisteria, Audiveris, Transcoda and Transkun with stand-ins, so they run in seconds and need neither Java nor the models; a job's stand-ins (`tests/stand_ins.py`) are functions of a module, since a job reads each unit in a process of its own. To try the real engines without Docker, install ffmpeg, `pip install --index-url https://download.pytorch.org/whl/cpu torch==2.11.0 torchaudio==2.11.0`, `pip install -r requirements.txt`, `pip install --no-deps transkun==2.0.1` (its own requirements bring its training tools, which are not needed here), run `homr --gpu no --init` once, then `uvicorn app.main:app --port 8765`.

## Licence

homr is licensed under the [GNU AGPL v3](https://github.com/liebharc/homr/blob/main/LICENSE), and so is this service (the files in this folder). It runs homr as published on PyPI, without changes; its source is at <https://github.com/liebharc/homr>. copisteria and the copista-28m weights are by KobiMusic (<https://kobi.music>), [Apache-2.0](https://github.com/kobimusic/copisteria/blob/main/LICENSE): the image runs them unmodified from a pinned commit of <https://github.com/kobimusic/copisteria> and a pinned revision of <https://huggingface.co/kobimusic/copista-28m>, and keeps their LICENSE and NOTICE files in `/opt/copisteria` (the weights' NOTICE as `models/NOTICE.copista-28m`). Transkun, with its model, is [MIT](https://github.com/Yujia-Yan/Skipping-The-Frame-Level/blob/main/LICENSE) and also runs as published on PyPI, on PyTorch's CPU build (BSD-3-Clause). ffmpeg is Debian's build, run as a separate program; its licence and source come with Debian. The Motiff Lab app only sends files to the service and reads its answers.

[Audiveris](https://github.com/Audiveris/audiveris) is licensed under the [GNU AGPL v3](https://github.com/Audiveris/audiveris/blob/master/LICENSE). The image runs version 5.11.0 as published, a Java program with the Java runtime it ships: its Linux package from the project's GitHub release, checked by its SHA-256 and unpacked into `/opt/audiveris` without changes (its copyright file is in `/opt/audiveris/share/doc`). It is run as a separate program; its source is at <https://github.com/Audiveris/audiveris>.

[Transcoda](https://github.com/btrkeks/transcoda) is by Daniel Dratschuk and Paul Swoboda (Heinrich Heine University Düsseldorf). Its code is licensed under the [GNU AGPL v3](https://github.com/btrkeks/transcoda/blob/main/LICENSE): the image runs its inference unmodified, from the commit of its 2.1 release, in an environment made from its own lock file, in `/opt/transcoda` with its LICENSE, NOTICE.md and `licenses/` folder; the parts it does not use to read a page (training, evaluation, data generation, and the vendored parsers they are built with, one of them non-commercial) are left out. **Its weights ([`btrkeks/transcoda-v2.1`](https://huggingface.co/btrkeks/transcoda-v2.1), pinned revision, checked by SHA-256) are licensed [CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/): non-commercial use only.** So is the ConvNeXt V2 encoder they start from ([`facebook/convnextv2-tiny-22k-224`](https://huggingface.co/facebook/convnextv2-tiny-22k-224)), which Transcoda loads first. The weights' LICENSE, NOTICE.md and UPSTREAM_LICENSE are in `/opt/transcoda/weights`. Andrei accepted them for the MVP and the prototype, which are not sold (decision record 0035); before an App Store release a commercial licence is asked from the authors, and until then `-e ENGINES=homr,copisteria,audiveris` switches Transcoda off. Its kern is turned into MusicXML here with [music21](https://github.com/cuthbertLab/music21) (BSD-3-Clause) and [converter21](https://github.com/gregchapman-dev/converter21) (MIT).
