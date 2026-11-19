# 0030 · Originals, Notes, the piece: the sources model and project file version 16

Status: accepted, 2026-11-13; amended 2026-11-19 (section 6: a piece that is only a recording, version 1.1). Andrei asked for the sources to be rethought (thread «Исправить баги версии 0.5», 13:01 UTC: "we must manage not just a set of files but a set of linked sources of musical information and the musical representations obtained from them") and chose «формат A», version 16 of the project file in one step in version 0.6 (13:45 UTC). Builds on [0022](0022-score-and-performance.md) (performances), [0023](0023-pdf-pieces.md) (PDF pieces), [0024](0024-assembling-a-version.md) (assembly) and [0026](0026-project-continuity.md) (one project across the web and the iPad); amends them as the last section says.

Sources (project folder): Andrei's concept `product/architecture/sources-concept-andrei-2026-11-13.md` (verbatim) and the architect's proposal `product/architecture/sources-model-2026-11-13.md` (sections 3–7, 10 and 11). The assistant research `product/research/ai-companion-2026-11-14/report.md` (sections 4.6 and 4.11) shaped how kept variations and corrections carry their origin. The file format itself is in [`project-file.md`](../../project-file.md).

## Context

Two thirds of what Andrei asks for already existed in version 15, spread over special fields: `document` and the fixed id `src_pdf` (one PDF a piece), `originals[]` (descriptions, version 0.3), `source` with `scan` or `transcription` (the base notes), `performances[]` (other versions, 0022), `assembly` (0024), `partOf` and `readFrom`. Three of his principles were broken:

- **Replacing a PDF rewrote provenance.** «Заменить PDF» in a piece with notes (`Project.WithDocument`) put new bytes under `src_pdf` while `scan.from` still said `src_pdf`, so notes read from the old PDF looked read from the new one (likely bug 15 of 0.5).
- **Removing an original removed its notes.** Removing a version (`Project.RemovePerformance`) removed the recording's description, its notes and the bars taken from it, with no other choice.
- **No version of the original was kept.** "Read from the PDF" was worked out ("there is a PDF and a scan"), not recorded: no fingerprint of the bytes at the time, no kind of operation.

## Decision

### 1. Three layers

```
Piece (workspace)        a row of the library; lives until the player deletes it
├─ Originals[]            files as they are; their bytes never change
├─ Notes[]                derived notes, each with its own provenance, corrections and Check flags
└─ Work                   which Notes are the working notes, and everything the player made on them
```

1. **An original never changes.** Its version is the SHA-256 of its bytes (`sha256`); other bytes are always a new original. There is no separate version number.
2. **Notes keep their own music** (MusicXML or MIDI), so an original can go and its notes stay valid.
3. **Corrections belong to the Notes they correct**, with the Notes' own Check flags; they go only with their Notes.
4. **The player's work belongs to the piece** and stands in the ticks of the working notes: motifs, variations, ideas, phrases, marks, bookmarks, resume points, the assembly journal, settings. One Notes are the working notes (a pointer); any Notes can be viewed, heard and compared. Other Notes become the working notes through the assembly of 0024 («Сделать нотами» keeps «Ноты до сборки»), or directly with «Сделать рабочими» (section 4, amended 2026-11-14), so nothing the player made loses its time axis. Option C of `sources-0.3.md` (every Notes a workspace of its own) stays rejected.
5. **Marks on a PDF's pages belong to that original**, not to bars. Removing the PDF removes them from view; its tombstone keeps them.

### 2. One provenance for every Notes

```jsonc
"provenance": {
  "op": "pdfRecognition" | "audioRecognition" | "import" | "manual" | "assembly" | "split" | …,
  "from": [ { "original": "src_pdf", "sha256": "…", "pages": [1, 2] | "ms": [0, 245000], "project": "p_…" } ],
  "inputs": ["n_1", "perf_2"],     // only notes made from other Notes (assembly, split)
  "engine": "homr 0.7.0",           // the algorithm and its version
  "params": { … },                  // what changes the result; opaque to a build that does not know the op
  "at": "2026-11-13T12:29:00Z"
}
```

