# Motiff Lab scan server

Reads a PDF or a photo of printed music and returns MusicXML, for Motiff Lab's **Scan printed music** (decision record [0011](../../docs/decisions/0011-sheet-music-recognition.md)). It runs [homr](https://github.com/liebharc/homr), an open-source optical music recognition engine, unmodified, on your own computer. A second engine, [copisteria](https://github.com/kobimusic/copisteria) with its copista-28m model, reads the same pages when the app asks for it (the experiment «Выбор движка распознавания» in Settings · Experimental), so the two can be compared on your own pages; unlike homr, it reads the printed time signature.

It also transcribes a recording, audio or video, into MIDI notes with [Transkun](https://github.com/Yujia-Yan/Skipping-The-Frame-Level) (decision record [0014](../../docs/decisions/0014-hosted-scan-server.md)), a piano transcription model that also hears the sustain pedal. On a CPU it takes about a second of two cores for a second of sound, so it hears the first 2½ minutes of a recording. The notes are still a draft for the player to check.

## Run it

You need [Docker Desktop](https://www.docker.com/products/docker-desktop/) (Windows or macOS) or Docker on Linux. In a copy of this repository:

```bash
docker build -t motiff-omr services/omr
docker run --rm -p 8765:8000 motiff-omr
```

The build takes a few minutes and downloads about 2 GB once, including homr's models; after that the server works offline. Then in Motiff Lab open **Settings → Sheet music scanning**, keep the address `http://localhost:8765`, and choose **Save and check**. **Import** now has a **Scan printed music** drop zone.

To keep it running in the background, start it with `docker run -d --restart unless-stopped -p 8765:8000 --name motiff-omr motiff-omr`, and stop it with `docker stop motiff-omr`.

A page takes 20 to 60 seconds, depending on the computer. A PDF can have up to 8 pages (`-e MAX_PAGES=12` raises that).

## Hosted

The same image also runs in Azure Container Apps for the iOS app and for players without Docker (decision record [0014](../../docs/decisions/0014-hosted-scan-server.md)). The `Scan server` workflow publishes it as `ghcr.io/andreykukalev/motiff-omr` and deploys it with [`infra/scan.bicep`](../../infra/scan.bicep) on each change here on `main`. There it takes PDFs of up to 4 pages and recordings as on your own computer, answers browsers only from the web app and its previews (`ALLOWED_ORIGINS`, `ALLOWED_ORIGIN_REGEX`), and scales to zero when idle. With `AUTH_AUTHORITY` and `AUTH_AUDIENCE` set, it reads a page or a recording only for a player signed in through Microsoft Entra External ID whose token carries the `Scan` scope, and answers `401` otherwise; `/health` says so with `"signIn": true`. Unset, as on your own computer, nothing is checked.

## What it does with your pages

Each upload is written to a temporary folder, read by homr page by page (or, for a recording, its sound taken out by ffmpeg and heard by Transkun), and the folder is deleted when the answer has been sent. ffmpeg opens only the upload itself, and only as a common audio or video format, never a playlist or another file. Nothing is kept, and the server logs no request details. It answers any web page that calls it, which is fine on your own computer; on a shared one, start it with `-e ALLOWED_ORIGINS=https://<the app's address>`.

## API

| Request | Answer |
| --- | --- |
| `GET /health` | `{"engine": "homr", "version": "0.7.0", "engines": [{"engine": "homr", "version": "0.7.0"}, {"engine": "copisteria", "version": "1.3.4"}], "maxPages": 8, "signIn": false, "transcribe": {"engine": "transkun", "version": "2.0.1", "maxMegabytes": 200, "maxSeconds": 150}}` |
| `POST /recognise`, multipart form with `file` (PDF, PNG or JPEG, up to 50 MB, `MAX_UPLOAD_MB`) and, optionally, `engine` (`homr`, the default, or `copisteria`; `400` with `"code": "engine"` for one the server does not have) | `200` with the MusicXML (`application/vnd.recordare.musicxml+xml`) and the headers `X-Engine: homr 0.7.0` (or `copisteria 1.3.4`), `X-Pages` and `X-Page-Report` (below); otherwise `4xx` with `{"message": "…"}` in plain words, and `X-Page-Report` too when a page could not be read |
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
cd services/omr
python -m venv .venv && . .venv/bin/activate
pip install -r requirements-test.txt
python -m pytest
```

The tests replace homr, copisteria and Transkun with stand-ins, so they run in seconds. To try the real engines without Docker, install ffmpeg, `pip install --index-url https://download.pytorch.org/whl/cpu torch==2.11.0 torchaudio==2.11.0`, `pip install -r requirements.txt`, `pip install --no-deps transkun==2.0.1` (its own requirements bring its training tools, which are not needed here), run `homr --gpu no --init` once, then `uvicorn app.main:app --port 8765`.

## Licence

homr is licensed under the [GNU AGPL v3](https://github.com/liebharc/homr/blob/main/LICENSE), and so is this service (the files in this folder). It runs homr as published on PyPI, without changes; its source is at <https://github.com/liebharc/homr>. copisteria and the copista-28m weights are by KobiMusic (<https://kobi.music>), [Apache-2.0](https://github.com/kobimusic/copisteria/blob/main/LICENSE): the image runs them unmodified from a pinned commit of <https://github.com/kobimusic/copisteria> and a pinned revision of <https://huggingface.co/kobimusic/copista-28m>, and keeps their LICENSE and NOTICE files in `/opt/copisteria` (the weights' NOTICE as `models/NOTICE.copista-28m`). Transkun, with its model, is [MIT](https://github.com/Yujia-Yan/Skipping-The-Frame-Level/blob/main/LICENSE) and also runs as published on PyPI, on PyTorch's CPU build (BSD-3-Clause). ffmpeg is Debian's build, run as a separate program; its licence and source come with Debian. The Motiff Lab app only sends files to the service and reads its answers.
