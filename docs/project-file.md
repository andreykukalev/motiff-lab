# The project file (`.motiff.json`), version 16

The project file is what a player keeps of a piece. Since version 16 ([0030](decisions/0030-originals-notes-piece.md), version 0.6) it is laid out in three layers: the piece's **originals** (files as they are, never changed), the **notes** made from them (each with its provenance, corrections and Check flags) and the **workspace** (which notes are the working notes, and everything the player made on them). It is written and read by `MotiffLab.MusicCore.Projects.ProjectFile`, by hand, so the same project gives the same bytes. The web app saves it in IndexedDB and, when the player turns saving online on, sends it to the API ([0014](decisions/0014-hosted-scan-server.md)); the iPad app reads and writes the same file through the facade. A file of any earlier version opens and is converted on reading; the rules are at the end ("Opening an older file").

Ticks are integers at 480 per quarter ([0002](decisions/0002-canonical-time.md)). A span is `onset` and `duration` in ticks. Times are ISO 8601 with offset. Enum values in the file are the C# names (`Carmine`, `PianoRoll`) unless a section says lower case; the facade answers them in lower camel case.

## The document

```jsonc
{
  "format": "motiff-lab-project",
  "version": 16,
  "id": "p_…",                    // the project's id
  "fileName": "minuet.musicxml",   // the piece's file name: how the working notes' music is read (by its extension), and its title when nothing else names it
  "createdAt": "…", "lastOpened": "…",
  "originals": [ … ],              // every original of the piece, and the tombstones of removed ones
  "notes": [ … ],                  // every Notes of the piece: the working notes and the ones compared with them
  "workspace": { … }               // the working notes and the player's work
}
```

