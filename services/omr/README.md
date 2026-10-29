# Motiff Lab scan server

Reads a PDF or a photo of printed music and returns MusicXML, for Motiff Lab's **Scan printed music** (decision record [0011](../../docs/decisions/0011-sheet-music-recognition.md)). It runs [homr](https://github.com/liebharc/homr), an open-source optical music recognition engine, unmodified, on your own computer.

It also transcribes a recording, audio or video, into MIDI notes with [Basic Pitch](https://github.com/spotify/basic-pitch) (decision record [0014](../../docs/decisions/0014-hosted-scan-server.md)). Basic Pitch is cheap and quick on a CPU, about a second for a minute of sound, but it is a first draft: it hears any instrument, not the piano's pedal, so the notes are for the player to check.

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

Each upload is written to a temporary folder, read by homr page by page (or, for a recording, its sound taken out by ffmpeg and heard by Basic Pitch), and the folder is deleted when the answer has been sent. ffmpeg opens only the upload itself, and only as a common audio or video format, never a playlist or another file. Nothing is kept, and the server logs no request details. It answers any web page that calls it, which is fine on your own computer; on a shared one, start it with `-e ALLOWED_ORIGINS=https://<the app's address>`.

## API

| Request | Answer |
| --- | --- |
| `GET /health` | `{"engine": "homr", "version": "0.7.0", "maxPages": 8, "signIn": false, "transcribe": {"engine": "basic-pitch", "version": "0.4.0", "maxMegabytes": 200, "maxSeconds": 600}}` |
| `POST /recognise`, multipart form with `file` (PDF, PNG or JPEG, up to 20 MB) | `200` with the MusicXML (`application/vnd.recordare.musicxml+xml`) and the headers `X-Engine: homr 0.7.0` and `X-Pages`; otherwise `4xx` with `{"message": "…"}` in plain words |
| `POST /transcribe`, multipart form with `file` (MP4, MOV, M4A, MP3, WAV, WebM, OGG, FLAC and the like, up to 200 MB) | `200` with the notes as MIDI (`audio/midi`, at 120 beats a minute, so a beat is half a second) and the headers `X-Engine: basic-pitch 0.4.0`, `X-Seconds` (how much was heard) and `X-Cut: true` when the recording was longer than the first 10 minutes that are heard; otherwise `4xx` with `{"message": "…"}` |

Pages of a PDF are joined into one score: the measures of page 2 follow those of page 1, and measure numbers run on. One upload, scan or recording, is read at a time; a second waits its turn. `-e MAX_RECORDING_MB=…` and `-e MAX_RECORDING_SECONDS=…` change the limits for recordings.

## Develop

```bash
cd services/omr
python -m venv .venv && . .venv/bin/activate
pip install -r requirements-test.txt
python -m pytest
```

The tests replace homr and Basic Pitch with stand-ins, so they run in seconds. To try the real engines without Docker, install ffmpeg, `pip install -r requirements.txt`, `pip install --no-deps basic-pitch==0.4.0` (its own requirements ask for TensorFlow, which it does not need here), run `homr --gpu no --init` once, then `uvicorn app.main:app --port 8765`.

## Licence

homr is licensed under the [GNU AGPL v3](https://github.com/liebharc/homr/blob/main/LICENSE), and so is this service (the files in this folder). It runs homr as published on PyPI, without changes; its source is at <https://github.com/liebharc/homr>. Basic Pitch, with its model, is [Apache-2.0](https://github.com/spotify/basic-pitch/blob/main/LICENSE) and also runs as published on PyPI. ffmpeg is Debian's build, run as a separate program; its licence and source come with Debian. The Motiff Lab app only sends files to the service and reads its answers.
