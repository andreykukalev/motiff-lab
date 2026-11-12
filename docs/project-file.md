# The project file (`.motiff.json`), version 15

The project file is what a player keeps of a piece: the imported source, the work on it, and since version 12 everything the redesign keeps with the piece ([0018](decisions/0018-redesign.md), section 7; [0020](decisions/0020-music-core-facade.md)), since version 13 the performances compared with it ([0022](decisions/0022-score-and-performance.md)), and since version 15 the assembly of the player's own version from them ([0024](decisions/0024-assembling-a-version.md)). It is written and read by `MotiffLab.MusicCore.Projects.ProjectFile`, by hand, so the same project gives the same bytes. The web app saves it in IndexedDB and, when the player turns saving online on, sends it to the API ([0014](decisions/0014-hosted-scan-server.md)); the iPad app reads and writes the same file through the facade. A file of an earlier version always opens; the rules are at the end.

Ticks are integers at 480 per quarter ([0002](decisions/0002-canonical-time.md)). A span is `onset` and `duration` in ticks. Times are ISO 8601 with offset. Enum values in the file are the C# names (`Carmine`, `PianoRoll`); the facade answers them in lower camel case.

## The document

```jsonc
{
  "format": "motiff-lab-project",
  "version": 15,
  "id": "p_…",                 // the project's id
  "fileName": "minuet.musicxml",
  "source": "<base64>",         // the imported file, MIDI or MusicXML, as it was; empty for a PDF read as it is
  "document": "<base64>",       // only a piece added as a PDF (version 13, 0023): the PDF byte for byte
  "documentMarks": { "ink": [ … ], "bookmarks": [ … ] },  // only when the PDF's pages carry marks (version 13, 0023)
  "documentPages": [0, 15360, 29760],  // only a piece with both notes and a PDF: where each page starts, in ticks (0023)
  "readFrom": { "project": "p_…", "pages": [0, 15360, 29760] },  // only notes read from a PDF piece into a project of their own, 2026-11-05 to 2026-11-07 (0023)
  "originals": [ … ],           // within version 15 (version 0.3): the descriptions of the piece's originals the piece's own data does not show
  "createdAt": "…", "lastOpened": "…",
  "metadata": { "title": "…", "composer": "…", "tags": ["…"], "playlists": ["…"] },   // within version 15: only what the player set in the library
  "motifs": [ … ],              // the working selection (version 1)
  "variations": [ … ],          // chains of steps on a motif (version 2); an optional "key" { "fifths", "mode" } the steps read, as on a kept variation (read as none when absent)
  "observations": [ … ],        // ideas (version 6; factId since 12)
  "edits": [ … ],               // note corrections (version 3); a correction can add a note (version 14)
  "phrases": { "added": [ticks], "removed": [ticks],   // the player's phrase boundaries (version 5)
               "short": { "added": [ticks], "removed": [ticks] },       // cuts inside phrases at the Short level (0018, section 3; read as none when absent)
               "sentences": { "added": [ticks], "removed": [ticks] } },  // phrase boundaries where a sentence must end, and ones it runs on across
  "marks": { … },               // version 12
  "bookmarks": { … },           // version 12
  "resumePoints": [ … ],        // version 12; replaces "sessions" (versions 8–11)
  "check": [ … ] | null,        // version 12
  "performances": [ … ],        // version 13
  "assembly": { … },            // version 15; absent while the piece has no assembly
  "settings": { … },            // version 12
  "scan": { … },                // only a scanned piece (version 7); "from" and "at" within version 15 (version 0.3)
  "transcription": { … }        // only a transcribed piece (version 9; bars since 10; seams in 12; "from" and "at" within 15)
}
```

The transcription keeps `"seams": [ms, …]` when a long recording was sent in parts ([0016](decisions/0016-bars-for-transcribed-notes.md)): where each part after the first takes over, in milliseconds from the start of the stretch of the recording that was sent, ascending. The Check layer flags the bars on either side of each seam (`PartSeam`). The array is left out when the recording went in one part, and a file without it reads as no seams.

