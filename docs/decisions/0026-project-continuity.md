# 0026 · One project across the web and the iPad (project continuity)

Status: accepted, 2026-11-08. Andrei accepted the basis of the project-continuity council with its round-3 amendments and answered its open questions (19:41, 19:45, 19:48 UTC), and set the release rule (22:04, agreed 22:05 UTC). Amends [0014](0014-hosted-scan-server.md), [0019](0019-native-ipad-app.md), [0020](0020-music-core-facade.md) and [0023](0023-pdf-pieces.md) (section "What this record changes in earlier records"). Decides the architecture questions A1–A4 and the code-architecture question that [0025](0025-two-studios.md) (section 13) left to this record; A5 (one source for the form of a piece) keeps its own task.

Sources: the council's decision record *Совет: один проект на вебе и iPad — запись решения* (https://claude.ai/artifact/PSJzSAPho6vbYCpkw924Vd) and the council folder `/mnt/project-files/councils/project-continuity/`: `facts-baseline.md` (facts F1–F71, read on `main` `c86d48e`), `facts.md` (facts V0–V31, checked against the code between rounds), `r3-instructions.md` (S1–S11 and C1–C6), `r3/` (the round-3 amendments) and `README.md`, section "Ответы Андрея" (Andrei's answers). A fact number below points into those two files.

## Context

The web app and the iPad app are two workspaces of one product ([0025](0025-two-studios.md)). The player continues work on the same piece in either, with no manual steps and nothing lost (0025, section 9), while the two apps are built and released on their own schedules. Sync is free with a quota (0025, section 8); the iPad works with no server and no account (F52).

Today this does not hold, and one loss happens without any sync: importing a project file on the web replaces the saved project with the same id (V8), and the iPad gives a new piece `p_0001` on every launch (F3, F4), so two different pieces from the iPad overwrite each other on the web. Other facts that shaped the decision:

- The writer drops unknown fields and lowers the file version to its own (V2); an unknown setting, variation operation or enum value makes an old build refuse the whole file (V3); a newer file is refused (F25).
- Swift does not parse the project file; it writes only the text the facade produced (F29, V4).
- The API reads the file with music-core before storing it (F42, V9), deploys separately and can be older than the web (V10), and has no create-only upload (V19).
- A note correction has no id of its own; two corrections of one note from two devices make the project unreadable (V20).
- Resume points are the player's named list, not a hand-over pointer (V25, V6).
- Real project files are kilobytes, far below the 1.9M-character limit (V14, F43); PDFs reach 50 MB (F55).

Eight council seats over three rounds (2026-11-08, 18:36–19:20 UTC) converged on one basis, close to "one shared core", with a written contract and golden files taken from the "two implementations" option and server-kept history kept as a candidate from the "server as the source of truth" option.

## Decision

### 1. The basis S1–S11

1. **S1. One core.** The C# music-core reads, writes and merges the project on the web, the iPad and the API; the project file is the contract. Swift does not parse it (F29, V4). No server-side merge and no operation log (V10, V14, F52). This answers 0025 section 13.4: one C# music-core under both apps.
2. **S2. The server checks the envelope only**: format, id, size and quota, never the musical content, because the API can be older than the web (V10) and a full reader refuses new values (V3). It stores exactly the bytes it was sent (V9).
3. **S3. Ids.** A project id is `p_` + a 32-hex GUID, minted on every platform. Every new entity gets a random id, the first motif and variation included (today the fixed `m_0001` and `v_0001`, F9, V17). Old ids stay valid (V1).
4. **S4. Transfer.** The whole file, gzipped (V14). An update carries `If-Match`; the first upload is create-only (`If-None-Match: *`, which the API does not support yet, V19); on 412 the client fetches and merges. With no base copy, the merge is a union with no deletions.
5. **S5. Device state.** Each project keeps a persisted base copy and a dirty flag on disk, not the in-memory queue (F47). The local file is kept as a revision before every merge; an acceptance test shows that nothing is lost. A pull keeps the local `document`, `documentPages` and `documentMarks` when the server copy lacks them (F55, V0e).
   - **Amendment: the foreign-file rule** (V12, V23). The app remembers the hash of the last file it wrote. A file that matches neither that hash nor the base copy (a copy made in Files, a replaced file) is merged with no base.
