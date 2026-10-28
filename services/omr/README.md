# Motiff Lab scan server

Reads a PDF or a photo of printed music and returns MusicXML, for Motiff Lab's **Scan printed music** (decision record [0011](../../docs/decisions/0011-sheet-music-recognition.md)). It runs [homr](https://github.com/liebharc/homr), an open-source optical music recognition engine, unmodified, on your own computer.

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

The same image also runs in Azure Container Apps for the iOS app and for players without Docker (decision record [0014](../../docs/decisions/0014-hosted-scan-server.md)). The `Scan server` workflow publishes it as `ghcr.io/andreykukalev/motiff-omr` and deploys it with [`infra/scan.bicep`](../../infra/scan.bicep) on each change here on `main`. There it takes PDFs of up to 4 pages, answers browsers only from the web app and its previews (`ALLOWED_ORIGINS`, `ALLOWED_ORIGIN_REGEX`), and scales to zero when idle.

## What it does with your pages

Each upload is written to a temporary folder, read by homr page by page, and the folder is deleted when the answer has been sent. Nothing is kept, and the server logs no request details. It answers any web page that calls it, which is fine on your own computer; on a shared one, start it with `-e ALLOWED_ORIGINS=https://<the app's address>`.

## API

| Request | Answer |
| --- | --- |
| `GET /health` | `{"engine": "homr", "version": "0.7.0", "maxPages": 8}` |
| `POST /recognise`, multipart form with `file` (PDF, PNG or JPEG, up to 20 MB) | `200` with the MusicXML (`application/vnd.recordare.musicxml+xml`) and the headers `X-Engine: homr 0.7.0` and `X-Pages`; otherwise `4xx` with `{"message": "…"}` in plain words |

Pages of a PDF are joined into one score: the measures of page 2 follow those of page 1, and measure numbers run on. One upload is read at a time; a second waits its turn.

## Develop

```bash
cd services/omr
python -m venv .venv && . .venv/bin/activate
pip install -r requirements-test.txt
python -m pytest
```

The tests replace homr with a stand-in, so they run in seconds. To try the real engine without Docker, `pip install -r requirements.txt`, run `homr --gpu no --init` once, then `uvicorn app.main:app --port 8765`.

## Licence

homr is licensed under the [GNU AGPL v3](https://github.com/liebharc/homr/blob/main/LICENSE), and so is this service (the files in this folder). It runs homr as published on PyPI, without changes; its source is at <https://github.com/liebharc/homr>. The Motiff Lab app only sends files to the service and reads its answers.