Each layer is an object or array of objects; a reader that meets a field it does not know at the top level, in the workspace, in a Notes, in an original or in a provenance keeps it and writes it back unchanged after the fields it knows (0026 S6). Unknown fields elsewhere are still dropped on writing, except inside an entity kept whole as unknown ("Unknown values").

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
    "data": "<base64>",                    // only a PDF or photos kept "file": the bytes, byte for byte
    "marks": { "ink": [ … ], "bookmarks": [ … ] },   // only a PDF or photos with the player's marks on its pages
    "removed": { "at": "…" } }             // only a tombstone: the player removed the original
]
```

- **`kept`**: `file`, the bytes are in this project file on the device and never online: a PDF's in `data`, an imported MIDI or MusicXML file's as the `music` of the Notes imported from it (so they are not kept twice); `device`, beside the project file on the device only (a recording's sound); `link` (version 0.5, «Импорт папки»), in the player's own folder (a folder on the computer, Files on the iPad, a cloud folder synced there), read in place. **The player's file is never changed**, renamed, moved or deleted, its properties never written and no folder made (Andrei, 2026-11-12); the bytes of a linked original are never written into the file, and neither is its page map then. The 50 MB limit of a PDF kept in the file does not apply to a linked one.
- **`sha256`** is the original's version: other bytes are always another original. A picked file is linked again to the original with its fingerprint (`Project.RelinkDocument`, «Привязать»); any other file is refused. A device that has the bytes works out a missing fingerprint once (`Project.Fingerprinted`).
- **`held`** is bookkeeping that only its own device writes: a pull takes this device's mark from its own copy and every other device's from the online copy. The iPad marks the PDF it keeps when it sends the copy online (`project.online`).
- **`folder`**: each name between the `/` is non-empty and not `.` or `..`, with no `\`; anything else is damage. A fresh look at the folder finds each file at its place and links again the ones moved or renamed (`LibraryRelink`).
- **`data`** must start with `%PDF-`. A PDF kept in the file never goes online: the online copy (`Project.ForOnline`) leaves `data` out and the page map with it (see "The PDF online").
- **`marks`**: the player's marks on the PDF's pages ([0023](decisions/0023-pdf-pieces.md)), apart from the marks on the notes, so the PDF view and the Notes view each keep their own: `ink` holds strokes in the shape of `workspace.marks.ink`, except that `bar` is the number of the page the stroke starts on (from 1), `u` its place across the page (0 to 1) and `v` its distance below the page's top edge in the page's own spaces (a hundred and twentieth of its width); `bookmarks` holds `{ "id": "pb_0001", "page": 3, "name"?: "Coda", "createdAt" }`, at most one per page, in page order. Left out when there are none. In version 16 only `src_pdf` carries marks in memory (`Project.DocumentMarks`); marks on another original are kept and written back.
- **`removed`**: a tombstone. The bytes left this device (a linked file was only unlinked), the description and the page marks stay, so the Notes made from it keep their provenance and the state "removed", and adding the same file again (the same `sha256`) revives the original with its marks. A tombstone has no `data` and no `held`.
- An unknown `kind`, `kept` or `device` keeps the description whole as an unknown entity (S6).

**A piece with no notes** is a piece when it has a live PDF original (in the file, linked in a folder or on another device): the library lists it (`project.summary`), and the facade's `open` answers `originalAway` until the file is here (`Project.RelinkDocument`, by fingerprint). A file with no Notes and no PDF original is damaged.

### The PDF online

Since version 0.5 (Andrei, 2026-11-12, 17:45 UTC, amending 0026 C3) a piece that is only a PDF syncs too, without the PDF: its online copy (`Project.ForOnline`) is the piece without `data` and without the working notes' page map, with every original described (the PDF with its `sha256`, worked out then if the file had none, its `kept`, `folder` and `held`) and the page marks. A device without the PDF keeps the copy as it comes and opens it once the same file is linked there. The device that has the PDF keeps it, and its page map, when the online copy comes back (`ProjectSync.Pulled`). Page marks are written by one device only (`Project.WritesDocumentMarks`): the device that has the PDF, and the iPad when both have it; a pull keeps the writer's own marks and gives every other device the online copy's, except that an online copy with no marks never clears a device's marks. The web shows such marks read only.

## Notes (`notes`)

Every Notes of the piece: the working notes first, then the ones compared with them (the versions of [0022](decisions/0022-score-and-performance.md)) in the order they were added. A Notes keeps its own music, so it stays valid when its original is removed.

```jsonc
"notes": [
  { "id": "n_1",                             // stable within the piece; a compared Notes keeps its version id ("perf_1")
    "provenance": { … },                     // where the notes came from (below)
    "music": "<base64>",                     // the notes as made: the working notes' MusicXML or MIDI, read by the piece's "fileName"
    "edits": [ … ],                          // the working notes: their corrections ("Note corrections")
    "check": [ … ] | null,                   // the working notes: their Check flags ("Check")
    "scan": { "confirmedBars": [3, 7], "pages": [ … ] },        // notes recognised from a PDF or photos
    "transcription": { "bars": { … } | null, "seams": [ … ], "sentFromMs": 754000 },   // notes heard in a recording
    "pageMap": { "original": "src_pdf", "starts": [0, 15360, 29760] } },   // where each page of a PDF starts in these notes
  { "id": "perf_1", "name": "Live at the National Gallery",
    "provenance": { … },
    "music": "<base64>",                     // a compared Notes: MIDI
    "performance": { … } }                   // how it is compared with the working notes ("Compared notes")
]
```

- **The working notes** are the Notes `workspace.working` names; they carry `edits` and `check`, and never `performance`. A piece with no notes yet (a PDF to read as it is) has no working notes and an empty `notes`.
- **`music`**: the working notes' bytes are read by the piece's `fileName`, as before: MusicXML (`.musicxml`, `.xml`, `.mxl`) or MIDI (`.mid`, `.midi`); a scan's notes are its MusicXML, a transcription's the engine's MIDI. A compared Notes' music is MIDI: the engine's MIDI, the player's MIDI file as it was, or a score written out as MIDI. The imported file of a piece imported from a file is the working notes' music byte for byte, so its original (`src_file`, kept `file`) has no `data` of its own.
- **`scan`** (with `"op": "pdfRecognition"`): the bars the player checked and said look right (`confirmedBars`), and the raw numbers of the scan server's report on each page (`pages`, version 0.4): `{ "page", "rotation", "sharpness", "contrast", "staves", "noteheads", "barLines", "spacePx", "measures" }`, never the music. A page's quality (good, weak, poor) is worked out from them on every read (`PageReport.Quality`), so the thresholds change without a migration; a field a report lacks reads as 0; `page` is the page's place in the notes, from 1. On open the bars of a weak page are less sure (0.5) and those of a poor page flagged `PageQuality`. `pages` is left out when there are no reports.
- **`transcription`** (with `"op": "audioRecognition"`): the bars and tempo worked out for the notes heard ([0016](decisions/0016-bars-for-transcribed-notes.md)), `null` to keep the engine's 120 BPM and 4/4: `{ "origin", "sixteenthMicroseconds", "signature", "barStarts", "stepsPerBeat", "beats", "meters"? }`. `meters` (version 0.5) says where the bars change signature: each change is at one of `barStarts` after the first (`at`, in 16ths from the origin), after the one before, with bars of whole 16ths, and may name a signature the grid's search does not offer, such as `7/8`; any other change is damage. `seams` (when a long recording went in parts): where each part after the first takes over, in milliseconds from the start of the stretch sent, ascending; the Check layer flags the bars on either side (`PartSeam`); left out for one part. `sentFromMs` (version 0.5): where, in the recording file, the stretch sent started, 0 or more; the workspace's `partOf.ms` counts from it, so another device that has the file (the same `sha256`) finds the piece's sound there; left out when not kept.
- **`pageMap`**: the rough link the PDF and Notes views switch by: where each page of `original` starts in these notes, in ticks, page 1 at 0, each page after the one before. A page starts at the first bar the scan server marked with `<print new-page="yes"/>`, or, without such marks, the bars are shared evenly between the pages. With `"project": "p_…"` the PDF is in another piece: notes read from a PDF piece into a project of their own between 2026-11-05 and 2026-11-07 (0023); no build writes such notes any more. Without `project`, the original must be this piece's PDF and the map is written only while the PDF's bytes are here.
- An entry with `edits` or `check` that is not the working notes, or one that is neither the working notes nor has `performance`, comes from a later build: it is kept whole as an unknown entity and written back where it stood.

### Provenance

One shape for every Notes ([0030](decisions/0030-originals-notes-piece.md), section 2), and for a kept variation ("Ideas"):

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
  "motifs": [ … ],                  // the working selection
  "variations": [ … ],              // chains of steps on a motif; an optional "key" { "fifths", "mode" } the steps read
  "observations": [ … ],            // ideas
  "phrases": { "added": [ticks], "removed": [ticks],
               "short": { "added": [ticks], "removed": [ticks] },       // cuts inside phrases at the Short level (0018, section 3)
               "sentences": { "added": [ticks], "removed": [ticks] } },  // phrase boundaries where a sentence must end, and ones it runs on across
  "marks": { … },
  "bookmarks": { … },
  "resumePoints": [ … ],
  "assembly": { … },                // absent while the piece has no assembly
  "settings": { … },
  "hiddenViews": ["src_pdf_2", "perf_1"]   // optional: the views hidden from the switch, only while the player hid one
}
```