6. **S6. Compatibility.** Unknown fields are kept and written back; the writer stamps the larger of the file's version and its own (V2). A new enum value, setting or variation operation breaks shipped readers (V3), so it raises `minReader` until a tolerant reader, which keeps unknown values as opaque entities, has shipped in every app. Today's builds refuse a newer file (F25); read-only opening comes with the tolerant reader. The new reader reaches the iPad (a tag) before any writer emits the new version. CI holds golden files, including a "future file" with an unknown value in each enum.
7. **S7. Derived data is recomputed, not stored.** A cross-runtime parity golden comes first: the Linux output asserted under NativeAOT in the iPad workflow (F64, V15). A stored snapshot is added only if parity fails. Advocate 3 dissents (see "Open").
8. **S8. Field classes in the merge.** Every top-level and entity field has a class, and a test fails for a field with none: the player's data (both values kept on a same-field conflict); bookkeeping such as `lastOpened` and counters (the latest or the largest wins, V21); derived data such as `check` (recomputed, never a conflict, V22).
9. **S9. Note corrections (`edits[]`).** Corrections of different notes are concatenated; a correction of the same note on both sides, or two added notes with the same id, is a conflict on those entries (V20). `assembly` merges as one block (web only today, V5). Performances and bookmarks merge by id.
   - **Amendment: the `source` guard** (V31). When the two sides differ in the source of the notes, corrections are not concatenated. Today `source` changes only when a PDF piece with no notes gets its recognised notes (V31), so the case is unreachable, but the merge rules keep the guard.
   - **Amendment: where losing corrections go** (V20). Entries of a conflicting correction that do not go into the log are kept in the conflict record (section 2, C2), so the log stays readable.
10. **S10. Pulling.** On opening a project, on focus or foreground, and when the network returns; never during playback. A pull goes through one save path that replaces the open project; on the web one writer per project across tabs (F48). A conflict is a badge and never blocks opening the piece.
11. **S11. The hand-over record.** One record per device, written only by that device on close or on going to the background: `subject` (the original or a variation id), place, passage, tempo, hands, loop. It is separate from the player's named resume points, which stay a list and are never pruned (V25, V6).
    - **Amendment: the web hand-over debounce.** A browser tab has no reliable close event, so the web writes its record on a change of context, after a delay.

### 2. Andrei's answers to the open questions

- **C1. Phrase identity: a stored anchor.** A phrase gets a stored anchor `ph_` when something first attaches to it (a note, a resume point, the hand-over record, a variation). Its id is a hash of the level and the bounds, taken once when the anchor is minted, so two devices offline mint the same id for the same phrase; the id does not change when the bounds later move. Re-analysis never moves what is attached: the player sees "boundaries changed" and re-attaches everything with one group action. A manual boundary edit by the player carries what is attached with the boundary (scenario 4: 12 notes on a phrase whose boundary moves from bar 17 to bar 18 all move with it, with no manual step). The rule `Observation.Home` (the phrase that holds the onset first, else the largest overlap, V16) does not change, so two builds never show one note on two phrases. This makes "the phrase" an object of the project, as the product frame needs (`docs/08-central-product-model.md`, 0025 section 9.1).
- **C2. One conflict record for everything.** A conflict is a `conflicts[]` record inside the entity, for leaf text, scalars (such as a tempo override) and entities that others point to (such as variations). Because a v15 build drops unknown fields (V2), the merge is switched on only after the tolerant reader that keeps unknown fields has shipped in both apps and `minReader` has been raised.
- **C3. PDFs.** A plain PDF stays only in the iPad's score library and is not synced: the web is for transforming material, and a plain PDF cannot be transformed (Andrei, 19:45). A recognised PDF syncs as a project with notes; its PDF (`document`, `documentPages`) and its marks (`documentMarks`) stay on the iPad (S5, F55). The second half is the moderator's reading of Andrei's answer, which he may correct. Amended for version 0.4 (Andrei, 2026-11-12, `v0.4-data.md` question 2): the marks on the PDF's pages go online with the copy, still without the PDF, and another device that has the same PDF by its fingerprint shows them read only; only a device that has the PDF writes them (the iPad when both have it). Amended for version 0.5 (Andrei, 2026-11-12, 17:45 UTC): a piece that is only a PDF syncs too, without the PDF: its title, composer, tags and playlists, the PDF's description with its fingerprint (`originals`) and the marks on its pages go online (`Project.ForOnline`), so a folder imported on the iPad reaches the web; a device without the PDF lists the piece and opens it once the same file is linked there by its fingerprint, and the device that has the PDF keeps it when the online copy comes back (`ProjectSync.Pulled`). Builds before 0.5 cannot read such a copy (no notes, no PDF) and leave it aside.
  - **Amendment, version 0.3: local-first originals** (Andrei, 2026-11-11, 21:16 and 21:20 UTC). Players' recordings and, where possible, their scores are not stored on our servers; the bytes stay on the devices and only the descriptions of the originals sync (`originals` in the project file: name, size, fingerprint, pages or length, and which device holds the bytes; `docs/project-file.md`, "Originals"). Each app shows where each original is held, and «Привязать оригинал» links a file the player picks again to the same original by its fingerprint. The descriptions are kept within version 15, so version 0.2 of both apps still opens the files; for the same reason a piece that is only a PDF still stays on its device in 0.3 (version 0.2 would read its online copy, with neither notes nor PDF, as damaged), and syncing it as a row of the library waits until no 0.2 build syncs. Superseded in version 0.5 (C3, amended 2026-11-12): it syncs without the PDF, and builds before 0.5 leave its copy aside.
