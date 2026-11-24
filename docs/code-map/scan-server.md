# Code map: the scan server (`src/backend/omr`)

Python (FastAPI) around the engines, run on the player's computer in Docker (0011) or hosted (0014); jobs by 0035.
Tests: `src/backend/omr/tests`, `python -m pytest` there, with stand-ins for every engine. How to run and the API:
`src/backend/omr/README.md`.

## Modules

- `main.py`: the HTTP layer. `create_app(recognise_page, …, other_engines, jobs_store, job_settings)`: `/health`,
  `/recognise` (one request, pages joined by `merge.py`), `/transcribe`, `/jobs` and its status, result and delete.
  `signed_in` (the player's id or a 401/403, via `auth.py`), `_upload` (size and kind), `_problem` (the error shape).
  Starts and stops the job threads (`job_worker.Runner`) in its lifespan.
- `engines.py`: `Engine(name, version, read, seconds)`, the pluggable list (`others()`); `seconds` is the starting
  estimate of a page. A job runs `read` in a child process, so it is a module function or a `functools.partial`.
- `recognise.py` (homr), `copisteria_reader.py`: one engine each; `Reading`, `Found`, `RecogniseError`.
- `pages.py`: `sniff`, `write_pages(upload, folder, max_pages, numbers)` (renders and turns pages), `page_report`
  (numbers only). `quality.py`: a page's look.
- `auth.py`: `TokenVerifier.verify` returns the token's `oid`; the Pro rule as `MusicCore.Accounts.Plans`.
- `job_store.py`: `Store`, the one interface over the queue (`send`, `receive` with a lease, `renew`,
  `delete_message`, `queue_length`) and the blobs (`write`, `read`, `delete` by prefix, `names`); `FolderStore`
  (memory and a folder, one process). s-3 adds the Azure Storage queue and blobs behind it.
- `jobs.py`: `Jobs`, the job records and what the routes ask (`submit`, `find`, `status`, `result`, `delete`,
  `figures`), the measured seconds per page (`seconds_per_page`, `measure`) and the `supervise` pass; `Settings`.
- `job_worker.py`: `Worker.step` (one message: lease, read, retry 10/20/40 s, poison, breaker) and `Runner` (threads).
- `unit_process.py`: `read_within`, an engine's read in its own process group, killed at the time limit.

## State and rules

- Blob names: `jobs/<id>/job.json`, `pages/<page>.png`, `units/<page>-<engine>.json`, `results/<page>-<engine>.json`,
  and `engines/<engine>.json` (the measured time). A unit's state is written by the worker holding its message; the
  supervisor and the breaker write only units nobody holds. A message carries the unit's token; another token is stale.
- With no message in the queue, every unfinished unit has lost its own: the supervisor requeues it.
- A job is deleted 10 minutes after its result is first fetched, on `DELETE`, and 24 hours after it was made.
- Nothing about the music or the player is logged; a job keeps a hash of the player's id.
- `/recognise` and `/transcribe` keep nothing after the answer, and stay as they are until the iPad uses jobs.