### Views hidden from the switch (`hiddenViews`)

The switch at the top of the score lists every view of the piece: each PDF original with the Notes read from it, each other Notes, and each recording with its sound as a Performance view (Andrei, 2026-11-14). The player can hide any of them from the switch in the Sources sheet, except the working notes. `hiddenViews` is the ids hidden, originals' or Notes', distinct and in the order of their ids; written only while one is hidden. An id this build does not know is kept, so a view a later build shows stays hidden; an id that names the working notes is kept and not applied while they are the working ones. A value that is not a list of distinct texts is damage. In the sync it is the player's data, as the rest of the workspace: the copy saved last says it.

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
- **Notes** merge by id; the working notes' corrections follow S9. What an import brought (`scan.pages`, `transcription.bars.meters`, `transcription.sentFromMs`) is kept from this device when the online copy has none and the notes are the same (the same `music`).
- **The workspace** is the player's data, except `editsOff` and `lastOpened` (bookkeeping, the copy saved last says it) and `check` (derived).

## Marks (`marks`)

Everything the player writes on the score, kept with the piece ([0018](decisions/0018-redesign.md), prep item 1). The pencil layer and the project file share one shape for strokes, so the web app and the iPad app pass them through unchanged (`ProjectFile.WriteInk` / `ReadInk` give the layer's own document, `{"version": 2, "marks": [stroke, …]}`).

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
      "fromPhrase": true,       // made from a phrase: follows it when phrases are joined or split
      "createdAt": "…" }
  ]
}
```

Bookmarks may overlap. A bookmark made from a phrase (`fromPhrase`) takes the span of the phrase that contains its onset after every phrase edit (`Project.FollowPhrases`); a bookmark the player drew over bars keeps its bars. Removing an own category moves its bookmarks to Practise; built-in categories cannot be removed or renamed.

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

An idea is an observation kept on a passage ([0009](decisions/0009-exploration-layer-and-notebook.md)): a variation, the steps that made it, a note, and the fact it came from (0018, prep item 4).

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

A kept variation belongs to the Notes it was built on (`notes`, research 4.6), beside their corrections; it is not Notes of its own. Every idea of version 16 is built on the working notes, and a file of an earlier version reads so. `provenance` is left out for an idea the player made in the app; a later build's assistant writes `"op": "assistant"` with `request` and `model` in `params`, and `inputs` naming the Notes, which this build keeps and writes back.

## Check (`check`)

What the scan server, the transcription or a performance left for the player to look at, as data, in one format for all three (prep item 6).

```jsonc
"check": [
  { "bar": 12, "source": "Scan" | "Transcription" | "Performance",
    "reason": "BarTooLong" | "BarTooShort" | "StavesDiffer" | "OutOfPianoRange" | "GraceNotes"   // scan
            | "PartSeam" | "TempoUncertain" | "DenseCluster" | "HandsGuessed"                     // transcription
            | "RecordingOctave" | "RecordingBarLength"                                            // a performance (version 13)
            | "PageQuality",                                                                        // scan: a bar on a poor page (version 0.4, within 15)
    "resolved": false }
]
```

`null` only in a file written for a project that was never opened since it was read from an older version; opening such a project works the flags out from the piece (`CheckFlag.FromScan`, or `CheckFlag.FromTranscription` for a transcription) and marks the bars the player had confirmed as resolved (`scan.confirmedBars` of the working notes).

A transcription's flags (prep item 12) are worked out from the notes the engine heard and the bars worked out for them: `PartSeam` for the bars on either side of a seam; `TempoUncertain` for a bar where at least half of four or more onsets sit more than 30 ticks off both the 16th and the triplet-8th grids; `DenseCluster` for a bar with four onsets within a 16th or a chord of three or more notes a second apart; `HandsGuessed` for a bar with two or more notes whose hand was guessed with confidence below 0.6. A reason other than `PartSeam` that would flag more than a quarter of the bars is left out, since it then says more about the recording than about a bar. Resolving a flag on a scanned piece also confirms the bar in `scan.confirmedBars`, so older readers see the same thing.

A performance added to a scanned piece raises its own flags (source `Performance`, [0022](decisions/0022-score-and-performance.md), section 6): `RecordingOctave` where at least three quarters of a hand's notes are played 12 semitones from the scan for two bars or more, and `RecordingBarLength` where the scan flagged a bar too long or too short and the performance measures it at the meter's length at the neighbouring bars' tempo. Removing the last performance drops them.

## Compared notes (`performance`)

Notes compared with the working notes ([0022](decisions/0022-score-and-performance.md), section 8): the anchors that line them up with the working notes, the bars the player said are not a difference, and the bars taken into the working notes. A recording's sound is never in the file; it stays on the device under the Notes' id.

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
    "merge": {                           // optional: the merge draft's choices, to open again
      "appliedAt": "2026-11-07T12:30:00.0000000+00:00",   // null after "Undo the merge"; the choices stay
      "bars": [ { "bar": 5, "choice": "Version" },        // Mine, Version, Own or NotADifference
                { "bar": 7, "choice": "Own", "own": { "bar": 7, "removed": [ … ], "added": [ … ] } } ] } } }
```