- **C4. Re-keying a legacy `p_NNNN`.** The new id is deterministic: `p_` + sha256(legacy id | project `createdAt` | sha256(`source` or `document`)), so two devices re-keying one piece get one id; `createdAt` is in the file (V30). The exact encoding into S3's 32-hex form is fixed by the id task with a golden file. Re-keying happens on open or import, and the old id is kept in `formerIds`. Two different pieces both `p_0001` imported on the web (V8) get different ids, and neither overwrites the other; one piece copied to the web before sync and re-keyed on both gets one id on both, and the copies merge with no base, as a union (S4). A copy made through Files that does not match the hash of the last write (V12, V23) gets a random GUID and becomes a separate piece.
- **C5. A merge bug in a shipped build.** The iPad lags a tag, builds live 7 days without TestFlight (V13), and no cross-runtime check exists (F64). The defence: the device's revision before the merge (S5), then a self-check after the merge: the result must hold everything both sides held, or it is not sent. Server-kept version history is decided after the RU measurement; the measurement was taken on 2026-11-08 (one save of a whole project, an upsert, cost 13.9 RU; the largest project is about 16 KB compressed), and the decision stays with Andrei (see "Open").
- **C6. Task order.** The V8 import fix first (#291), then the golden files v12–v15 with the "future file" and the parity golden (#293), the tolerant reader (24 enum types behind 33 `Name<T>` sites, settings and operations, likely two tasks, V28), unknown fields kept with the larger version and read-only opening, an iPad tag with the new reader (built by Andrei on macOS), the ids (GUIDs for iPad projects, random entity ids, re-keying), the API's envelope-only check and create-only upload (deployed after Andrei approves), and then the merge in music-core and the two sync clients. The web seat's single save path (29 save calls in 14 files, V29) is a candidate before the web sync client. Andrei allowed the six checks before building that the record lists (unknown fields round trip, WASM and NativeAOT parity, the iPad token on `GET /projects`, RU per save, an old tab after a deploy, the weight of ink and `documentMarks`).

### 3. Releases over one data contract

The data contract is the project file (`docs/project-file.md`, its `version` and `minReader`) and the facade's JSON contract ([0020](0020-music-core-facade.md)). Andrei's rule (22:04, agreed 22:05):

1. **A change to the data contract**: the web and the iPad are released together. When the change breaks shipped readers (S6), the release that carries the tolerant reader reaches both apps before any writer emits the new version.
2. **A change in music-core with the same contract**: both apps are released together by default; a fix used by only one app is released with that app alone.
3. **A change only in the iPad app or only in the web app**: released independently.

The API is not part of the pair: it deploys on its own after Andrei approves (V10), and because it checks only the envelope (S2), a new project-file version needs no API release. This is how A4 and 0025 section 9.3 ("each app shows the data it understands") are met: compatibility comes from S6, not from the two apps always shipping together.

## Consequences

- Every delivery task of this work cites the rule of this record it implements (`S1`–`S11`, `C1`–`C6`, section 3) and runs through its own Task Record, in the order of C6.
- Each format task updates `docs/project-file.md` when it changes the file: the anchors `ph_`, `conflicts[]`, `formerIds`, `minReader`, the hand-over record.
- music-core gains the tolerant reader, the field classes with their test, the three-way merge, the re-keying and the self-check; it stays pure and deterministic, with no I/O.
- The API changes to an envelope-only check with a create-only first upload, deployed after Andrei approves.
- Each app keeps a base copy and a revision per project, and pulls on open, focus and network return.
- The web stops offering a plain PDF piece; the iPad keeps plain PDFs in its score library.
- The iPad keeps working with no server and no account (F52).

## Alternatives considered

- **Two implementations from a specification** (Swift and C#). Rejected: Swift does not parse the file (F29, F36, V4), so there is no second implementation. Kept from it: the written contract and the golden files in CI.
- **The server as the source of truth** (server-side merge or an operation log). Rejected: the API can be older than the web (V10), a full reader refuses new values (V3), and the iPad must work without a server (F52). Kept from it: server-kept version history as a candidate for C5.
- **C1 without a phrase id**: each attached thing keeps only its span and one resolution rule places it (3 of 8 seats). Not chosen: it gives the same view to the player but no phrase object to attach to.
- **C1 auto-follow after re-analysis** with a "moved" mark. Rejected unanimously: it can move a resume loop to the wrong half of a phrase.
- **C2 split by kind** (a `conflictOf` sibling for leaf text, `conflicts[]` for the rest; 3 of 8). Not chosen: two rules instead of one, and an old build shows two notes with no reason.
- **C3** (A) defer every PDF, (B) sync marks, the PDF's hash and the page now with the bytes later (7 of 8), (C) a content-addressed blob store now. Andrei chose to keep plain PDFs on the iPad only.
- **C5** a server list of bad builds answering 409 "update to sync": last, for when builds exist beyond Andrei's.

## What this record changes in earlier records

| Record | Stands | Amended or superseded by this record |
| --- | --- | --- |
| [0014](0014-hosted-scan-server.md) Hosted scan server and API | the API and its endpoints, `If-Match` with 412, no project content in logs, the profile, sign-in, saving online as the player's choice | section 11: the API reads the file with `ProjectFile` before storing it → envelope only (S2); the first upload becomes create-only (S4). Section 14: on a 412 the client fetches and merges instead of stopping (S4, S5); "the last save wins" → the three-way merge (S5, S8, S9); Home's list fetched when the player asks → pulls on open, focus and network return (S10) |
| [0019](0019-native-ipad-app.md) Native iPad app | SwiftUI, music-core through the facade, projects as files on the device, releases from tags, the macOS workflow | releases: the rule of section 3 decides when an iPad release goes with the web; projects as files: the foreign-file rule (S5) for files copied or replaced in Files (V12, V23) |
| [0020](0020-music-core-facade.md) music-core facade | one facade, JSON in and out, the five C functions, the versioned contract | project ids numbered by the facade (`_next`, `p_NNNN`, F3) → GUIDs with deterministic re-keying (S3, C4); fixed `m_0001` and `v_0001` (F9) → random ids (S3); versioning: `minReader`, the tolerant reader and the larger version on write (S6, V2) |
| [0023](0023-pdf-pieces.md) PDF pieces | the PDF kept byte for byte, its pages and marks, recognising its notes into the same piece | a plain PDF piece stays only in the iPad's score library, is not synced and is not shown on the web (C3); a recognised PDF syncs as a project with notes while its PDF and marks stay on the iPad (C3, S5); the iPad's remembered page keyed by the project id (V26) moves with the re-keying |
| [0025](0025-two-studios.md) Two studios | everything | section 13: A1–A4 and the code architecture (13.4) are decided here; section 9.3 is met by section 3 of this record |

## Open

- Sync of plain PDFs between two iPads.
- The `merger` stamp (an older merger does not merge a newer one's output): for the core, web, iPad and advocates 1 and 2, against for the cost seat and the blind seat as a duplicate of `minReader`.
- The stored snapshot of derived data that advocate 3 asked for: only if the parity golden fails (S7).
- Server-kept version history (C5): Andrei's decision after the RU measurement of 2026-11-08.
- The rule when two anchors `ph_` still meet for one span; the council's proposal (the iPad seat) keeps the smaller id. Set by the merge task.
- Whether a recognised PDF's marks stay on the iPad (C3, the moderator's reading).
- The sync quota (0025, section 8).
