# Code map: the scan server (`src/backend/omr`)

Python (FastAPI) around the engines, run on the player's computer in Docker (0011) or hosted (0014); jobs by 0035.
Tests: `src/backend/omr/tests`, `python -m pytest` there, with stand-ins for every engine. How to run and the API:
`src/backend/omr/README.md`.

## Modules

- `main.py`: the HTTP layer. `create_app(recognise_page, …, other_engines, jobs_store, job_settings)`: `/health`,
  `/recognise` (one request, pages joined by `merge.py`), `/transcribe`, `/jobs` and its status, result and delete.
  `signed_in` (the player's id or a 401/403, via `auth.py`), `_upload` (size and kind), `_problem` (the error shape).
  Starts and stops the job threads (`job_worker.Runner`) in its lifespan.
- `engines.py`: `Engine(name, version, read, seconds, evidence)`, the pluggable list (`others()`: the installed
  engines besides homr that `ENGINES` keeps; an unknown name stops the server at start); `seconds` is the starting estimate of a page. A job runs `read` in a child process, so it
  is a module function or a `functools.partial`.
- `recognise.py` (homr; `run`, an engine's program under the page time limit, for every engine; `threads`, `cores` (the affinity, within the cgroup CPU quota);
  `Reading`, `Found`, `RecogniseError`), `copisteria_reader.py` (`page_evidence`, for jobs only), `audiveris_reader.py` (its `.mxl` movements
  joined by `merge.merge(new_page=False)`), `transcoda_reader.py` (compact kern to standard Humdrum, music21 to
  MusicXML, two parts to one part with two staves): one engine each, its own program run unmodified.
- `evidence.py`: a page's evidence (0035 §1): staves, systems and bar lines by image processing (numpy), copisteria's
  detections named as SMuFL glyphs and placed in system, bar and staff; `for_page` adds what the job knows.
- `pages.py`: `sniff`, `write_pages(upload, folder, max_pages, numbers)` (renders and turns pages), `page_report`
  (numbers only). `quality.py`: a page's look.
- `auth.py`: `TokenVerifier.verify` returns the token's `oid`; the Pro rule as `MusicCore.Accounts.Plans`.
- `job_store.py`: `Store`, the one interface over the queue (`send`, `receive` with a lease, `renew`,
  `delete_message`, `queue_length`) and the blobs (`write`, `read`, `delete` by prefix, `names`, `folders` right under
  a prefix); `FolderStore` (memory and a folder, one process), the default.
- `azure_store.py`: `AzureStore`, the same on an Azure Storage queue (visibility timeout, pop receipts; a receipt
  refused with 400 or 404 is `LeaseLost`) and blob container (`folders` by the delimiter), signed in as the managed
  identity; hosted, `main.jobs_store_from_environment` picks it when `JOBS_STORAGE_ACCOUNT` is set. Infrastructure:
  `infra/scan.bicep`, `infra/scan-base.bicep`.
- `jobs.py`: `Jobs`, the job records and what the routes ask (`submit`, `find`, `status`, `result`, `delete`,
  `figures`), the measured seconds per page (`seconds_per_page`, `measure`) and the `supervise` pass; `Settings`.
- `job_worker.py`: `Worker.step` (one message: lease, read, the engine's `evidence` after it, retry 10/20/40 s,
  poison, breaker) and `Runner` (threads, by default one per engine, so a page's engines read it in parallel).
- `unit_process.py`: `read_within`, an engine's read in its own process group, killed at the time limit, its programs
  limited to `Settings.threads` cores (`set_thread_limits`: OpenMP, MKL, OpenBLAS, Java; `recognise.threads`).

## State and rules

- Blob names: `jobs/<id>/job.json`, `pages/<page>.png`, `units/<page>-<engine>.json`, `results/<page>-<engine>.json`,
  and `engines/<engine>.json` (the measured time). Writes are last-writer-wins, with no lock between writers.
- A unit's state is written mostly by the worker holding its message. The supervisor (closing, requeueing) and the
  breaker (switching an engine off) also write units without holding a message, so they can race with a worker that has
  just received one (more likely with `JOB_WORKERS` > 1): a reading under way may then end as `read` or `not_read`, or a
  unit be read twice. What keeps these races harmless is idempotence, not exclusion: a unit has one live message, named
  by the token in the unit and in the message (a message with another token is dropped), and a repeat writes the same
  result name. A unit is written `read` only after its result. Conditional writes (ETags) are not used: they would
  add a compare-and-swap to both stores and every unit write, for races that are already harmless.
- The supervisor requeues a unit only while the queue holds no message but those it sent in that pass, and reads the
  unit again after looking: such a unit has lost its message. It keeps what is left of a retry's pause. It asks a
  busy queue its length once a job (a request on Azure), an idle one before each unit. Every copy runs a supervisor.
- `Store` tests: one contract (`tests/test_job_store.py`) for `FolderStore` and, with Azurite running, `AzureStore`.
- A job is deleted 10 minutes after its result is first fetched, on `DELETE`, and 24 hours after it was made.
- Nothing about the music or the player is logged; a job keeps a hash of the player's id.
- `/recognise` and `/transcribe` keep nothing after the answer, and stay as they are until the iPad uses jobs.
- A page's evidence lies in the result of the engine that saw it (copisteria's); the job's result gives it once per
  page, beside the engines. Engine output stays recognition: nothing is merged on the server (music-core merges).
- An engine's programs write only into the unit's temporary folder (Audiveris's settings and logs too, by XDG folders).