The provenance's `op` follows `source`: a recording heard is `audioRecognition` (its original is the version's own audio original), a MIDI file `import`, a written score `import`, or `pdfRecognition` when it was read from a PDF with an engine. `merge` is written once the player applies a merge draft (0022, section 5): applying it sets `taken` to the bars the draft takes and adds its "not a difference" bars to `dismissed`; undoing the merge empties `taken` and sets `appliedAt` to null.

The working notes a project opens with are their music, then their corrections, then the taken bars: removed ids are left out (an id that is no longer there is ignored) and added notes come in marked user-edited. Bars cannot be changed while bars are taken.

## Assembly (`assembly`)

The assembly of the player's own version from the piece's sources (version 15, [0024](decisions/0024-assembling-a-version.md), section 7): the journal the player's notes are rebuilt by, and after «Сделать нотами» the notes the piece plays since then and the notes before the first apply. Left out while the piece has none.

In the model of [0030](decisions/0030-originals-notes-piece.md) the applied notes are Notes made by `"op": "assembly"` from the compared Notes they carry; in version 16 they stay here, in the workspace beside their journal, until the assembly is reworked (0028).

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

## Settings (`settings`)

Only the per-piece overrides live in the file (prep item 5); the catalogue (`MotiffLab.MusicCore.Settings.SettingsCatalogue`) says which settings have the piece scope.