## Originals (`originals`)

Where each original of the piece is (version 0.3, local-first; Andrei approved it on 2026-11-11, 21:20 UTC, amending 0026 C3): only the description of each original is in the file and goes online with it; the bytes stay on the devices. It is kept within version 15 as optional fields, like `metadata`, because version 0.2 of both apps refuses a file of a later version but reads past fields it does not know: a v0.2 build opens such a file, and when it writes it again it drops `originals` and the `from` and `at` links (unknown fields are dropped on writing), which the next v0.3 read takes up again as below.

```jsonc
"originals": [
  { "id": "src_pdf",                       // "src_pdf": the PDF in `document`; "src_file": the file the notes were imported from; a version's id for its recording or MIDI file
    "kind": "pdf" | "images" | "audio" | "midi" | "musicxml",
    "name": "…",                           // optional: the player's name for it
    "fileName": "Nocturne.pdf",
    "sha256": "<64 lower-case hex digits>",  // optional: the fingerprint of the bytes as picked; a picked file is linked again by it
    "bytes": 123456,                       // optional
    "pages": 3,                            // optional: a PDF's or photos' pages
    "durationMs": 61000,                   // optional: a recording's length
    "addedAt": "…",
    "kept": "file" | "device",             // file: inside the project file on the device (never online); device: beside it, on the device only
    "held": [ { "device": "ipad" | "web", "at": "…" } ] }   // optional: the devices that hold the bytes, each writing only its own mark
]
```

The piece's own data shows most originals, so a file without `originals` (every file before version 0.3, or one a v0.2 build wrote again) reads as: the PDF in `document` as `src_pdf` (kept in the file); the MIDI or MusicXML `source` of a piece imported from a file as `src_file` (not a scan's or a transcription's notes); each version from a recording (`audio`, kept on the device) or a MIDI file (`midi`, kept in the file) under the version's own id, so the web keeps the sound it stores under `<project>/<version>`. Those carry no fingerprint: the device that has the PDF works it out once (`Project.Fingerprinted`). The file writes only the descriptions that say more than that, so a piece with nothing more to say is written as before. The online copy of a piece with a PDF (`ForOnline`) writes them all, so another device shows the PDF it does not have.

`scan`, `transcription` and each performance can say which original their notes were made from and when: `"from": "src_pdf"`, `"at": "…"`, both optional. A scan of the piece's own PDF says `src_pdf`; a recording version says its own id.

In the sync (0026 S8) the descriptions are the player's data: a pull unites both copies by fingerprint, else by id, with no deletions (a different original with an id the other copy uses gets the next free one, `src_a_2`). The `held` marks are bookkeeping that only their own device writes: a pull takes this device's mark from its own copy and every other device's from the online copy. The iPad marks the PDF it keeps when it sends the copy online (`project.online`). An unknown `kind`, `kept` or `device` in a file of this version keeps the description whole as an unknown entity (S6). A piece that is only a PDF still stays on the device: its online copy would have neither notes nor a PDF, which version 0.2 reads as damaged.

## Title, composer, tags and playlists (`metadata`)

What the player wrote about the piece in the library (version 0.2; Andrei approved the change on 2026-11-11): its title, its composer, their own tags and the playlists it is on, kept within version 15 as an optional field, like `merge` within version 14. Each is the player's: nothing is copied into it from the source file.

```jsonc
"metadata": {
  "title": "Für Elise",          // optional: the player's title
  "composer": "L. van Beethoven", // optional: the player's composer
  "tags": ["Exam", "Romantic"],   // optional: the player's tags, in their order
  "playlists": ["Recital"]        // optional: the playlists the piece is on, by name, in the player's order
}
```

