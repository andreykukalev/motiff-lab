# The project file (`.motiff.json`), version 12

The project file is what a player keeps of a piece: the imported source, the work on it, and since version 12 everything the redesign keeps with the piece ([0018](decisions/0018-redesign.md), section 7; [0020](decisions/0020-music-core-facade.md)). It is written and read by `MotiffLab.MusicCore.Projects.ProjectFile`, by hand, so the same project gives the same bytes. The web app saves it in IndexedDB and, when the player turns saving online on, sends it to the API ([0014](decisions/0014-hosted-scan-server.md)); the iPad app reads and writes the same file through the facade. A file of an earlier version always opens; the rules are at the end.

Ticks are integers at 480 per quarter ([0002](decisions/0002-canonical-time.md)). A span is `onset` and `duration` in ticks. Times are ISO 8601 with offset. Enum values in the file are the C# names (`Carmine`, `PianoRoll`); the facade answers them in lower camel case.

## The document

```jsonc
{
  "format": "motiff-lab-project",
  "version": 12,
  "id": "p_…",                 // the project's id
  "fileName": "minuet.musicxml",
  "source": "<base64>",         // the imported file, MIDI or MusicXML, as it was
  "createdAt": "…", "lastOpened": "…",
  "motifs": [ … ],              // the working selection (version 1)
  "variations": [ … ],          // chains of steps on a motif (version 2)
  "observations": [ … ],        // ideas (version 6; factId since 12)
  "edits": [ … ],               // note corrections (version 3)
  "phrases": { "added": [ticks], "removed": [ticks],   // the player's phrase boundaries (version 5)
               "short": { "added": [ticks], "removed": [ticks] },       // cuts inside phrases at the Short level (0018, section 3; read as none when absent)
               "sentences": { "added": [ticks], "removed": [ticks] } },  // phrase boundaries where a sentence must end, and ones it runs on across
  "marks": { … },               // version 12
  "bookmarks": { … },           // version 12
  "resumePoints": [ … ],        // version 12; replaces "sessions" (versions 8–11)
  "check": [ … ] | null,        // version 12
  "settings": { … },            // version 12
  "scan": { … },                // only a scanned piece (version 7)
  "transcription": { … }        // only a transcribed piece (version 9; bars since 10)
}
```

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

What the scan server or the transcription left for the player to look at, as data, in one format for both (prep item 6).

```jsonc
"check": [
  { "bar": 12, "source": "Scan" | "Transcription",
    "reason": "BarTooLong" | "BarTooShort" | "StavesDiffer" | "OutOfPianoRange" | "GraceNotes"   // scan
            | "PartSeam" | "TempoUncertain" | "DenseCluster" | "HandsGuessed",                    // transcription
    "resolved": false }
]
```

`null` only in a file written for a project that was never opened since it was read from an older version; opening such a project works the flags out from the piece (`CheckFlag.FromScan`) and marks the bars the player had confirmed as resolved. Resolving a flag on a scanned piece also confirms the bar in `scan.confirmedBars`, so older readers see the same thing.

## Settings (`settings`)

Only the per-piece overrides live in the file (prep item 5); the catalogue (`MotiffLab.MusicCore.Settings.SettingsCatalogue`) says which settings have the piece scope.

```jsonc
"settings": { "phrases.granularity": "sentences", "layers.ink": "off" }   // sorted by id; empty when none
```

Settings have three scopes. **Device** settings (layout, note names, pencil-only, pen ink, metronome, count-in, follow playback, theme, language, save online, analytics) stay on the device as one document, `{"version": 1, "values": {…}}`, written and read by `SettingsState`; the free tier has no cloud account, so they never leave it. **Account** settings (level, guidance, assistant answers) follow the player once there is an account; until then they are on the device too. **Piece** settings (phrase granularity, the layers) can be overridden per piece, here. The presets Learning, Practising and Clean set the governed settings together; a value that deviates from every preset shows as Custom.

## Opening an older file

Every version since 1 opens (`ProjectFile.Read`). The rules for the versions before 12, in addition to the earlier ones kept in `ProjectFile.Version`'s comment:

| Versions | What the reader does |
| --- | --- |
| 8–11 | Each session becomes a resume point with the same number (`s_0001` → `r_0001`), named by the session's task, at its passage, with the session's `lastActive`; its intent is dropped. |
| 6–11 | Observations lose `sessionId`; `factId` is absent. |
| < 12 | No marks, bookmarks (the four built-in categories only) or overrides. `check` is null until the project is opened, then worked out from the piece and the scan's confirmed bars. |
| 12 without `phrases.short` / `phrases.sentences` | Written before the three phrase levels ([0018](decisions/0018-redesign.md), section 3): the Short and Sentence levels have no edits, so the engine suggests them. The level shown is the override `phrases.granularity`, as before. |

Pencil marks saved on the device before version 12 (the `motiff-lab-pencil` IndexedDB database, [0015](decisions/0015-ipad-app.md)) are taken into the project the first time its score is shown and then forgotten on the device. A piece open without a project (Für Elise from the start page) still keeps its marks on the device.

A file written by a newer version is refused with "This project was saved by a newer version of Motiff Lab", so the API ([0014](decisions/0014-hosted-scan-server.md)), which reads every file it stores, must be deployed with or before the apps that write a new version.
