# 0014 · A hosted scan server, and a database for what players keep

Status: accepted, 2026-10-28. Andrei chose sign-in with Apple, Google or an email code through Microsoft Entra External ID. The scan server is deployed; the API waits on the tenant's one-time setup (`infra/README.md`).

## Context

The iOS app cannot carry homr: its AGPL licence keeps it out of an App Store build, and an on-device engine waits on a licence answer or on another model (the research of 2026-10-28, project folder `omr/on-device/`). An app that sends a page to our own server and reads MusicXML back stays closed source, and homr runs unmodified there, as it does on the player's computer (0011). Andrei also wants sessions and imported scores saved in the cloud, in Cosmos DB, rather than only in the browser.

The Azure subscription is shared with an unrelated project. Its Container Apps environments carry a Standard Load Balancer (about $18 a month each) because they use workload profiles and a VNet. A free-tier Cosmos DB account in `rg-shared` (Poland Central) is to be reused, with Motiff Lab's own database in it.

## Decision

### The scan server

1. **The `services/omr` image runs in Azure Container Apps in Poland Central** (`infra/scan.bicep`), in a Consumption-only environment on Azure's own network, with no Log Analytics workspace. Such an environment costs nothing by itself; the app is billed per second only while it reads, and it scales to zero when idle. One copy at most, with 2 cores and 4 GiB (the most a Consumption-only app gets), reading one upload at a time.
2. **The image is public on GitHub's container registry** (`ghcr.io/andreykukalev/motiff-omr`), built and deployed by the `Scan server` workflow on each change to `services/omr` on `main`. The repository is public and the service is AGPL, so nothing is lost by a public image, and no registry is paid for. The image's source label points to this repository, which is where homr's source is named.
3. **A PDF may have up to 4 pages when hosted**, not 8: Container Apps ends a request after 240 s, and a page takes up to 40 s on 2 cores.
4. **Browsers may call it only from the web app and its pull request previews** (`ALLOWED_ORIGINS`, `ALLOWED_ORIGIN_REGEX`). A native app sends no origin, and an origin is no protection against a script, so until scanning needs a sign-in, the single copy is what caps the cost of misuse.
5. **Privacy stays as 0011 has it:** the page lives in a temporary folder for the request and is deleted when the answer is sent; nothing about the music is logged, and Container Apps keeps the data in the region. The app asks before each upload to a server that is not on this computer, and the iOS app will show a consent screen and declare user content in its privacy label.

### The database

6. **Motiff Lab gets its own database, `motifflab`, in the shared free-tier account**, with 400 RU/s shared by its containers, and one container, `projects`, partitioned by `/ownerId`, so each player's projects, sessions and sources sit in one partition. `infra/cosmos-bootstrap.sh` creates it once, and first adds up what the account's other databases use, so it stops rather than take the account past the free tier's 1000 RU/s.
7. **Only the Motiff Lab API reaches the data, with its own identity** (`id-motifflab-api`), allowed into this one database through Cosmos DB's data roles. No account key is used or stored, and nothing on the account or in its other databases changes.
8. **What is stored:** a project document (the project file of 0009 and 0012: sessions, observations, the scan origin) and a source document with the imported MusicXML or MIDI, compressed. A source too large for a 2 MB document goes to Blob storage instead, which is added only when one first needs it.

### Sign-in and the API

9. **Players sign in with Microsoft Entra External ID**, in its own external tenant: Apple, Google or a one-time code by email. The core is free for the first 50,000 monthly active users. One app registration, *Motiff Lab API*, exposes two scopes, `Projects.ReadWrite` and `Scan`; the web app and, later, the iOS app ask for them. A player is their `oid` in the tenant, the same from every app, and that is the partition their documents sit in.
10. **The API is `src/MotiffLab.Api`**, an ASP.NET Core service in the scan server's environment (`infra/api.bicep`), scaled to zero. It lists, reads, saves and deletes the calling player's projects (`/projects`, `/projects/{id}`) and deletes everything they keep (`DELETE /me`, the right to erasure). A save is a project file; the API reads it with `ProjectFile` before storing it, refuses one that is not that project, and returns an ETag, so a save from a stale copy on another device is refused (`If-Match`, 412) rather than lost. It logs no project content.
11. **The hosted scan server reads a page only with a token that carries `Scan`** once `AUTH_AUTHORITY` and `AUTH_AUDIENCE` are set; on the player's own computer nothing is checked, as before.
12. **Saving online is the player's choice, never the default.** The rule that imports never leave the device (CLAUDE.md, 0011) becomes: nothing leaves the device unless the player signs in and turns on saving online, and then only to this API. The web app's sign-in and sync, the privacy notice and the consent wording come in the next batch.

## Consequences

- The scan server's cold start pulls a 2 GB image, so the first page after an idle spell can take a minute or two longer. Raising `minReplicas` to 1 would remove that but bill around the clock.
- Beyond Container Apps' monthly free grant (180,000 vCPU-seconds and 360,000 GiB-seconds per subscription, which the other project's apps share), a page costs about a quarter of a cent, and the copy then waits five minutes at the idle rate (about half a cent) before it scales to zero, so the next page of a session starts warm.
- The player's own computer stays an option: Settings still takes any address, and `http://localhost:8765` still works offline.