It replaces `scan.from`/`at`/`engine`, `transcription.from`/`at`/`engine`, `performances[].engine`/`from`/`at` and `readFrom.project`. `op` is an open name: a later operation (the assistant's, below) is read, kept and written back unchanged by a build that does not know it. `from[].sha256` is the original's fingerprint at the time of the operation; `pages`/`ms` the part of it used.

Kept variations (ideas, `observations` with a variation) belong to the Notes they were built on (`notes`, with their base note ids as today) and may carry a `provenance` of the same shape, so the assistant's variations (`"op": "assistant"`, with `request` and `model` in `params`, research 4.6) need no format change. A note correction may carry a `group` (corrections accepted together, undone as one) and an `origin` (`player`, `repair`, `assistant`); a correction without them is the player's own, alone.

### 3. States are worked out, never stored

| State | When | The player sees |
| --- | --- | --- |
| here | the bytes are on this device and the fingerprint matches | «с Santiago.pdf» |
| on another device | not here, held by another device (`held`) | «на iPad», «Привязать» |
| file changed | the original's fingerprint differs from the one the Notes were made from | «Файл изменился» → «Добавить как новый оригинал» |
| removed | the original is a tombstone | «Оригинал удалён · ноты остаются» |

### 4. Operations

- **Add an original** without recognising it: a PDF (any number of them), photos, a recording, MIDI, MusicXML.
- **Recognise** an original or a part of it: new Notes. **Recognise again** (another engine, other parameters): new Notes beside the old, which keep their corrections. Carrying corrections over is a later task (`pending-review.md`).
- **No «Replace»** for a PDF that has Notes: «Add another PDF» makes a new original. Retaking a page while a PDF has no Notes stays allowed (`document.replace`).
- **Remove an original, keep its notes** — the default: its bytes leave this device; a tombstone `{id, sha256, name, removed at, page marks}` stays. Adding the same file again (the same `sha256`) revives it with its marks. A linked original (`kept: link`) is only unlinked: **the player's own file is never touched** (Andrei, 2026-11-12).
- **Remove an original with its notes**: its Notes and their corrections go too. The working notes cannot be removed while they are working.
- **Remove Notes**: the original stays. The working notes cannot be removed.
- **Make working** («Сделать рабочими»; Andrei, 2026-11-14, for the iPad, which does not assemble): other Notes become the working notes without the assembly, and the working notes until then stay as other Notes with their corrections and Check flags, so the step is undone by making them working again. The work in ticks (phrases, bookmarks, resume points, ideas without a variation) stays with the piece; the marks on the score stay with the Notes they were drawn on; motifs, their variations and kept variations wait with the Notes they were made on, hidden until those are working again. Refused while bars are taken from versions or an assembly is applied; a compared version becomes the working notes only through the assembly. No assembly step is recorded ([`project-file.md`](../../project-file.md), "Other Notes").

The warning (English and Russian): "This original has notes made from it: «Santiago — notes from PDF», 14 corrections." / «Из этого оригинала получены ноты: …», with «Удалить только оригинал» (first) and «Удалить вместе с нотами».

### 5. Sync (0026)

The online copy is as before: no bytes, the descriptions, the Notes and the work (Stage 1 rule of `CLAUDE.md`). A removal travels as its tombstone: a tombstone wins over a description the other copy did not change since (the S8 union "with no deletions" would bring a removed original back). Notes made on another device meanwhile from that original stay, in the state "removed". Notes merge by id, as versions did; corrections inside Notes follow S9, note by note, while both copies have the same Notes with the same music. With a base copy (0026 S5) a removal or an undone correction on one side goes through; without one the merge is a union with no deletions (S4).

### 6. Version 16 of the project file

The file is laid out in the three layers: `originals`, `notes`, `workspace` ([`project-file.md`](../../project-file.md)). The reader converts every file of versions 1 to 15 itself and deterministically; the writer writes only version 16, and the same project gives the same bytes. Unknown fields of the file, the workspace, a Notes, an original and a provenance are kept and written back (S6). A file of a later version is refused, as before. The workspace also keeps the views the player hid from the switch at the top of the score (`hiddenViews`, ids of originals and Notes; never the working notes; Andrei, 2026-11-14), the player's data in the sync like the rest of the workspace. *Amended in version 1.1* (Andrei, 2026-11-19, plan v1.1 answers 2 and 4, finding 29): a piece may be only a recording, added as it is, free, on the device (the original `src_rec`, kept `device`), with no Notes until the player recognises it; the notes heard become its working notes. Version 16 judged a file with no Notes and no PDF original damaged, so such a piece rides version 17 of the file, the step [0034](0034-drafts.md) section 7 takes for drafts; a file of version 16 or earlier is judged as before ([`project-file.md`](../../project-file.md), "A piece with no notes").

In memory the piece keeps the shape both apps use today (`Project.Source`, `Scan`, `Transcription`, `Edits`, `Check`, `Performances`, `Document`): they are the working notes, the compared Notes and the first PDF. The facade's answers stay as they are; the `sources` answer adds the Originals and Notes with their states (step 5 below). So the web and the iPad keep working on version 16 before their own sources UI.

What stays as it is in version 16: the applied assembly (`assembly.applied` and `before`) remains in the workspace beside its journal; in the model it is Notes with `op: assembly` and `inputs`, and moving it into `notes` waits until the assembly is reworked (0028). `partOf` stays in the workspace: a part of a collection can be only a PDF, with no Notes to carry it.

### 7. Steps and release

1. This record and `project-file.md` (docs only).
2. music-core: the model, reading v1–v15 into it, writing v16, goldens `v16-*` and `v17-future`.
3. music-core: the operations of section 4 and the sync rules of section 5.
4. API: the envelope check only (S2), so the API never needs to read the musical content again.
5. Facade: `sources` answers the Originals and Notes with their states, and the mode switch (Original, Notes, Performance) in one call for the web and the iPad.
6. Web and iPad: the Sources sheet by the model (each studio decides what it shows, 0025); a studio that does not assemble still keeps other Notes and tombstones.

Builds before 0.6 refuse a version 16 file ("saved by a newer version"), so the web, the iPad and the API are released together (0026, section 3): the API first, then both apps on the same day. After the release a rollback only goes forward: before the tag every golden of versions 1 to 15 is read and a version 16 file round-trips.

## Decided without Andrei (decision log, 2026-11-13/14)

- An original's version is its `sha256`; there is no other number.
- «Remove the original only» is the default; removing with the notes is the second action.
- The working notes cannot be removed on their own while they are working.
- PDF page marks live in the tombstone and come back with the same file.
- Carrying corrections over to notes recognised again is not part of this step.
- The applied assembly and `partOf` stay in the workspace in version 16 (section 6).
- A transcribed stretch keeps its start in the recording (`transcription.sentFromMs`, `performance.recordingStart` in microseconds) where it was; `from[].ms` is written when both ends are known, so no timing is rounded.

## Alternatives

- **B. Fields within version 15**, as in 0.3–0.5. Rejected by Andrei: corrections stay with the base, Notes are unequal, and an old build that saves the file silently drops tombstones, so a removed original comes back.
- **C. Postpone.** Rejected: «Заменить PDF» keeps breaking provenance, and DNA, Insights and source comparison of 0.6 would be built on a temporary scheme.

## What this record changes in earlier records

| Record | Amended |
| --- | --- |
| [0022](0022-score-and-performance.md) | A version is Notes compared with the working notes; a recording version is an audio original plus Notes `audioRecognition`. Removing a version keeps its original unless the player removes it too. |
| [0023](0023-pdf-pieces.md) | A piece may hold any number of PDFs; «Replace» exists only for a PDF with no Notes. Notes read from a PDF name it and its fingerprint in their provenance. |
| [0024](0024-assembling-a-version.md) | The assembly's applied notes are Notes `op: assembly` in the model; in version 16 they stay in `assembly`. |
| [0026](0026-project-continuity.md) | S8: a tombstone wins over an unchanged copy of the original. S6: unknown fields are kept in version 16. Section 3: the 0.6 release carries a new file version, so the API goes first and both apps on the same day. |
