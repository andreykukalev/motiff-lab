# Motiff Lab scan server

Reads a PDF or a photo of printed music and returns MusicXML, for Motiff Lab's **Scan printed music** (decision record [0011](../../../docs/architecture/adr/0011-sheet-music-recognition.md)). It runs [homr](https://github.com/liebharc/homr), an open-source optical music recognition engine, unmodified, on your own computer. A second engine, [copisteria](https://github.com/kobimusic/copisteria) with its copista-28m model, reads the same pages when the app asks for it (the experiment «Выбор движка распознавания» in Settings · Experimental), so the two can be compared on your own pages; unlike homr, it reads the printed time signature.

It also transcribes a recording, audio or video, into MIDI notes with [Transkun](https://github.com/Yujia-Yan/Skipping-The-Frame-Level) (decision record [0014](../../../docs/architecture/adr/0014-hosted-scan-server.md)), a piano transcription model that also hears the sustain pedal. On a CPU it takes about a second of two cores for a second of sound, so it hears the first 2½ minutes of a recording. The notes are still a draft for the player to check.

Version 1.3 extends this service to four engines and background jobs (decision record [0035](../../../docs/architecture/adr/0035-scan-jobs-four-engines.md)). The background jobs have landed ([Scan jobs](#scan-jobs), and `/jobs` in the API below); the four engines come with the next change, until then a job reads with the engines this server has.

## Run it

You need [Docker Desktop](https://www.docker.com/products/docker-desktop/) (Windows or macOS) or Docker on Linux. In a copy of this repository:

```bash
docker build -t motiff-omr src/backend/omr
docker run --rm -p 8765:8000 motiff-omr
```

The build takes a few minutes and downloads about 2 GB once, including homr's models; after that the server works offline. Then in Motiff Lab open **Settings → Sheet music scanning**, keep the address `http://localhost:8765`, and choose **Save and check**. **Import** now has a **Scan printed music** drop zone.

To keep it running in the background, start it with `docker run -d --restart unless-stopped -p 8765:8000 --name motiff-omr motiff-omr`, and stop it with `docker stop motiff-omr`.

A page takes 20 to 60 seconds, depending on the computer. A PDF can have up to 8 pages (`-e MAX_PAGES=12` raises that).

### Scan jobs

`POST /jobs` reads the pages in the background instead of while the app waits (decision record [0035](../../../docs/architecture/adr/0035-scan-jobs-four-engines.md)): each page is read by every engine the server has, and the app asks how far it got. The unit of work is one page read by one engine, a message in a queue that a worker takes. A unit that fails, or runs past its time limit (5 minutes; its engine is stopped then), is tried again after 10, 20 and 40 seconds; after the last try that page is not read by that engine and the job goes on with the others. An engine that fails on 3 pages of a job in a row is switched off for the rest of that job. A worker renews its lease on a unit while it reads it, so a unit whose worker died goes back to the queue. Once a minute a supervisor gives a unit whose message was lost a new one, closes a job that has not finished in 3 hours with what was read, and deletes what has expired (below). The progress and the estimate weigh each unit by its engine's seconds per page, measured on this server, which `/health` shows too.

The jobs lie in a folder, `/tmp/motiff-scan-jobs` in the container (`-e JOBS_FOLDER=…` moves it), so a server that is restarted (`docker restart motiff-omr`, or `--restart unless-stopped` after a crash) goes on with the jobs it had; `docker run --rm` starts with none. One unit is read at a time, since the engines use every core; `-e JOB_WORKERS=2` reads two.

## Hosted

The same image also runs in Azure Container Apps for the iOS app and for players without Docker (decision record [0014](../../../docs/architecture/adr/0014-hosted-scan-server.md)). The `Scan server` workflow publishes it as `ghcr.io/andreykukalev/motiff-omr` and deploys it with [`infra/scan.bicep`](../../../infra/scan.bicep) on each change here on `main`. There it takes PDFs of up to 4 pages and recordings as on your own computer, answers browsers only from the web app and its previews (`ALLOWED_ORIGINS`, `ALLOWED_ORIGIN_REGEX`), and scales to zero when idle. With `AUTH_AUTHORITY` and `AUTH_AUDIENCE` set, it reads a page or a recording only for a player signed in through Microsoft Entra External ID whose token carries the `Scan` scope, and answers `401` otherwise; `/health` says so with `"signIn": true`. Unset, as on your own computer, nothing is checked.

## What it does with your pages

Each upload to `/recognise` or `/transcribe` is written to a temporary folder, read by homr page by page (or, for a recording, its sound taken out by ffmpeg and heard by Transkun), and the folder is deleted when the answer has been sent. ffmpeg opens only the upload itself, and only as a common audio or video format, never a playlist or another file. Nothing of it is kept, and the server logs no request details.

A scan job keeps its pages (as the engines read them; the uploaded file itself is not kept) and its readings until the app has fetched the result, and **at most 24 hours** after it was made. They are deleted 10 minutes after the result was first fetched, at once when the app deletes the job, and in any case a day after the job was made, whether or not it finished. The job's id is random and cannot be guessed; on the hosted server only the signed-in player who made the job can ask for it, and the job keeps a hash of that player's id, not the id. Nothing about the music is logged, and nothing is used for anything but reading the job. It answers any web page that calls it, which is fine on your own computer; on a shared one, start it with `-e ALLOWED_ORIGINS=https://<the app's address>`.

## API

| Request | Answer |
| --- | --- |
| `GET /health` | `{"engine": "homr", "version": "0.7.0", "engines": [{"engine": "homr", "version": "0.7.0", "secondsPerPage": 31.4, "pagesMeasured": 12}, {"engine": "copisteria", "version": "1.3.4", "secondsPerPage": 25.0, "pagesMeasured": 0}], "maxPages": 8, "signIn": false, "jobs": {"queue": 3, "workers": 1}, "transcribe": {"engine": "transkun", "version": "2.0.1", "maxMegabytes": 200, "maxSeconds": 150}}`: `secondsPerPage` is the engine's average on this server (of about the last 20 pages it read in jobs), or its usual figure while `pagesMeasured` is 0; `queue` counts the units waiting or being read |
| `POST /recognise`, multipart form with `file` (PDF, PNG or JPEG, up to 50 MB, `MAX_UPLOAD_MB`) and, optionally, `engine` (`homr`, the default, or `copisteria`; `400` with `"code": "engine"` for one the server does not have) | `200` with the MusicXML (`application/vnd.recordare.musicxml+xml`) and the headers `X-Engine: homr 0.7.0` (or `copisteria 1.3.4`), `X-Pages` and `X-Page-Report` (below); otherwise `4xx` with `{"message": "…"}` in plain words, and `X-Page-Report` too when a page could not be read |
| `POST /jobs`, multipart form with `file` (as for `/recognise`) and, optionally, `pages` (the PDF's pages to read, from 1, separated by commas, such as `2,3`; all of them by default) and `engines` (separated by commas; every engine the server has by default; `400` with `"code": "engine"` for one it does not have) | `202` with `{"id": "…", "status": "/jobs/…"}` and the status's address in `Location`; `400` (`"code": "pages"`), `413`, `415` or `422` with `{"message": "…"}` for a file or pages it cannot read |
| `GET /jobs/{id}` | `{"id": "…", "state": "running", "pages": [{"page": 1, "engines": {"homr": "read", "copisteria": "reading"}}], "progress": 0.62, "etaSeconds": 40}`: the job is `queued`, `running`, `done` (every unit finished, at least one page read) or `failed` (no page read by any engine); each unit is `waiting`, `reading`, `read`, `retrying` or `not_read`; `progress` runs from 0 to 1. `404` with `"code": "job"` for a job that does not exist, has been deleted or is another player's |
| `GET /jobs/{id}/result` | `{"id": "…", "pages": [{"page": 1, "engines": {"homr": {"state": "read", "version": "0.7.0", "musicxml": "<?xml …", "report": {…}}, "copisteria": {"state": "not_read", "message": "No music staves were found on page 1. …", "report": {…}}}}]}`, with each page's report as in `X-Page-Report` (below); `409` with `"code": "running"` until every unit has finished |
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
| `staves`, `noteheads`, `barLines` | what the engine found: homr's from its log, copisteria's from its reading (the symbols it judged real) |
| `spacePx` | about one staff space: for homr the median height of a notehead in its working copy of the page (cropped to the paper, 1920 pixels wide); for copisteria a quarter of the median measure box it detected, scaled to a page 1920 pixels wide |
| `measures` | measures in the music that came out of the page; 0 when the page could not be read |

The first three are measured by this service with Pillow before the engine runs; the rest come from homr's log, or copisteria's reading and detections, which are read for these counts only and never passed on, since they also name the title read. copisteria's reading also holds a probability for every symbol and the readings the context changed; it is deleted with the upload for now. One upload, scan or recording, is read at a time; a second waits its turn. `-e MAX_RECORDING_MB=…` and `-e MAX_RECORDING_SECONDS=…` change the limits for recordings.

## Develop

```bash
cd src/backend/omr
python -m venv .venv && . .venv/bin/activate
pip install -r requirements-test.txt
python -m pytest
```

The tests replace homr, copisteria and Transkun with stand-ins, so they run in seconds; a job's stand-ins (`tests/stand_ins.py`) are functions of a module, since a job reads each unit in a process of its own. To try the real engines without Docker, install ffmpeg, `pip install --index-url https://download.pytorch.org/whl/cpu torch==2.11.0 torchaudio==2.11.0`, `pip install -r requirements.txt`, `pip install --no-deps transkun==2.0.1` (its own requirements bring its training tools, which are not needed here), run `homr --gpu no --init` once, then `uvicorn app.main:app --port 8765`.

## Licence

homr is licensed under the [GNU AGPL v3](https://github.com/liebharc/homr/blob/main/LICENSE), and so is this service (the files in this folder). It runs homr as published on PyPI, without changes; its source is at <https://github.com/liebharc/homr>. copisteria and the copista-28m weights are by KobiMusic (<https://kobi.music>), [Apache-2.0](https://github.com/kobimusic/copisteria/blob/main/LICENSE): the image runs them unmodified from a pinned commit of <https://github.com/kobimusic/copisteria> and a pinned revision of <https://huggingface.co/kobimusic/copista-28m>, and keeps their LICENSE and NOTICE files in `/opt/copisteria` (the weights' NOTICE as `models/NOTICE.copista-28m`). Transkun, with its model, is [MIT](https://github.com/Yujia-Yan/Skipping-The-Frame-Level/blob/main/LICENSE) and also runs as published on PyPI, on PyTorch's CPU build (BSD-3-Clause). ffmpeg is Debian's build, run as a separate program; its licence and source come with Debian. The Motiff Lab app only sends files to the service and reads its answers.