Every text is one line: runs of white space are one space and the ends are trimmed, so an empty text is none. Tags and playlists keep the player's order and are distinct whatever the letter case ("Exam" and "exam" are one tag, the first spelling kept). A playlist is its name: it exists while a piece is on it, so a piece is on many playlists without copies and its playlists travel with it; the order of the pieces within a playlist is not kept yet. A field is written only when it is set, and `metadata` only when one is; a file without it has none.

What a library card shows (`Project.Details`, the facade's `title`, `composer`, `tags` and `playlists`) is the player's value where there is one, else what the imported file says (a MusicXML score's `<work-title>`, else `<movement-title>`, else a title credit; its `<creator type="composer">`, else a composer credit), else for the title the file's name without its extensions. The file's values are read from the source on every summary and never written into `metadata`, so clearing the player's value shows the file's again. A scanned or transcribed piece and a PDF piece take no title or composer from their source.

A build before this field reads such a file as before and drops `metadata` when it writes the file again (unknown fields are dropped on writing), so the title, composer and tags are lost if the piece is saved by an older build of either app. Likewise a build before `playlists` reads the rest of `metadata` and drops the playlists when it writes the file again.

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

An idea is an observation kept on a passage (version 6, [0009](decisions/0009-exploration-layer-and-notebook.md)): a variation, the steps that made it, a note, and since version 12 the fact it came from (prep item 4).

```jsonc
"observations": [
  { "id": "o_0001", "name": "Give the melody more space",
    "factId": "motif.contour.arch",          // the fact of the Understand digest the experiment was built on; optional
    "note": "Airier.",                       // optional
    "createdAt": "…",
    "phrase": { "onset": 0, "duration": 3840, "filter": { … } },
    "experiment": { "id": "…", "alternative": 0 },   // optional
    "variation": { … } }                     // the kept chain; optional
]
```

Any number of ideas can sit on one phrase. Which phrase an idea belongs to after the phrase level changes is worked out, not stored: the phrase that contains the idea's onset, else the phrase it overlaps most (`Observation.Home`). The `sessionId` of versions 8–11 is dropped.

## Check (`check`)

What the scan server, the transcription or a performance left for the player to look at, as data, in one format for all three (prep item 6).

```jsonc
"check": [
  { "bar": 12, "source": "Scan" | "Transcription" | "Performance",
    "reason": "BarTooLong" | "BarTooShort" | "StavesDiffer" | "OutOfPianoRange" | "GraceNotes"   // scan
            | "PartSeam" | "TempoUncertain" | "DenseCluster" | "HandsGuessed"                     // transcription
            | "RecordingOctave" | "RecordingBarLength",                                           // a performance (version 13)
    "resolved": false }
]
```

`null` only in a file written for a project that was never opened since it was read from an older version; opening such a project works the flags out from the piece (`CheckFlag.FromScan`, or `CheckFlag.FromTranscription` for a transcription) and marks the bars the player had confirmed as resolved.

A transcription's flags (prep item 12) are worked out from the notes the engine heard and the bars worked out for them: `PartSeam` for the bars on either side of a seam; `TempoUncertain` for a bar where at least half of four or more onsets sit more than 30 ticks off both the 16th and the triplet-8th grids; `DenseCluster` for a bar with four onsets within a 16th or a chord of three or more notes a second apart; `HandsGuessed` for a bar with two or more notes whose hand was guessed with confidence below 0.6. A reason other than `PartSeam` that would flag more than a quarter of the bars is left out, since it then says more about the recording than about a bar. Resolving a flag on a scanned piece also confirms the bar in `scan.confirmedBars`, so older readers see the same thing.

A performance added to a scanned piece raises its own flags (source `Performance`, [0022](decisions/0022-score-and-performance.md), section 6): `RecordingOctave` where at least three quarters of a hand's notes are played 12 semitones from the scan for two bars or more, and `RecordingBarLength` where the scan flagged a bar too long or too short and the performance measures it at the meter's length at the neighbouring bars' tempo. Removing the last performance drops them.

## Performances (`performances`)

A performance compared with the piece (version 13, [0022](decisions/0022-score-and-performance.md), section 8): its notes as MIDI, the anchors that line it up with the score, the bars the player said are not a difference, and the bars taken into the score. The recording's sound is never in the file; it stays on the device.

```jsonc
"performances": [
  { "id": "perf_1", "name": "Live at the National Gallery",
    "source": "Recording" | "Midi" | "Score",   // Score: a written score such as MusicXML, kept as MIDI
    "engine": "transkun 2.0.1",          // only a recording
    "midi": "<base64>",                  // the engine's MIDI, the player's MIDI file as it was, or a score written out as MIDI
    "recordingStart": 0,                 // microseconds into the recording where the transcribed stretch starts
    "anchors": [[0, 0], [1440, 1500000]],   // [score tick, microseconds], both strictly increasing
    "dismissed": [7],                    // bars marked "not a difference"
    "taken": [
      { "bar": 5,
        "removed": ["n_0031"],           // printed notes the performance does not play
        "added": [ { "id": "perf-perf_1-n_0042", "pitch": 79, "onset": 4560, "duration": 120,
                     "velocity": 64, "track": 0, "hand": "Right" } ] }   // hand absent when unknown
    ],
    "merge": {                           // optional (within version 14): the merge draft's choices, to open again
      "appliedAt": "2026-11-07T12:30:00.0000000+00:00",   // null after "Undo the merge"; the choices stay
      "bars": [ { "bar": 5, "choice": "Version" },        // Mine, Version, Own or NotADifference
                { "bar": 7, "choice": "Own", "own": { "bar": 7, "removed": [ … ], "added": [ … ] } } ] } }   // own: the bar the player wrote, as a taken bar
]
```

`merge` is written once the player applies a merge draft ([0022](decisions/0022-score-and-performance.md), section 5). Applying it sets `taken` to the bars the draft takes and adds its "not a difference" bars to `dismissed`; undoing the merge empties `taken` and sets `appliedAt` to null. A file without it opens with no saved choices, its taken bars read as "from the version" and its dismissed bars as "not a difference".

The score a project opens with is the imported notes, then the edits, then the taken bars: removed ids are left out (an id that is no longer there is ignored) and added notes come in marked user-edited. Bars cannot be changed while bars are taken. The array is empty when there are no performances, and a file before version 13 reads as none.

## Assembly (`assembly`)

The assembly of the player's own version from the piece's sources (version 15, [0024](decisions/0024-assembling-a-version.md), section 7): the journal the player's notes are rebuilt by, and after «Сделать нотами» the notes the piece plays since then and the notes before the first apply. Left out while the piece has none.

```jsonc
"assembly": {
  "next": 4,                                  // the number the next step takes
  "steps": [
    { "number": 1, "at": "…", "kind": "carry",
      "source": "perf_1",                     // a performance's id
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

The notes a project opens with: with `applied`, those notes, its bars and tempo, then the edits made since (the taken bars of `performances` are part of them); without it, as before. The journal builds from `before` once there is one, otherwise from the notes the project opens with. A file before version 15 reads as no assembly.

## Settings (`settings`)

Only the per-piece overrides live in the file (prep item 5); the catalogue (`MotiffLab.MusicCore.Settings.SettingsCatalogue`) says which settings have the piece scope.

```jsonc
"settings": { "phrases.granularity": "sentences", "layers.ink": "off" }   // sorted by id; empty when none
```

Settings have three scopes. **Device** settings (layout, note names, pencil-only, pen ink, metronome, count-in, follow playback, theme, language, save online, analytics) stay on the device as one document, `{"version": 1, "values": {…}}`, written and read by `SettingsState`; the free tier has no cloud account, so they never leave it. **Account** settings (level, guidance, assistant answers) follow the player once there is an account; until then they are on the device too. **Piece** settings (phrase granularity, the layers) can be overridden per piece, here. The presets Learning, Practising and Clean set the governed settings together; a value that deviates from every preset shows as Custom.

A piece added as a PDF to read as it is ([0023](decisions/0023-pdf-pieces.md)) keeps the PDF in `document`, exactly as the player picked it, and has an empty `source` until its notes are recognised: such a project has no notes to open, so the apps show it in their PDF view. `document` must start with `%PDF-`; a project with neither a `source` nor a `document` is damaged. The player's marks on the PDF's pages are kept in `documentMarks`, apart from `marks`, so the PDF view and the Notes view each keep their own: `ink` holds strokes in the shape of `marks.ink`, except that `bar` is the number of the page the stroke starts on (from 1), `u` its place across the page (0 to 1) and `v` its distance below the page's top edge in the page's own spaces (a hundred and twentieth of its width); `bookmarks` holds `{ "id": "pb_0001", "page": 3, "name"?: "Coda", "createdAt" }`, at most one per page, in page order. The field is left out when there are no such marks. The web app and the iPad keep a PDF on the device only, never online: a PDF can run to tens of megabytes, so a project without notes stays on the device, and the copy of a project with notes saved online leaves `document` and `documentPages` out; when the sync brings the online copy back (version 0.2, `ProjectSync.Pulled`), the device's own PDF and its pages stay with the piece. Builds before 0.4 leave `documentMarks` out of the online copy too. Since version 0.4 (Andrei, 2026-11-12; a sync rule, the format does not change) the online copy carries `documentMarks`, so another device that has the same PDF, linked by its fingerprint, shows them: only one device writes them (`Project.WritesDocumentMarks`), the device that has the PDF, and the iPad when both have it (the PDF's original is marked held by the iPad); a pull keeps the writer's own marks and gives every other device the online copy's, except that an online copy with no `documentMarks` (as a build before 0.4 sends it) never clears a device's marks. The web shows such marks read only. What a device knows of a project's online copy for the sync (its ETag and save time) is kept on the device beside the projects, never in the project file, so the format does not change.

A project can hold both notes and a PDF (since 2026-11-07, testing item 16; no change to the format, version 14): the notes read from its own PDF by the scan server, or a PDF the player kept inside a piece opened from a file or a recording (Sources, [0022](decisions/0022-score-and-performance.md) section 1). For notes read from the PDF, the MusicXML becomes `source` (with `fileName` ending `.musicxml`, the PDF's name with its extension changed) and `scan` says which engine read it. `documentPages` holds where each of the PDF's pages starts in the notes, in ticks: page 1 at 0, each page after the one before. It is the rough link the PDF and Notes views switch by: a page starts at the first bar the scan server marked with `<print new-page="yes"/>`, or, without such marks, the bars are shared evenly between the pages. `documentPages` needs both a `document` and a `source`; the PDF, `documentMarks` and the notes' own `marks` all stay.

From 2026-11-05 to 2026-11-07 the notes read from a PDF made a separate project, an ordinary scanned piece with `source` and `scan`, and the PDF's project was not changed. Such notes keep `readFrom`: the PDF piece's `project` id and, in `pages`, where each of its pages starts in the notes, in ticks, as `documentPages`; PDF | Notes switches between the two by it, and the library shows only the notes' card. `readFrom` needs a `source`, and no app writes it any more.

## Note corrections (`edits`)

The player's corrections of the notes, oldest first (S1-I7): the imported notes never change, and the notes the apps show are the imported ones with every correction applied in order. Each correction names its note and the values it found, so a log that no longer fits the notes is refused rather than half applied. The note editor on the roll (testing item 15) writes them only when the player saves.

```jsonc
"edits": [
  { "noteId": "n_12", "before": { "pitch": 64, "onset": 960, "duration": 240, "velocity": 80 },
                      "after":  { "pitch": 65, "onset": 960, "duration": 240, "velocity": 80 } },   // changed
  { "noteId": "n_13", "before": { … } },                                                           // taken out: no "after"
  { "noteId": "a1", "added": { "track": 0, "hand": "Right" }, "after": { … } }                     // added (version 14): no "before"; "hand" only when the piece has hands
]
```

An added note's id is the first of `a1`, `a2`, … that no note of the piece or the log has; it can be changed or taken out like any other.

## Opening an older file

Every version since 1 opens (`ProjectFile.Read`). The rules for the versions before 12, in addition to the earlier ones kept in `ProjectFile.Version`'s comment:

| Versions | What the reader does |
| --- | --- |
| 8–11 | Each session becomes a resume point with the same number (`s_0001` → `r_0001`), named by the session's task, at its passage, with the session's `lastActive`; its intent is dropped. |
| 6–11 | Observations lose `sessionId`; `factId` is absent. |
| 15 without `metadata` | Written before the library kept a title, composer and tags: none; the card shows the file's title and composer, or its name. |
| 15 without `originals` | Written before version 0.3, or written again by a version 0.2 build: the originals the piece's own data shows (see "Originals"), without fingerprints or marks. |
| < 15 | No assembly. |
| < 14 | No corrections that add a note. |
| < 13 | No performances; no `Performance` flags. |
| 12 without `transcription.seams` | The recording went in one part, or was transcribed before seams were kept: no `PartSeam` flags. |
| < 12 | No marks, bookmarks (the four built-in categories only) or overrides. `check` is null until the project is opened, then worked out from the piece and the scan's confirmed bars. |
| 12 without `phrases.short` / `phrases.sentences` | Written before the three phrase levels ([0018](decisions/0018-redesign.md), section 3): the Short and Sentence levels have no edits, so the engine suggests them. The level shown is the override `phrases.granularity`, as before. |

Pencil marks saved on the device before version 12 (the `motiff-lab-pencil` IndexedDB database, [0015](decisions/0015-ipad-app.md)) are taken into the project the first time its score is shown and then forgotten on the device. A piece open without a project (Für Elise from the start page) still keeps its marks on the device.

## Unknown enum values

A later build can add a value to an enum while the file keeps its version ([0026](decisions/0026-project-continuity.md), S6). A reader that does not know the value keeps the entity holding it as an unknown entity (`UnknownEntity`, with the enum's type name), out of the project's lists: it takes no part in playback, analysis or variations, and an app never meets it. The writer puts it back unchanged at the place it had in its array, so a file read and written with no change keeps every entity in the same order.

- **Only a value that is a name** (ASCII letters and digits, starting with a letter) and only in a file of the reader's own version. In an older file, or for any other text, the field is damaged as before: the builds that wrote an older file knew no value this one does not.
- **The entities kept so**: a motif (`motifs`), a variation (`variations`), an idea (`observations`), a stroke (`marks.ink`, `documentMarks.ink`), a highlight (`marks.highlights`), a bookmark category (`bookmarks.categories`), a resume point (`resumePoints`) and a Check flag (`check`), whatever the depth of the value inside it (a variation step's parameters, a passage's filter, a key's mode).
- **What goes with it.** A motif and its variations are kept or left unknown together: a variation cannot open without its motif, and an app that replaces a motif drops its variations, which it cannot do with one it does not see. A bookmark in an unknown category is left unknown with it. A built-in category with an unknown ink stays unknown and is not taken by the built-in one.
- **New ids** for bookmarks, categories, resume points, ideas and highlights pass the unknown ones by. An entity an app makes with the id of an unknown one (the facade's working motif `m_0001`) takes its place: the unknown one is not written, and neither is what went with it.
- **Still damaged**: an unknown value in a note correction (`edits`), a performance (`performances`) or the assembly (`assembly`), since keeping those aside would change the notes that sound (Andrei, #313); an unknown per-piece setting (`settings`) or variation operation; an unknown `Overlay` or Keep choice. Unknown fields are still dropped on writing, except inside an entity kept whole as unknown.

A file written by a newer version is refused with "This project was saved by a newer version of Motiff Lab", so the API ([0014](decisions/0014-hosted-scan-server.md)), which reads every file it stores, must be deployed with or before the apps that write a new version.