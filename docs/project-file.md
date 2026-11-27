# The project file (`.motiff.json`), version 18

**Status (2026-11-16):** the web app is archived ([0032](architecture/adr/0032-ipad-first-web-archived.md)); the file stays the contract between the iPad app, the API and the web when it returns. While the web is frozen, a new version ships with the iPad alone, and the frozen web refuses files newer than it knows.

**Version 17 (2026-11-19, [0034](architecture/adr/0034-drafts.md); version 1.1):** keeps the layout of version 16; a piece with no notes may be only its recording (finding 29, "A piece with no notes"), and the piece's drafts come (`workspace.drafts`, "Drafts") with music-core's drafts step; nothing else changes. Builds since version 1.1 write version 17.

**Version 18 (2026-11-27, #314, #315; version 1.6):** keeps the layout of version 16 and adds `minReader`, the oldest version that may read the file (18); a file of a later version whose `minReader` this build meets opens read-only ("Files of a later version"). Unknown fields are kept in every workspace entity, and an unknown per-piece setting or variation operation is kept rather than damage ("Unknown values"). The piece's phrase level (`phrases.granularity`) is gone: the score shows every level, and a file of version 17 or earlier loses the override on reading. A scan keeps the pages no engine read (`scan.unread`, `scan.read`) and the player's marks «что не так» (`scan.marks`). Builds since version 1.6 write version 18.

The project file is what a player keeps of a piece. Since version 16 ([0030](architecture/adr/0030-originals-notes-piece.md), version 0.6) it is laid out in three layers: the piece's **originals** (files as they are, never changed), the **notes** made from them (each with its provenance, corrections and Check flags) and the **workspace** (which notes are the working notes, and everything the player made on them). It is written and read by `MotiffLab.MusicCore.Projects.ProjectFile`, by hand, so the same project gives the same bytes. The web app saves it in IndexedDB and, when the player turns saving online on, sends it to the API ([0014](architecture/adr/0014-hosted-scan-server.md)); the iPad app reads and writes the same file through the facade. A file of any earlier version opens and is converted on reading; the rules are at the end ("Opening an older file").

Ticks are integers at 480 per quarter ([0002](architecture/adr/0002-canonical-time.md)). A span is `onset` and `duration` in ticks. Times are ISO 8601 with offset. Enum values in the file are the C# names (`Carmine`, `PianoRoll`) unless a section says lower case; the facade answers them in lower camel case.

## The document

```jsonc
{
  "format": "motiff-lab-project",
  "version": 18,
  "minReader": 18,                 // the oldest version that may read the file (version 18)
  "id": "p_…",                    // the project's id
  "fileName": "minuet.musicxml",   // the piece's file name: how the working notes' music is read (by its extension), and its title when nothing else names it
  "createdAt": "…", "lastOpened": "…",
  "originals": [ … ],              // every original of the piece, and the tombstones of removed ones
  "notes": [ … ],                  // every Notes of the piece: the working notes, other Notes and the ones compared with them
  "workspace": { … }               // the working notes and the player's work
}
```

Each layer is an object or array of objects; a reader that meets a field it does not know at the top level, in the workspace, in a Notes, in an original, in a provenance and (version 18) in a motif, a variation, an idea, a bookmark, a bookmark category, a resume point, a draft and the settings keeps it and writes it back unchanged after the fields it knows (0026 S6). Unknown fields elsewhere are still dropped on writing, except inside an entity kept whole as unknown ("Unknown values").

### Files of a later version (`minReader`)

A file of version 18 or later carries `minReader`, a whole number from 1. A file whose `minReader` is above this build's version (even one of version 18), or of a later version without `minReader`, is refused with "This project was saved by a newer version of Motiff Lab" before any other field is read (#315, criterion 4). A file of version 18 or later without `minReader`, or with one that is not a whole number of 1 or more, is damaged. A file of a later version whose `minReader` this build meets is read as this layout, every unknown field, setting and operation kept, and opens **read-only** (`Project.Newer`, `Project.IsReadOnly`): it is shown and played, and written again as it came, its `version` and `minReader` the larger of its own and this build's, but never saved (`ProjectFile.Save` refuses it, and so does every facade call that answers the file, with the error `readOnly`). The iPad says so in one line under the bar of every view of the piece. #315 criterion 3 (a later version alone opens read-only) conflicts with criterion 4; this build follows 4.

## Originals (`originals`)

Every original of the piece, in order: the first PDF (`src_pdf`), the file the notes were imported from (`src_file`), then the others in the order they were added; the tombstones of removed originals after them. Only descriptions go online; the bytes stay on the devices (version 0.3, local-first; Andrei 2026-11-11 21:20 UTC, amending 0026 C3).

```jsonc
"originals": [
  { "id": "src_pdf",                       // stable within the piece: "src_pdf", "src_file", a version's id for its recording or MIDI file, "src_book", "src_sound", …
    "kind": "pdf" | "images" | "audio" | "midi" | "musicxml",
    "name": "…",                           // optional: the player's name for it
    "fileName": "Nocturne.pdf",
    "folder": "Шопен/Ноктюрны",            // optional: where the file was inside the folder the player imported
    "sha256": "<64 lower-case hex digits>",  // optional: the fingerprint of the bytes as picked; the original's version
    "bytes": 123456,                       // optional
    "pages": 3,                            // optional: a PDF's or photos' pages
    "durationMs": 61000,                   // optional: a recording's length
    "addedAt": "…",
    "kept": "file" | "device" | "link",
    "held": [ { "device": "ipad" | "web", "at": "…" } ],   // optional: the devices that hold the bytes, each writing only its own mark
    "data": "<base64>",                    // only an original kept "file" whose bytes are not a Notes' music: the bytes, byte for byte
    "marks": { "ink": [ … ], "bookmarks": [ … ] },   // only a PDF or photos with the player's marks on its pages
    "removed": { "at": "…" } }             // only a tombstone: the player removed the original
]
```

- **`kept`**: `file`, the bytes are in this project file on the device and never online: a PDF's in `data`, an imported MIDI or MusicXML file's as the `music` of the Notes imported from it (so they are not kept twice); `device`, beside the project file on the device only (a recording's sound); `link` (version 0.5, «Импорт папки»), in the player's own folder (a folder on the computer, Files on the iPad, a cloud folder synced there), read in place. **The player's file is never changed**, renamed, moved or deleted, its properties never written and no folder made (Andrei, 2026-11-12); the bytes of a linked original are never written into the file, and neither is its page map then. The 50 MB limit of a PDF kept in the file does not apply to a linked one.
- **`sha256`** is the original's version: other bytes are always another original. A picked file is linked again to the original with its fingerprint (`Project.RelinkDocument`, «Привязать»); any other file is refused. A device that has the bytes works out a missing fingerprint once (`Project.Fingerprinted`).
- **`held`** is bookkeeping that only its own device writes: a pull takes this device's mark from its own copy and every other device's from the online copy. The iPad marks the PDF it keeps when it sends the copy online (`project.online`).
- **`folder`**: each name between the `/` is non-empty and not `.` or `..`, with no `\`; anything else is damage. A fresh look at the folder finds each file at its place and links again the ones moved or renamed (`LibraryRelink`).
- **`data`** of a PDF or photos must start with `%PDF-`; of another original (a second PDF, a MIDI or MusicXML file added without being imported, version 0.6) it is any bytes, never empty. Only the first PDF is in memory as `Project.Document`; the others' bytes are `Project.Sources.Data`. No `data` goes online: the online copy (`Project.ForOnline`) leaves it out and the page map with it (see "The PDF online"), and a pull keeps this device's bytes.
- **`marks`**: the player's marks on the PDF's pages ([0023](architecture/adr/0023-pdf-pieces.md)), apart from the marks on the notes, so the PDF view and the Notes view each keep their own: `ink` holds strokes in the shape of `workspace.marks.ink`, except that `bar` is the number of the page the stroke starts on (from 1), `u` its place across the page (0 to 1) and `v` its distance below the page's top edge in the page's own spaces (a hundred and twentieth of its width); `bookmarks` holds `{ "id": "pb_0001", "page": 3, "name"?: "Coda", "createdAt" }`, at most one per page, in page order. Left out when there are none. Every PDF or photos original carries its own marks (`Project.PageMarksOf`; `src_pdf`'s are `Project.DocumentMarks`), so a second PDF takes the pen apart from the first; a pull never clears the marks of an original the online copy does not describe.
- **`removed`**: a tombstone. The bytes left this device (a linked file was only unlinked), the description and the page marks stay, so the Notes made from it keep their provenance and the state "removed", and adding the same file again (the same `sha256`) revives the original with its marks. A tombstone has no `data` and no `held`.
- An unknown `kind`, `kept` or `device` keeps the description whole as an unknown entity (S6).

**A piece with no notes** is a piece when it has a live PDF original (in the file, linked in a folder or on another device): the library lists it (`project.summary`), and the facade's `open` answers `originalAway` until the file is here (`Project.RelinkDocument`, by fingerprint). Since version 17 (version 1.1, finding 29; Andrei, 2026-11-19) it is a piece too when it has a live recording original: a recording added as it is, free, on the device (`Project.CreateRecording`: the original `src_rec`, kind `audio`, kept `device`, held by the device that added it, with its `durationMs`, `sha256` and `bytes` when known; the piece's `fileName` is the recording's name with `.mid`, so its title is the recording's name and stays when its notes are heard). It has no notes until the player recognises the recording («Распознать · Pro»), and the notes heard become its working notes. A file with no Notes and neither a live PDF nor a live recording original is damaged, and so is a file of version 16 or earlier with no Notes and no PDF original, recording or not: the builds that wrote one knew no such piece.

### The PDF online

Since version 0.5 (Andrei, 2026-11-12, 17:45 UTC, amending 0026 C3) a piece that is only a PDF syncs too, without the PDF: its online copy (`Project.ForOnline`) is the piece without `data` and without the working notes' page map, with every original described (the PDF with its `sha256`, worked out then if the file had none, its `kept`, `folder` and `held`) and the page marks. A device without the PDF keeps the copy as it comes and opens it once the same file is linked there. The device that has the PDF keeps it, and its page map, when the online copy comes back (`ProjectSync.Pulled`). Page marks are written by one device only (`Project.WritesDocumentMarks`): the device that has the PDF, and the iPad when both have it; a pull keeps the writer's own marks and gives every other device the online copy's, except that an online copy with no marks never clears a device's marks. The web shows such marks read only.

## Notes (`notes`)

Every Notes of the piece: the working notes first, then the other Notes (read again from a PDF, or the working notes until other Notes were made the working ones) in the order they were added, then the ones compared with them (the versions of [0022](architecture/adr/0022-score-and-performance.md)) in the order they were added. A Notes keeps its own music, so it stays valid when its original is removed.

```jsonc
"notes": [
  { "id": "n_1",                             // stable within the piece; a compared Notes keeps its version id ("perf_1")
    "provenance": { … },                     // where the notes came from (below)
    "music": "<base64>",                     // the notes as made: the working notes' MusicXML or MIDI, read by the piece's "fileName"
    "edits": [ … ],                          // the working notes: their corrections ("Note corrections")
    "check": [ … ] | null,                   // the working notes: their Check flags ("Check")
    "scan": { "confirmedBars": [3, 7], "pages": [ … ], "unread": [4], "read": [2], "marks": [ … ] },  // notes recognised from a PDF or photos
    "transcription": { "bars": { … } | null, "seams": [ … ], "sentFromMs": 754000 },   // notes heard in a recording
    "pageMap": { "original": "src_pdf", "starts": [0, 15360, 29760] } },   // where each page of a PDF starts in these notes
  { "id": "n_2", "name": "homr 0.8",          // other Notes: name optional
    "fileName": "Nocturne.musicxml",         // what their music is read by, as the piece's fileName for the working notes
    "provenance": { … }, "music": "<base64>", "edits": [ … ], "check": [ … ] | null, "scan": { … }, "pageMap": { … },
    "marks": { … } },                        // optional: the marks on the score drawn while they were working, as workspace.marks
  { "id": "perf_1", "name": "Live at the National Gallery",
    "provenance": { … },
    "music": "<base64>",                     // a compared Notes: MIDI
    "performance": { … } }                   // how it is compared with the working notes ("Compared notes")
]
```

- **The working notes** are the Notes `workspace.working` names; they carry `edits` and `check`, and never `performance`. A piece with no notes yet (a PDF to read as it is) has no working notes and an empty `notes`.
- **`music`**: the working notes' bytes are read by the piece's `fileName`, as before: MusicXML (`.musicxml`, `.xml`, `.mxl`) or MIDI (`.mid`, `.midi`); a scan's notes are its MusicXML, a transcription's the engine's MIDI. A compared Notes' music is MIDI: the engine's MIDI, the player's MIDI file as it was, or a score written out as MIDI. The imported file of a piece imported from a file is the working notes' music byte for byte, so its original (`src_file`, kept `file`) has no `data` of its own.
- **`scan`** (with `"op": "pdfRecognition"`): the bars the player checked and said look right (`confirmedBars`), and the raw numbers of the scan server's report on each page (`pages`, version 0.4): `{ "page", "rotation", "sharpness", "contrast", "staves", "noteheads", "barLines", "spacePx", "measures" }`, never the music. A page's quality (good, weak, poor) is worked out from them on every read (`PageReport.Quality`), so the thresholds change without a migration; a field a report lacks reads as 0; `page` is the page's place in the notes, from 1. On open the bars of a weak page are less sure (0.5) and those of a poor page flagged `PageQuality`. `pages` is left out when there are no reports. `unread` (version 18) are the PDF pages, from 1, no engine read when the notes were put together, and `read` the ones of them put in since (`scan.insert` with `pdfPage`); both are distinct and sorted, never share a page, and are left out while `unread` is empty, so the iPad can offer the pages still to read after the scan queue is gone (`check` answers `unreadPages`, `readPages`). `marks` (version 18, «Как напечатано» step 3) are the player's marks «что не так», what the page prints where the reading went wrong, by bar, then kind, then staff: `{ "kind", "bar", "last", "staff", "value" }`, `kind` one of `meter` (`value` "5/4", from `bar` on to the next meter mark, `last` = `bar`), `key` (`value` the sharps or flats, "-7" to "7", likewise from `bar` on), `clef` (`staff` from 1, `value` "treble" or "bass"), `octave` (`staff`, `value` "8va" or "8vb"), `triplets` (`value` "yes" or "no"), `hands` and `badlyRead` (no `value`); `staff` only on a clef or octave mark. They change no note: the next reading of the same pages (`sources.notes.recognise`) puts them in and the new Notes keep them, on the bars their music moved to (`ReadingMarks`). Over bars, a mark of a kind and staff joins those of the same value it overlaps and takes its bars from those of another value, so each bar takes it once. The bars of a mark carried to notes read again are those of the new notes. A mark of a kind this build does not know whose kind is a name (a later build's) is kept as written and written back (S6); a kind that is no name, a known kind with a value it cannot say, or such a mark where the reader keeps no unknown values (other Notes: kept whole as an unknown entry; a file before version 16) is damage. Left out when there are none; files without it read as before.
- **`transcription`** (with `"op": "audioRecognition"`): the bars and tempo worked out for the notes heard ([0016](architecture/adr/0016-bars-for-transcribed-notes.md)), `null` to keep the engine's 120 BPM and 4/4: `{ "origin", "sixteenthMicroseconds", "signature", "barStarts", "stepsPerBeat", "beats", "meters"? }`. `meters` (version 0.5) says where the bars change signature: each change is at one of `barStarts` after the first (`at`, in 16ths from the origin), after the one before, with bars of whole 16ths, and may name a signature the grid's search does not offer, such as `7/8`; any other change is damage. `seams` (when a long recording went in parts): where each part after the first takes over, in milliseconds from the start of the stretch sent, ascending; the Check layer flags the bars on either side (`PartSeam`); left out for one part. `sentFromMs` (version 0.5): where, in the recording file, the stretch sent started, 0 or more; the workspace's `partOf.ms` counts from it, so another device that has the file (the same `sha256`) finds the piece's sound there; left out when not kept.
- **`pageMap`**: the rough link the PDF and Notes views switch by: where each page of `original` starts in these notes, in ticks, page 1 at 0, each page after the one before. A page starts at the first bar the scan server marked with `<print new-page="yes"/>`, or, without such marks, the bars are shared evenly between the pages. With `"project": "p_…"` the PDF is in another piece: notes read from a PDF piece into a project of their own between 2026-11-05 and 2026-11-07 (0023); no build writes such notes any more. Without `project`, the original must be this piece's PDF and the map is written only while the PDF's bytes are here.
- **Other Notes** (version 0.6) carry everything the working notes do, and `fileName`, so they can be made the working notes with nothing lost (`NotesOperations.MakeWorking`, «Сделать рабочими»; Andrei, 2026-11-14, amending 0030 section 1). Made working, the piece keeps its own `fileName` with the extension of theirs, and the working notes until then become other Notes under their id with the piece's `fileName`. Motifs, their variations and kept variations made on the notes until then wait with them: a motif and an idea name them (`notes`), they stay in the file and are left out on opening until those notes are working again. The marks on the score (`workspace.marks`) stay with the notes they were drawn on: the notes until then take them as their `marks`, and the Notes made working bring theirs (sources sheet, 2026-11-14). Phrases, bookmarks (a title still by their bars retitled), resume points, ideas without a variation and the versions compared (anchors, `dismissed`, their Check flags) stay with the piece and follow the music into the new working notes by bars (v1.5 c-11b, `FollowedWork`: a place in a bar with no match is kept where the music before it went); the file's fields do not change. Refused while bars are taken from versions or an assembly is applied.
- An entry with `edits` or `check` and no `fileName` that is not the working notes, or one that is neither the working notes nor other Notes nor has `performance`, comes from a later build: it is kept whole as an unknown entity and written back where it stood.

### Provenance

One shape for every Notes ([0030](architecture/adr/0030-originals-notes-piece.md), section 2), and for a kept variation ("Ideas"):

```jsonc
"provenance": {
  "op": "pdfRecognition",            // pdfRecognition (a PDF or photos read by a scan engine), audioRecognition (a recording heard),
                                     // import (a MIDI or MusicXML file), manual, assembly, split, or a later build's own name
  "from": [ { "original": "src_pdf",     // an id in this piece's originals (or, with "project", another piece's)
              "sha256": "…",             // optional: the original's fingerprint when the notes were made from it
              "pages": [1, 2],           // optional: the pages used, from 1, both ends included
              "ms": [0, 245000],         // optional: the stretch of a recording used, in milliseconds, from < to
              "project": "p_…" } ],      // optional: the original is another piece's
  "inputs": ["n_1", "perf_2"],       // optional: the Notes these were made from (assembly, split)
  "engine": "homr 0.7.0",            // optional: the algorithm and its version as it reported them; required for pdfRecognition and audioRecognition
  "params": { … },                   // optional: what changes the result; kept as it is
  "at": "2026-11-13T12:29:00Z"       // optional: when
}
```

- `op` is a name (ASCII letters and digits, starting with a letter). An `op` this build does not know is kept with everything in its provenance and written back unchanged; the Notes then open as notes of their own, as an import's do. A later build's assistant variations and notes use `"op": "assistant"` with `request` and `model` in `params` (research 4.6), with no change to the format.
- `from` is empty when the original is not known (notes read before version 0.3, or from photos not kept). Its first entry is the original the notes were made from, as the facade names it.
- `from[].sha256` is what makes "file changed" visible: an original whose fingerprint is not the one its Notes were made from (the player's file in the folder was edited, or a PDF was replaced before version 16).
- `pages` or `ms` on the original a part of a collection was made from repeats the workspace's `partOf` (below); a stretch whose end is not known (a recording sent from `sentFromMs` on) has no `ms`.
- The states the apps show — here, on another device, file changed, removed — are worked out on every open from `from`, the originals and this device, never stored (0030, section 3).

## The workspace (`workspace`)

The working notes and everything the player made on them, in their ticks.

```jsonc
"workspace": {
  "working": "n_1",                 // the working notes, an id in "notes"; left out for a piece with no notes yet
  "metadata": { "title": "…", "composer": "…", "tags": ["…"], "playlists": ["…"] },   // optional: only what the player set in the library
  "partOf": { "original": "src_book", "pages": [6, 11] },   // optional: only a piece made from a collection; or "ms": [from, to]
  "editsOff": true,                 // optional: only while the player has turned the corrections off
  "tempo": { "quarterNotesPerMinute": 92 },   // optional: only once the player set the piece's tempo
  "motifs": [ … ],                  // the working selection first; a motif made on other Notes than the working ones names them ("notes": "n_1")
  "variations": [ … ],              // chains of steps on a motif; an optional "key" { "fifths", "mode" } the steps read
  "observations": [ … ],            // ideas
  "phrases": { "added": [ticks], "removed": [ticks],
               "short": { "added": [ticks], "removed": [ticks] },       // cuts inside phrases at the Short level (0018, section 3)
               "sentences": { "added": [ticks], "removed": [ticks] } },  // phrase boundaries where a sentence must end, and ones it runs on across
  "marks": { … },
  "bookmarks": { … },
  "resumePoints": [ … ],
  "assembly": { … },                // absent while the piece has no assembly
  "drafts": [ … ],                  // the piece's drafts (version 17); absent while it has none
  "settings": { … },
  "hiddenViews": ["src_pdf_2", "perf_1"]   // optional: the views hidden from the switch, only while the player hid one
}
```

A save never loses a saved variation (#821): the facade writes the selection as the working motif (the first motif, which the piece opens on) and the chain shown under the id of the variation it was opened from, written over only its own earlier save; every other variation stays as it was. A chain that goes (another chip tried, the same passage chosen again, the variation left, a correction) leaves its saved copy as it was, and the next one is saved beside it; trying chips without a save in between adds no variation. Once the player chooses another passage or brings back an idea on one, a working motif that has variations saved stays as it was, with all of them, and the passage is saved beside it under a new id; the same passage again stays the working motif.

### Views hidden from the switch (`hiddenViews`)

The switch at the top of the score lists every view of the piece: each PDF original with the Notes read from it, each other Notes, and each recording with its sound as a Performance view (Andrei, 2026-11-14), and since version 17 each draft ("Drafts"), after them. The player can hide any of them from the switch in the Sources sheet, except the working notes. `hiddenViews` is the ids hidden, originals', Notes' or (version 17) drafts', distinct and in the order of their ids; a draft deleted leaves it; written only while one is hidden. An id this build does not know is kept, so a view a later build shows stays hidden; an id that names the working notes is kept and not applied while they are the working ones. A value that is not a list of distinct texts is damage. In the sync it is the player's data, as the rest of the workspace: the copy saved last says it.

### Title, composer, tags and playlists (`metadata`)

What the player wrote about the piece in the library (version 0.2; Andrei approved it on 2026-11-11): its title, its composer, their own tags and the playlists it is on. Each is the player's: nothing is copied into it from the source file. Every text is one line: runs of white space are one space and the ends are trimmed, so an empty text is none. Tags and playlists keep the player's order and are distinct whatever the letter case ("Exam" and "exam" are one tag, the first spelling kept). A playlist is its name: it exists while a piece is on it. A field is written only when it is set, and `metadata` only when one is.

What a library card shows (`Project.Details`, the facade's `title`, `composer`, `tags` and `playlists`) is the player's value where there is one, else what the imported file says (a MusicXML score's `<work-title>`, else `<movement-title>`, else a title credit; its `<creator type="composer">`, else a composer credit), else for the title the file's name without its extensions. The file's values are read from the music on every summary and never written into `metadata`, so clearing the player's value shows the file's again. A scanned or transcribed piece and a PDF piece take no title or composer from their notes.

### Pieces of a collection (`partOf`)

Where a piece made with «Пьесы в файле» (version 0.4) is in its collection: the collection's original, an id in this piece's own `originals`, and either `pages` (the pages the piece takes, from 1, both ends included) or `ms` (the stretch of the collection's recording, in milliseconds, from < to). Each piece is an ordinary project with its own notes and bytes; `partOf` is only the link. Exactly one of `pages` and `ms`; one with neither (another kind of place, from a later build) reads as none. `PieceParts` sets it on each part: pages for a PDF, photos or a scan with pages, `ms` for a recording heard (a recording that describes no original is then described as `src_book`, kind `audio`, held by no device), none for a score cut by bars without pages. It stays in the workspace, not in a provenance, because a part can be only a PDF, with no notes.

A piece cut by time from a concert heard (`PieceParts` with `FreshBars`, `HeardCut`) has bars and a tempo of its own, found from its stretch alone, its own Check flags and `partOf` with `ms`. Its own stretch of the sound, cut on the web, is described as `src_sound`, kind `audio`, kept `device`, `<title>.wav` with `bytes`, `durationMs` and `sha256` of the WAV, held by the device that cut it (`PieceParts.WithSound`). The pieces layer is bookmarks in the category with the reserved id `pieces` (`PieceLayer`).

### Corrections off (`editsOff`)

«Мои правки: вкл / выкл» (version 0.5): the piece opens with the working notes as made, their `edits` kept as they are. Written only when true; `false` reads as left out, any other value is damage. The facade opens a piece so when `open` asks for `"edits": "piece"`.

### The player's tempo (`tempo`)

The tempo the player set for the piece (version 0.5.4, #511 section 17), such as "♩ = 92" read from the printed notes when the scan could not read it: a whole number from 20 to 300; any other value is damage. It replaces the tempo at the start, whatever its source (notated, inferred from a recording, or the default assumed), and is the player's from then on: played, marked on the score as "♩ = 92" and named as the player's in the tempo control (`MotiffLab.MusicCore.Playback.PlayerTempo`). A tempo the notes state later on stays as stated; the tempos worked out from a recording follow it in proportion. Nothing the file imported changes: clearing it plays the piece as it states again. The playback tempo the player chooses while practising (25 % to 150 %) is not this field: it stays a whole percentage on each resume point (`tempoPercent`).

## Sync

The online copy is the file without the bytes of originals (0026 C3, as amended), so the Stage 1 rule of `CLAUDE.md` holds. What each field is in the merge (0026 S8) is `ProjectSync.Fields`; a test fails for a field with none.

- **Originals** are the player's data: a pull unites both copies by fingerprint, else by id (a different original with an id the other copy uses gets the next free one, `src_a_2`); `held` marks as above. A tombstone wins over a live description of the same original (by fingerprint, else by id) unless that copy added or held it again after the removal (its `addedAt` or a `held` mark later than `removed.at`), which revives it; Notes made from it meanwhile on another device stay, in the state "removed".
- **Notes** merge by id (`ProjectSync.Resolve`): the copy kept takes the other Notes and versions only the other copy has, and the corrections of notes only the other copy corrected, in the working notes and in other Notes alike, while both are the same Notes with the same `music` (S9 and its source guard). The same note corrected on both copies is a conflict: the copy saved last keeps its corrections, the other's stay in the previous copy kept beside. With the copy both last agreed on (`base`, when the app keeps one, 0026 S5), Notes removed and corrections undone on one side since then go through; without it the merge is a union with no deletions (S4), so they come back. What an import brought (`scan.pages`, `transcription.bars.meters`, `transcription.sentFromMs`) is kept from this device when the online copy has none and the notes are the same (the same `music`).
- **The workspace** is the player's data, except `editsOff` and `lastOpened` (bookkeeping, the copy saved last says it) and `check` (derived).
- **Drafts** (version 17) merge by id, as Notes do: the copy kept takes the drafts only the other copy has; the same draft changed on both copies is a conflict, the copy saved last keeps its draft and the other's stays in the previous copy kept beside. With `base`, a draft removed on one side since then goes through; without it, it comes back.

## Marks (`marks`)

Everything the player writes on the score, kept with the piece ([0018](architecture/adr/0018-redesign.md), prep item 1). The pencil layer and the project file share one shape for strokes, so the web app and the iPad app pass them through unchanged (`ProjectFile.WriteInk` / `ReadInk` give the layer's own document, `{"version": 2, "marks": [stroke, …]}`).

```jsonc
"marks": {
  "ink": [                      // strokes, in the pencil layer's shape (Scripts/score/pencil-marks.ts)
    { "id": "m…", "tool": "pen" | "highlighter",
      "colour": "graphite" | "carmine" | "forest" | "ochre" | "amber",   // the design kit's inks; lower case here
      "size": 0.16,             // thickness in staff spaces
      "bar": 1920,              // the tick where the bar the stroke starts in begins
      "u": 0.25, "v": -1,       // where it starts: share across the bar, staff spaces below the bar's top line
      "points": [dx, dy, pressure, …],   // the points after the first, three numbers each, in staff spaces
      "pressure": 0.5 }
  ],
  "notes": [                    // margin notes: text about a passage
    { "id": "n_0001", "onset": 0, "duration": 3840, "text": "…", "createdAt": "…" }
  ],
  "highlights": [               // a passage, and the notes in it, under the highlighter
    { "id": "h_0001", "onset": 0, "duration": 1920, "noteIds": ["…"], "colour": "Amber" }
  ],
  "fingering": [                // one finger per note
    { "noteId": "…", "finger": 3 }
  ]
}
```

A stroke's anchor is its bar's start tick, so marks follow the piece through a new layout, the as-written and as-played scores and the PDF. Notes, highlights and fingering anchor to a span or to note ids, which the importers keep stable.

A stroke begun in the margin of a line (version 0.4, same shape) is kept against the line's first bar with `u` below 0, or its last bar with `u` above 1. The apps draw such a stroke from that edge of whatever line its bar is on in the layout shown, so after another layout it stays in the margin rather than over a neighbouring bar's notes; a build before 0.4 draws it from its bar's edge, the same place while the bar is still at the line's edge. Music-core keeps every stroke an app hands in thinned (`InkThinning`: points within 0.05 staff spaces of the line through the others, and within 0.1 of its pressure, are left out); a file is read with its points as written.

## Bookmarks (`bookmarks`)

The contents of the piece in the player's own order (prep item 2): four built-in categories, each with an ink of the design kit, and any number of the player's own.

```jsonc
"bookmarks": {
  "categories": [
    { "id": "practise",    "ink": "Carmine" },    // built-in: no name in the file; named in the app's language
    { "id": "performance", "ink": "Forest" },
    { "id": "structure",   "ink": "Graphite" },
    { "id": "teacher",     "ink": "Ochre" },
    { "id": "c_0001", "name": "Exam", "ink": "Forest" }   // the player's own: a name, any ink but amber
  ],
  "items": [                    // in the player's order; the app also sorts them by place
    { "id": "b_0001", "title": "Theme", "category": "structure",
      "onset": 0, "duration": 3840,
      "fromPhrase": true,       // made from a phrase; since 1.2 it stays put when a phrase boundary changes
      "createdAt": "…" }
  ]
}
```

Bookmarks may overlap. A bookmark keeps its span when the phrases change, one made from a phrase (`fromPhrase`) too (the phrases spec, version 1.2; until then it took the span of the phrase that contained its onset). Removing an own category moves its bookmarks to Practise; built-in categories cannot be removed or renamed.

## Resume points (`resumePoints`)

Where the player can come back to (prep item 3). One is made when a piece is opened and brought up to date as the player works; the player can name any number of them. There is no intent and no "end": a resume point is a place, not a session.

```jsonc
"resumePoints": [
  { "id": "r_0001", "name": "Coda",         // name optional
    "at": 7680,                              // the tick in view
    "passage": { "onset": 7680, "duration": 3840, "filter": { "hand": "Right" } },   // the selection; absent for none
    "focused": false,                        // phrase focus on
    "overlays": ["PianoRoll", "Insights"],   // open panels: PianoRoll, Insights, Contents, Check
    "tempoPercent": 70,                      // 25–150
    "hands": "Left",                         // absent for both
    "loop": true,
    "createdAt": "…", "lastActive": "…" }
]
```

## Ideas (`observations`)

An idea is an observation kept on a passage ([0009](architecture/adr/0009-exploration-layer-and-notebook.md)): a variation, the steps that made it, a note, and the fact it came from (0018, prep item 4).

```jsonc
"observations": [
  { "id": "o_0001", "name": "Give the melody more space",
    "factId": "motif.contour.arch",          // the fact of the Understand digest the experiment was built on; optional
    "note": "Airier.",                       // optional
    "createdAt": "…",
    "phrase": { "onset": 0, "duration": 3840, "filter": { … } },
    "experiment": { "id": "…", "alternative": 0 },   // optional
    "variation": { … },                      // the kept chain, with the ids of the notes it was built on; optional
    "notes": "n_1",                          // with a variation: the Notes it was built on
    "provenance": { … } }                    // optional: how it was made, in the shape of a Notes' provenance
]
```

Any number of ideas can sit on one phrase. Which phrase an idea belongs to after the phrase level changes is worked out, not stored: the phrase that contains the idea's onset, else the phrase it overlaps most (`Observation.Home`).

A kept variation belongs to the Notes it was built on (`notes`, research 4.6), beside their corrections; it is not Notes of its own. Every idea of version 16 is built on the working notes, and a file of an earlier version reads so. `provenance` is left out for an idea the player made in the app, except a variant (below); a later build's assistant writes `"op": "assistant"` with `request` and `model` in `params`, and `inputs` naming the Notes, which this build keeps and writes back.

### A variant of a phrase («Что если», version 0.6)

A variant the player kept from the «Что если» panel (the AI companion study, sections 2.4 and 4.6; `MotiffLab.MusicCore.Assistant.Variants.VariantRecord`) is an idea whose `variation` is its whole chain of steps from the phrase's notes, whose `notes` are the Notes it was built on, and whose `provenance` is its record, with no field of its own:

```jsonc
"provenance": {
  "op": "variation",                 // a «Что если» chip, no AI; "assistant" when the assistant chose the steps (0028)
  "from": [],
  "params": {
    "chip": "v12.pedal",             // optional: the Try chip that made the last step
    "request": "…", "model": "…",    // optional: "assistant" only, the player's words and the model that chose the tool calls
    "seed": 0,                       // the seed the steps ran with
    "parent": "o_0001",              // optional: the variant it was made from, an idea's id
    "barChange": 1,                  // optional: whole bars added (or, negative, taken away) at its end, -8 to 8
    "base": [ { "id": "n_0024", "pitch": 45, "onset": 2880, "duration": 120, "velocity": 80 }, … ]
                                     // the notes under it when it was made, in the order of variation.noteIds;
                                     // a note that was missing then has its id alone
  },
  "at": "2026-11-15T10:01:00Z"       // when it was made
}
```

- The keys are written in this order, only those that are set, so the same variant gives the same bytes.
- `base` is what tells that the notes under a variant changed, and what it is made again over: the working notes when they still hold those notes as they were, else the notes before the assembly (`assembly.before`), else the working notes with those notes given back their values. Taking a variant into work is a carry of the assembly journal whose `source` is `variant:` and the idea's id; after a reload the carry's music is the variant made again so, from this record.
- Whether a variant is in the working notes is worked out from the journal, never stored.
- An idea with no record (any earlier file, a provenance of another `op`, or `params` that do not read as a variant's) reads as before: its steps over the notes under its passage as they are now. This build keeps any provenance as it came and writes it back.


## Check (`check`)

What the scan server, the transcription or a performance left for the player to look at, as data, in one format for all three (prep item 6).

```jsonc
"check": [
  { "bar": 12, "source": "Scan" | "Transcription" | "Performance",
    "reason": "BarTooLong" | "BarTooShort" | "StavesDiffer" | "OutOfPianoRange" | "GraceNotes"   // scan
            | "PartSeam" | "TempoUncertain" | "DenseCluster" | "HandsGuessed"                     // transcription
            | "RecordingOctave" | "RecordingBarLength"                                            // a performance (version 13)
            | "PageQuality"                                                                         // scan: a bar on a poor page (version 0.4, within 15)
            | "EnginesDisagree"                                                                     // scan: no two engines agree on a staff (version 1.3, within 17)
            | "CorrectionUnfit"                                                                     // scan: a correction not carried to notes read again (version 1.5, within 17)
            | "MarkUnfit"                                                                           // scan: a mark «что не так» notes read again could not take (version 1.6, 18)
            | "ReadAgain",                                                                          // scan: a bar marked read badly, read again from another engine (version 1.6, 18)
    "resolved": false }
]
```

`EnginesDisagree` (version 1.3, within version 17, Andrei 2026-11-24; [0035](architecture/adr/0035-scan-jobs-four-engines.md), section 5) flags a bar of a page several engines read where no two of them read some staff alike. The merge marks such a measure in the scan's MusicXML with a hidden direction (`<other-direction print-object="no">Motiff Lab scan: engines disagree</other-direction>`), so the flag is worked out from the notes' bytes like the other scan reasons; which engines gave each staff is not kept in the file. A build before 1.3 keeps the flag whole as an unknown one and writes it back (S6).

`CorrectionUnfit` (version 1.5, within version 17, «Как напечатано» step 2, Andrei 2026-11-26) flags a bar of other Notes read again from the PDF (`sources.notes.recognise`) where a correction of the working notes was not carried to them: the new reading gives the note neither as the player found it nor as they corrected it. The correction stays with the notes it was made on, which stay beside; the flag is the bar where its music lies in the new reading. It is stored with those Notes' other flags, which are then written out (`check` is no longer null for them). A build before 1.5 reads other Notes with no tolerance for an unknown value (`NotesCodec.ReadOtherNotes`), so it keeps those Notes whole as an unknown entry: it does not list them in Sources, and writes them back unchanged, nothing lost. Once they are made the working notes, such a build reads the working notes tolerantly and keeps the flag whole as an unknown one (S6).

`MarkUnfit` (version 1.6, project file 18, «Как напечатано» step 3) flags a bar of other Notes read again from the PDF where a mark «что не так» of the working notes could not be put in: the bar's music has no bar in the new reading, or the reading has no such staff (a hand mark on one staff, a clef or octave mark on a staff it does not have) or writes the staff in a clef other than treble or bass. A triplets mark does not fit a bar where a staff adds up neither to the bar nor, re-timed by the mark, to it; a bar read badly where no engine's reading of it was taken in place of the earlier one. The mark stays on the new Notes; it is stored with their other flags, as `CorrectionUnfit` is.

`ReadAgain` (version 1.6, project file 18, «Как напечатано» step 3) flags a bar the player marked as read badly that the merge of its page read again took from another engine than the one taken before (`scan.join` with the page's readings and its bad bars). The merge marks the measure with a hidden direction (`<other-direction print-object="no">Motiff Lab scan: read again from another engine</other-direction>`), so the flag is worked out from the notes' bytes as `EnginesDisagree` is; a build before 1.6 keeps it whole as an unknown flag (S6).

`null` only in a file written for a project that was never opened since it was read from an older version; opening such a project works the flags out from the piece (`CheckFlag.FromScan`, or `CheckFlag.FromTranscription` for a transcription) and marks the bars the player had confirmed as resolved (`scan.confirmedBars` of the working notes).

A transcription's flags (prep item 12) are worked out from the notes the engine heard and the bars worked out for them: `PartSeam` for the bars on either side of a seam; `TempoUncertain` for a bar where at least half of four or more onsets sit more than 30 ticks off both the 16th and the triplet-8th grids; `DenseCluster` for a bar with four onsets within a 16th or a chord of three or more notes a second apart; `HandsGuessed` for a bar with two or more notes whose hand was guessed with confidence below 0.6. A reason other than `PartSeam` that would flag more than a quarter of the bars is left out, since it then says more about the recording than about a bar. Resolving a flag on a scanned piece also confirms the bar in `scan.confirmedBars`, so older readers see the same thing.

A performance added to a scanned piece raises its own flags (source `Performance`, [0022](architecture/adr/0022-score-and-performance.md), section 6): `RecordingOctave` where at least three quarters of a hand's notes are played 12 semitones from the scan for two bars or more, and `RecordingBarLength` where the scan flagged a bar too long or too short and the performance measures it at the meter's length at the neighbouring bars' tempo. Removing the last performance drops them.

## Compared notes (`performance`)

Notes compared with the working notes ([0022](architecture/adr/0022-score-and-performance.md), section 8): the anchors that line them up with the working notes, the bars the player said are not a difference, and the bars taken into the working notes. A recording's sound is never in the file; it stays on the device under the Notes' id.

```jsonc
{ "id": "perf_1", "name": "Live at the National Gallery",
  "provenance": { "op": "audioRecognition", "from": [ { "original": "perf_1", "sha256": "…" } ], "engine": "transkun 2.0.1", "at": "…" },
  "music": "<base64>",
  "performance": {
    "source": "Recording" | "Midi" | "Score",   // Score: a written score such as MusicXML, kept as MIDI
    "recordingStart": 0,                 // microseconds into the recording where the transcribed stretch starts
    "anchors": [[0, 0], [1440, 1500000]],   // [score tick, microseconds], both strictly increasing
    "dismissed": [7],                    // bars marked "not a difference"
    "taken": [
      { "bar": 5,
        "removed": ["n_0031"],           // working notes the performance does not play
        "added": [ { "id": "perf-perf_1-n_0042", "pitch": 79, "onset": 4560, "duration": 120,
                     "velocity": 64, "track": 0, "hand": "Right" } ] }   // hand absent when unknown
    ],
    "merge": {                           // optional: the merge draft's choices, read and written back as they were
      "appliedAt": "2026-11-07T12:30:00.0000000+00:00",   // null after "Undo the merge"; the choices stay
      "bars": [ { "bar": 5, "choice": "Version" },        // Mine, Version, Own or NotADifference
                { "bar": 7, "choice": "Own", "own": { "bar": 7, "removed": [ … ], "added": [ … ] } } ] } } }
```

The provenance's `op` follows `source`: a recording heard is `audioRecognition` (its original is the version's own audio original), a MIDI file `import`, a written score `import`, or `pdfRecognition` when it was read from a PDF with an engine. `merge` was written when the player applied a merge draft (0022, section 5): applying it set `taken` to the bars the draft took and added its "not a difference" bars to `dismissed`; undoing the merge emptied `taken` and set `appliedAt` to null. The draft left the iPad in version 1.1 (ADR 0034, section 8) and no build writes a new `merge`; a file that has one reads it and writes it back as it was.

The working notes a project opens with are their music, then their corrections, then the taken bars: removed ids are left out (an id that is no longer there is ignored) and added notes come in marked user-edited. Bars cannot be changed while bars are taken.

## Assembly (`assembly`)

The assembly of the player's own version from the piece's sources (version 15, [0024](architecture/adr/0024-assembling-a-version.md), section 7): the journal the player's notes are rebuilt by, and after «Сделать нотами» the notes the piece plays since then and the notes before the first apply. Left out while the piece has none.

In the model of [0030](architecture/adr/0030-originals-notes-piece.md) the applied notes are Notes made by `"op": "assembly"` from the compared Notes they carry; in version 16 they stay here, in the workspace beside their journal, until the assembly is reworked (0028).

```jsonc
"assembly": {
  "next": 4,                                  // the number the next step takes
  "steps": [
    { "number": 1, "at": "…", "kind": "carry",
      "source": "perf_1",                     // a compared Notes' id
      "from": { "onset": 2880, "duration": 1200 },        // in the source's own ticks
      "to": { "start": { "note": "n_0031", "offset": 0 },  // a place held by notes: the note's onset plus offset
              "end": { "note": "n_0037", "offset": 240 } }, // null end: insert at start
      "transpose": 0,
      "keep": true },                         // only from a recording: the place's notes stay, what they lack is added
    { "number": 2, "at": "…", "kind": "note", "note": "c1.n_0042", "semitones": 2 },   // semitones null: the note removed
    { "number": 3, "at": "…", "kind": "transpose", "place": { … }, "semitones": -2 }
  ],
  "applied": { "notes": [ … ], "bars": [ … ], "tempos": [ … ], "keys": [ … ] },   // only after «Сделать нотами»
  "before":  { … },                                                           // the notes before the first apply
  "appliedAt": "…"
}
```

A note in `applied` and `before` is `{ "id", "pitch", "onset", "duration", "velocity", "track", "voice"?, "hand"?, "handAttribution"?, "attribution": { "provenance", "confidence" } }`, with the provenance in lower camel case; a bar is `{ "onset", "duration", "beats", "beatType" }`, the bars following one another without gaps; a tempo is `{ "at", "microsecondsPerQuarter" }`; a key is `{ "at", "fifths", "mode"? }`. A carried note's id is its source note's id behind the step's number (`c1.n_0042`). A carry with `keep` (a fragment of a recording, «оставить ваши ноты, добавив то, что исполнитель играет иначе») replaces nothing: the place keeps its length, bars and notes, the fragment is fitted into it, and only its notes no note of the place sounds (the same pitch within a 16th) are added. `keep` is left out when false, and a reader before it took it as an ordinary carry. The recording's sound is never in the file (0024, section 9).

The notes a project opens with: with `applied`, those notes, its bars and tempo, then the edits made since (the taken bars of compared Notes are part of them); without it, as before. The journal builds from `before` once there is one, otherwise from the notes the project opens with. A file before version 15 reads as no assembly.

## Drafts (`drafts`)

The piece's drafts (version 17, [0034](architecture/adr/0034-drafts.md)): the player's own versions put together from fragments of any Notes, of this piece or another, without changing them. Left out while the piece has none. A draft is not the working notes: nothing in it is played, analysed or synced as the piece's notes, and no other part of the file names it but `hiddenViews`, while the player hides it from the switch.

```jsonc
"drafts": [
  { "id": "d_1", "name": "Предложение 1", "createdAt": "…",
    "places": [                                   // in order; the draft's bars are numbered from 1 through them
      { "id": "pl_1", "base": "v_1",              // the variant that gives the place its bars, meter, key and tempo
        "variants": [ { … }, { … } ],             // at least one; the base among them
        "marks": { … } },                         // optional: pencil marks on the place, as workspace.marks, in the place's ticks
      { "id": "pl_2", "base": "v_3", "variants": [ { … } ],
        "bridge": true }                          // optional: a place made for a seam, its variants the bridge candidates
    ],
    "shelf": [ { … } ],                           // variants not in a place; empty when none
    "next": 6,                                    // the number the next step takes
    "steps": [ … ] }                              // the journal, oldest first
]
```

A **variant** is a copy of a fragment, with where it came from:

```jsonc
{ "id": "v_2", "addedAt": "…",
  "from": { "notes": "perf_1",                    // the Notes it was copied from
            "project": "p_…",                     // optional: only when they are another piece's
            "span": { "onset": 23040, "duration": 7680 },   // in those Notes' ticks
            "bars": [9, 12],                      // optional: their bar numbers, both ends included, for the label
            "ms": [72000, 84500],                 // optional: the stretch of the recording, from < to
            "part": "all" },                      // all, right, left or melody
  "provenance": { … },                            // those Notes' provenance, copied as it was (below)
  "music": { "notes": [ … ], "bars": [ … ], "tempos": [ … ], "keys": [ … ] },   // the fragment's own notes, from 0
  "anchors": [ { "place": 1920, "variant": 2160 } ] }   // optional, in a place's variant that is not the base: the ticks the player confirmed match
```

- **`music`** has the shapes of the assembly's `applied` ("Assembly"): notes with their attribution and hand, bars following one another from 0, tempos and keys. A tempo also says where it comes from, `"source"`: `notated`, `inferred` (worked out from the playing), `player` or `default` (the piece's, taken where nothing states one), as the facade names them; left out when assumed (a recording's own clock), and a file without it reads every tempo as assumed. So a tempo worked out from a recording is never read back as stated. A variant's notes keep their ids; in a place they are named `<variant id>.<note id>` (`v_2.n_0042`), as a carried note is in the assembly. The music is a copy, so the variant stays whole when its Notes, their original or their piece is removed.
- **`provenance`** is the provenance of the Notes copied, with the shape of "Provenance"; from another piece, every entry of its `from` gets that piece's `project` when it has none. So an original's `sha256` follows the fragment, and the draft shows whether its source is here, changed or removed, worked out on open as for any Notes (0030, section 3). The sound of a recording is never in the file: it is played from the original on the device when it is there (`ms`).
- **`part`**: `melody` only when the voices of the fragment were cleanly split; its notes are marked inferred, as the engine found them.
- **A bridge candidate** (a variant of a place with `bridge`, version 1.2, [0034](architecture/adr/0034-drafts.md) section 5) is music-core's own bar, not a copy: its `provenance` is `{ "op": "variation", "params": { "bridge": "dominant" }, "at": "…" }` (`dominant`, `commonChord` or `bassWalk`: the rule that made it, as a «Что если» variant's provenance names its chip), its `from` names the Notes and `span` of the base of the place after its seam, whose meter, tempo and key it follows (no `bars` or `ms`; `part` `all`), and its notes and their hands are marked inferred. Nothing is added to the format for it.
- **A carried приём** (version 1.4 c-3b, Andrei's answer of 2026-11-26) is a variant music-core built for a place from a приём of another place: the place's base with the приём done in the bars it fits. Its `provenance` is `{ "op": "variation", "params": { "carry": "octave", "place": "pl_0001", "variant": "v_0002", "bar": 2 }, "at": "…" }` (the приём's kind, the place and variant it was read from and the bar it was read at), its `from` names the Notes and `span` of that place's base (no `bars` or `ms`), and the notes it puts in and their hands are marked inferred. Each note put in follows the base note it is read against by whole scale steps of the key there (natural minor in a minor key) and keeps the semitones left over: a note a scale step count does not reach from a chromatic base note keeps that difference, so the A put over an F♯ in C major (two steps up, A♯, less a semitone) is an A♭ over an F. A carry that would take out every note of the place is not made. The same carry is found again by its `params` as JSON values, however the file spaces them. Nothing is added to the format for it.
- **`anchors`**: only the ones the player confirmed, both strictly increasing; `place` is a tick of the place's base, `variant` a tick of the variant. The rest of the alignment with the base is worked out on every open (0022, section 2), each stretch between them on its own, so a confirmed anchor is never moved, the one after the last aligned from its start, so the music just after it follows it (version 1.2); the rebuild and the приёмы read that alignment. Only a variant of a place that is not its base has them: anchors on a place's base or on a variant on the shelf are damage. The player confirms one at a tick of the variant, or at a time of the recording it was heard in, which is written as the variant's tick played then (version 1.2). A new base removes the anchors of every variant of its place, as the place loses its steps: they lined the variants up with the base before.
- The draft's bar numbers, a place's meter, key and tempo (from its base and its steps), its seams and the приёмы of its variants are worked out, never stored. A приём is a variant's differences from the base of one kind and the same shape (where in the bar the notes taken out and put in are) in different bars, by the comparison of 0022 (section 3), or a run of the variant's bars found nowhere in the base, bars put in; its occurrences are the place's bars, from 1, as the base has them, which `technique.bars` names. It is sure when each occurrence's bar «совпадает» as 0024 decides it (at least 85 % of its notes found in place, or all but one of at least half), counting the bar's notes on the side with more, the base's or the variant's, the приём's own notes left aside. Its own notes are only what its kind explains: for an octave or a rhythm all the notes it pairs, for another bass the replaced basses and the basses played near them, for a fill or an ornament the notes added, for a simplification the notes left out, for «иначе» none; every other difference in the bar counts as missed. So a bar whose every note is the приём's own (a whole-bar octave) «совпадает» as an empty bar does and is sure, a bar replaced by something unrecognised is not, and bars put in never are. A take writes the приём's own notes, so «Взять» on another bass changes only the bass; «иначе», explaining none, takes all its bar's differences. «Взять всё уверенное» takes the sure ones as one group, skipping one in a bar the group already took from another variant. An occurrence a `leave` of its variant names by kind and bar is left; one a `take` names, or whose notes the result already has as the variant does, is taken.

A **step** names its place and changes only it. Steps are applied in number order on the place's base, as the assembly's journal is (0024, section 2): a step whose notes are no longer there is not applied, and the place says what it lost.

```jsonc
"steps": [
  { "number": 1, "at": "…", "kind": "take", "place": "pl_1", "variant": "v_2",
    "technique": { "kind": "fill", "bars": [10, 14] },   // optional: the приём it came from; a label, not read by the rebuild
    "part": "right",                              // optional: only the notes of a hand were taken
    "removed": ["v_1.n_0031", "v_1.n_0032"],      // base notes the step takes out
    "added": ["v_2.n_0042", "v_2.n_0043"] },      // variant notes the step puts in, at the place the alignment gives them
  { "number": 2, "at": "…", "kind": "take", "place": "pl_1", "variant": "v_2",
    "technique": { "kind": "insertBars", "bars": [16] },
    "insert": { "at": 7680, "span": { "onset": 8160, "duration": 1920 } } },   // bars of the variant put in at a place tick, the rest after them moved
  { "number": 3, "at": "…", "kind": "leave", "place": "pl_1", "variant": "v_2",
    "technique": { "kind": "octave", "bars": [12] } },   // the приём is kept as in the base, and its number goes
  { "number": 4, "at": "…", "kind": "transpose", "place": "pl_2", "semitones": -3 },
  { "number": 5, "at": "…", "kind": "tempo", "place": "pl_2", "quarterNotesPerMinute": 92 },
  { "number": 6, "at": "…", "kind": "leave", "place": "pl_2", "seam": true,   // the seam before the place is kept as it is
    "group": "g_…", "origin": "assistant" }       // optional on any step: accepted with others as one; who proposed it
]
```

- **`kind`**: `take` (a приём, a hand or a selection of a variant), `leave` (a приём, or the seam before the place, kept as it is), `transpose`, `tempo`. `technique.kind` is one of `fill`, `bass`, `simplify`, `ornament`, `octave`, `rhythm`, `insertBars`, `other`; `technique.bars` are the place's bar numbers, from 1.
- **`transpose`** moves the place's notes by `semitones` (one that would leave the MIDI range by whole octaves less, as a transposed piece's) and the keys its source states by as many fifths, a tritone the way the semitones go, written with seven sharps or flats at most (a place whose source states no key still states none); the notes a later `take` puts in, inserted bars too, move as far; **`tempo`** plays the place at `quarterNotesPerMinute` from its start, its own changes in proportion, as the player's tempo of a piece does. A **seam** is the boundary of a place and the one before it: what differs of key, meter and tempo is worked out from both results (a key only as a source states it, a tempo with its `source`; a place with no tempo at its start plays at the piece's default), and a `leave` with `seam` keeps it as it is.
- **`removed` and `added`** are what a take means: the rebuild reads only them, so a step means the same after the comparison's rules change. A take with `insert` puts the variant's bars in whole, in their own meter, and never fits them into the base's bars.
- **`group` and `origin`** are as in a correction ("Note corrections"): `origin` is `player` (left out) or `assistant`.
- **The structure is state, not steps**: adding, moving or removing a place or a variant, choosing the base, moving a variant to or from the shelf change `places` and `shelf`. Removing a variant, or moving it out of its place (to another place, as a new place or to the shelf), removes there the steps that name it and its anchors; changing a place's base removes the steps of that place and the anchors of all its variants. Moving a place to another position (version 1.4) removes the `leave` with `seam` of each place that has another place before it then: it kept a seam that is no longer there; and a place with `bridge` whose place before or after it is another then goes with its steps, made for a seam that is no longer there (version 1.4 c-3b). Putting in or removing a bridge place removes the `leave` with `seam` of the place after it in the same way. Making a place longer or shorter (version 1.4 c-3b) cuts its base again from its `from` Notes over other bars: the same variant `id` and `part`, its `span`, `bars`, `ms` and notes as that cut gives; the place's steps whose notes, bars or bars put in (`insert.at`) are outside the new span are removed, the bars (`technique.bars`) and `insert.at` of the others are counted from the new start, and so are the `anchors` of its other variants, those outside removed; the `leave` with `seam` of the place goes when its start moved, that of the place after it when its end moved, and a place with `bridge` before it or after it goes with its steps in the same way. A bridge place is not moved to another position; a bridge place, or a base music-core made (`op` `variation`), is not made longer or shorter. The app lists them first.
- Ids: `d_`, `pl_`, `v_` and `g_` followed by a random part, as every new entity's (0026, S3; the examples here are short); step numbers count up within the draft.

## Settings (`settings`)

Only the per-piece overrides live in the file (prep item 5); the catalogue (`MotiffLab.MusicCore.Settings.SettingsCatalogue`) says which settings have the piece scope.

```jsonc
"settings": { "layers.analysis": "off", "layers.ink": "off" }   // sorted by id; empty when none
```

Settings have three scopes. **Device** settings (layout, note names, pencil-only, pen ink, metronome, count-in, follow playback, theme, language, save online, analytics) stay on the device as one document, `{"version": 1, "values": {…}}`, written and read by `SettingsState`; they never leave it. **Account** settings (level, guidance, assistant answers) follow the player once there is an account; until then they are on the device too. **Piece** settings (the layers) can be overridden per piece, here. What a device knows of a project's online copy for the sync (its ETag and save time) is kept on the device beside the projects, never in the project file.

## Note corrections (`edits`)

The player's corrections of the working notes, oldest first (S1-I7): the notes as made never change, and the notes the apps show are those with every correction applied in order. Each correction names its note and the values it found, so a log that no longer fits the notes is refused rather than half applied. The note editor on the roll (testing item 15) writes them only when the player saves.

```jsonc
"edits": [
  { "noteId": "n_12", "before": { "pitch": 64, "onset": 960, "duration": 240, "velocity": 80 },
                      "after":  { "pitch": 65, "onset": 960, "duration": 240, "velocity": 80 } },   // changed
  { "noteId": "n_13", "before": { … } },                                                           // taken out: no "after"
  { "noteId": "a1", "added": { "track": 0, "hand": "Right" }, "after": { … } },                    // added: no "before"; "hand" only when the piece has hands
  { "noteId": "n_20", "before": { … }, "after": { … }, "group": "g_3f…", "origin": "repair" }      // accepted with others as one
]
```

An added note's id is the first of `a1`, `a2`, … that no note of the piece or the log has; it can be changed or taken out like any other. `group` (optional, version 16) names corrections accepted together, such as a batch of repairs, so they are undone as one; `origin` (optional, version 16) says who proposed it: `player` (left out), `repair` (a repair candidate of the engine, 0028) or `assistant`; another name from a later build is kept as it is. A correction without them is the player's own, alone.

## Opening an older file

Every version since 1 opens (`ProjectFile.Read`) and is converted to the current version in memory, deterministically: the same file always gives the same project, and writing it gives the same bytes.

### From version 17 (and 16) to 18

One thing is converted: a `phrases.granularity` override is dropped (the piece's phrase level is gone; the score shows every level), and an unknown setting or variation operation is kept as in a file of version 18, since versions 16 and 17 share its layout ("Unknown values"); in a file of version 15 or earlier it stays damage. Written again, it is the same file with version 18 and `minReader` 18 (`Golden/v18-*`).

### From version 16 to 17

Nothing is converted: a version 16 file has no drafts and no piece that is only a recording, and everything else reads as it is. Written again, it is the same file with version 17. An unknown value in a version 16 file is kept as in a version 17 file, as the builds that read version 16 kept it ("Unknown values").

### From version 15 (and earlier) to 16

| Version 15 | Version 16 |
| --- | --- |
| `source`, `fileName` | the working notes `n_1`: `music` (left out when `source` is empty: a PDF piece, no Notes) |
| `scan` (`engine`, `confirmedBars`, `pages`, `from`, `at`) | `n_1`: `provenance` `{ "op": "pdfRecognition", "from": [from], "engine", "at" }` and `scan` `{ confirmedBars, pages }` |
| `transcription` (`engine`, `bars`, `seams`, `sentFromMs`, `from`, `at`) | `n_1`: `provenance` `{ "op": "audioRecognition", … }` and `transcription` `{ bars, seams, sentFromMs }` |
| neither | `n_1`: `provenance` `{ "op": "import", "from": ["src_file"] }` when the file is a MIDI or MusicXML file by its name, else `"from": []` |
| `edits`, `check` | `n_1`: `edits`, `check` |
| `documentPages` | `n_1`: `pageMap` `{ "original": "src_pdf", "starts" }` |
| `readFrom` `{ project, pages }` | `n_1`: `provenance.from` `[{ "original": "src_pdf", "project" }]` and `pageMap` `{ "original": "src_pdf", "project", "starts" }` |
| `document`, `documentMarks` | the original `src_pdf`: `data`, `marks` |
| `originals` | `originals`, with every original the piece's data shows written out: `src_pdf` for `document`; `src_file` for a piece imported from a MIDI or MusicXML file; each recording or MIDI version under its own id (audio kept `device`, MIDI kept `file`); `addedAt` the project's `createdAt` (a version's `at` when it has one) |
| `performances[]` | Notes after `n_1`, with their id and `name`: `midi` as `music`; `engine`, `from`, `at` in `provenance`; the rest in `performance` |
| `partOf`, `editsOff`, `tempo`, `metadata`, `motifs`, `variations`, `observations`, `phrases`, `marks`, `bookmarks`, `resumePoints`, `assembly`, `settings` | the same, in `workspace`; `workspace.working` is `n_1` when there are notes |
| an idea with a variation | the same, with `"notes": "n_1"` |
| a correction | the same, the player's, alone (no `group`, no `origin`) |

`from[].sha256` is the original's fingerprint as the file knew it, left out when it did not; nothing is worked out on reading. A piece whose PDF was replaced before version 16 («Заменить PDF» with notes) keeps `from` pointing to `src_pdf` without a fingerprint, so its state cannot be told; a PDF fingerprinted since then shows "here".

### Versions before 15

The rules for the versions before 15, in addition to the earlier ones kept in `ProjectFile.Version`'s comment:

| Versions | What the reader does |
| --- | --- |
| 15 without `metadata` | Written before the library kept a title, composer and tags: none; the card shows the file's title and composer, or its name. |
| 15 without `originals` | Written before version 0.3, or written again by a version 0.2 build: the originals the piece's own data shows (see the table above), without fingerprints or marks. |
| 15 without `folder` | Written before version 0.5, or written again by an earlier build: the originals say nowhere where they were in a folder; a fresh look at the folder links them by fingerprint (`LibraryRelink`). |
| < 15 | No assembly. |
| < 14 | No corrections that add a note. |
| < 13 | No performances; no `Performance` flags. |
| 12 without `transcription.seams` | The recording went in one part, or was transcribed before seams were kept: no `PartSeam` flags. |
| < 12 | No marks, bookmarks (the four built-in categories only) or overrides. `check` is null until the project is opened, then worked out from the piece and the scan's confirmed bars. |
| 12 without `phrases.short` / `phrases.sentences` | Written before the three phrase levels ([0018](architecture/adr/0018-redesign.md), section 3): the Short and Sentence levels have no edits, so the engine suggests them. The score shows every level; a `phrases.granularity` override is dropped on reading (version 18). |
| 8–11 | Each session becomes a resume point with the same number (`s_0001` → `r_0001`), named by the session's task, at its passage, with the session's `lastActive`; its intent is dropped. |
| 6–11 | Observations lose `sessionId`; `factId` is absent. |

Pencil marks saved on the device before version 12 (the `motiff-lab-pencil` IndexedDB database, [0015](architecture/adr/0015-ipad-app.md)) are taken into the project the first time its score is shown and then forgotten on the device.

### Older builds

A build before 1.6 refuses a version 18 file with "This project was saved by a newer version of Motiff Lab"; from version 18 on, a later version that keeps `minReader` 18 opens read-only in 1.6 rather than being refused.

A build before 1.1 refuses a version 17 file the same way ([0034](architecture/adr/0034-drafts.md), section 7), whether it holds drafts, a piece that is only a recording or neither; the API needs no release for it.

A build before 0.6 refuses a version 16 file with "This project was saved by a newer version of Motiff Lab", so the web, the iPad and the API are released together ([0026](architecture/adr/0026-project-continuity.md), section 3; 0030, section 7). The API checks only the envelope (`format`, `version`, `id`, size, quota; 0026 S2) and stores the bytes it was sent, so a later version needs no API release.

## Unknown values

A later build can add a value to an enum while the file keeps its version ([0026](architecture/adr/0026-project-continuity.md), S6). A reader that does not know the value keeps the entity holding it as an unknown entity (`UnknownEntity`, with the enum's type name), out of the project's lists: it takes no part in playback, analysis or variations, and an app never meets it. The writer puts it back unchanged at the place it had in its array, so a file read and written with no change keeps every entity in the same order.

- **Only a value that is a name** (ASCII letters and digits, starting with a letter) and only in a file of the reader's layout (version 16 to 18, which share it). In an older file, or for any other text, the field is damaged: the builds that wrote an older file knew no value this one does not.
- **The entities kept so**: an original (`originals`), a Notes this build cannot open (`notes`, see "Notes"), a motif (`motifs`), a variation (`variations`), an idea (`observations`), a stroke (`marks.ink`, an original's `marks.ink`), a highlight (`marks.highlights`), a bookmark category (`bookmarks.categories`), a resume point (`resumePoints`), a Check flag (`check`) and a draft (`drafts`, version 17), whatever the depth of the value inside it.
- **What goes with it.** A motif and its variations are kept or left unknown together. A bookmark in an unknown category of the player's own is left unknown with it.
- **Built-in categories** are always there (#364). One with an unknown ink is kept unknown and written back as it was, and stands in under the name the file gives it, in the built-in's own ink (an app never shows an ink it does not have): its bookmarks are read as known, bookmarks can be added to it and moved into it (removing a category moves its bookmarks to Practise). Once the player renames it, the renamed one takes its place.
- **New ids** for bookmarks, categories, resume points, ideas, highlights and Notes pass the unknown ones by, and a bookmark is never added under the id of an unknown one (#364). A new working motif of the facade passes the unknown motifs by too (#821). An entity an app makes with the id of an unknown one takes its place: the unknown one is not written, and neither is what went with it.
- **Owned entities** (a variation of its motif, a bookmark of its category) are taken only by a known one with the same id and the same owner (#364): one of another owner never drops it. The facade never makes a variation with the id of an unknown one (its working variation passes them by, #821); one made so by a project built another way is written under the next id free of both, so no two variations of the file share one.
- **Still damaged**: an unknown value in a note correction's values or hand, in the compared notes' `performance` or in the assembly, since keeping those aside would change the notes that sound (Andrei, #313); an unknown `Overlay` or Keep choice; in a file of version 15 or earlier, an unknown per-piece setting or variation operation.
- **Settings and operations (version 18, #314)**: in a file of version 16 or later, a setting id this build does not know, or a piece setting with a text value it does not allow, is kept and written back with the overrides, and dropped once the player sets that setting; a value that is not text is damage. A variation with a step of an operation this build does not know is kept whole as an unknown entity (type `VariationOperation`) and written back; the facade answers its id in `newerVariations` (an idea's, in `newerIdeas`), and no app shows it.
- **Unknown fields** are kept at the top level, in `workspace`, in each Notes, each original, each provenance and (version 18) each motif, variation, idea, bookmark, category, resume point, draft and the settings, and written back after the known fields in the order they came; elsewhere they are dropped on writing, except inside an entity kept whole as unknown. An unknown `op` keeps its provenance whole.

A file written by a newer version is refused with "This project was saved by a newer version of Motiff Lab", in Russian «Этот проект сохранён более новой версией Motiff Lab» (#363): the facade answers it in the language of the request.