```jsonc
"settings": { "phrases.granularity": "sentences", "layers.ink": "off" }   // sorted by id; empty when none
```

Settings have three scopes. **Device** settings (layout, note names, pencil-only, pen ink, metronome, count-in, follow playback, theme, language, save online, analytics) stay on the device as one document, `{"version": 1, "values": {…}}`, written and read by `SettingsState`; they never leave it. **Account** settings (level, guidance, assistant answers) follow the player once there is an account; until then they are on the device too. **Piece** settings (phrase granularity, the layers) can be overridden per piece, here. What a device knows of a project's online copy for the sync (its ETag and save time) is kept on the device beside the projects, never in the project file.

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

Every version since 1 opens (`ProjectFile.Read`) and is converted to version 16 in memory, deterministically: the same file always gives the same project, and writing it gives the same bytes.

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
| 12 without `phrases.short` / `phrases.sentences` | Written before the three phrase levels ([0018](decisions/0018-redesign.md), section 3): the Short and Sentence levels have no edits, so the engine suggests them. The level shown is the override `phrases.granularity`, as before. |
| 8–11 | Each session becomes a resume point with the same number (`s_0001` → `r_0001`), named by the session's task, at its passage, with the session's `lastActive`; its intent is dropped. |
| 6–11 | Observations lose `sessionId`; `factId` is absent. |

Pencil marks saved on the device before version 12 (the `motiff-lab-pencil` IndexedDB database, [0015](decisions/0015-ipad-app.md)) are taken into the project the first time its score is shown and then forgotten on the device.

### Builds before 0.6

A build before 0.6 refuses a version 16 file with "This project was saved by a newer version of Motiff Lab", so the web, the iPad and the API are released together ([0026](decisions/0026-project-continuity.md), section 3; 0030, section 7). The API checks only the envelope (`format`, `version`, `id`, size, quota; 0026 S2) and stores the bytes it was sent, so a later version needs no API release.

## Unknown values

A later build can add a value to an enum while the file keeps its version ([0026](decisions/0026-project-continuity.md), S6). A reader that does not know the value keeps the entity holding it as an unknown entity (`UnknownEntity`, with the enum's type name), out of the project's lists: it takes no part in playback, analysis or variations, and an app never meets it. The writer puts it back unchanged at the place it had in its array, so a file read and written with no change keeps every entity in the same order.

- **Only a value that is a name** (ASCII letters and digits, starting with a letter) and only in a file of the reader's own version. In an older file, or for any other text, the field is damaged: the builds that wrote an older file knew no value this one does not.
- **The entities kept so**: an original (`originals`), a Notes this build cannot open (`notes`, see "Notes"), a motif (`motifs`), a variation (`variations`), an idea (`observations`), a stroke (`marks.ink`, an original's `marks.ink`), a highlight (`marks.highlights`), a bookmark category (`bookmarks.categories`), a resume point (`resumePoints`) and a Check flag (`check`), whatever the depth of the value inside it.
- **What goes with it.** A motif and its variations are kept or left unknown together. A bookmark in an unknown category is left unknown with it. A built-in category with an unknown ink stays unknown and is not taken by the built-in one.
- **New ids** for bookmarks, categories, resume points, ideas, highlights and Notes pass the unknown ones by. An entity an app makes with the id of an unknown one (the facade's working motif `m_0001`) takes its place: the unknown one is not written, and neither is what went with it.
- **Still damaged**: an unknown value in a note correction's values or hand, in the compared notes' `performance` or in the assembly, since keeping those aside would change the notes that sound (Andrei, #313); an unknown per-piece setting or variation operation; an unknown `Overlay` or Keep choice.
- **Unknown fields** are kept at the top level, in `workspace`, in each Notes, each original and each provenance, and written back after the known fields in the order they came; elsewhere they are dropped on writing, except inside an entity kept whole as unknown. An unknown `op` keeps its provenance whole.

A file written by a newer version is refused with "This project was saved by a newer version of Motiff Lab".
