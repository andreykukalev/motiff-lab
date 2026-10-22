<!-- Exported from the Motiff Lab specification doc (https://claude.ai/artifact/8uR7nDk6Z7ar8vLjbsCMz3) on 2026-10-22. Edit here or in the doc; keep them in sync. -->

# MVP · Stage 1: Notation-Based Motif Exploration

## Stage 1 at a glance

The first release of Motiff Lab works on structured music, not audio. Players import a MIDI or MusicXML file, pick a short motif, explore its rhythm and melody, and transform it through controlled, explained variations. It is the first implementation milestone of the full vision, not a replacement for it.

| Item | Stage 1 decision |
| --- | --- |
| Goal | Validate that musicians find value in exploring a motif's structure and making meaningful variations of it |
| Primary user | The self-directed pianist from the Overview, plus songwriters who already work with MIDI |
| Input | MIDI (.mid) and MusicXML (.musicxml, .mxl) files |
| Core loop | Import → select motif → explore → preserve and modify → compare → save or export |
| Central feature | The variation engine, with explicit "preserve" choices and a plain explanation of every change |
| Learning | Discovery, Guided and Analysis modes for variations; light comparison tasks |
| Platform | Desktop and tablet browser; no backend required for the core loop |
| Output | Saved variations in the project; MIDI export |
| Not in Stage 1 | Audio import, reconstruction attempts with hints, PDF or image scores, reharmonisation |
| Exit gate | Usability, creative engagement and learning-potential criteria in the validation study below |

The guiding principle for this stage: help musicians move from reproducing existing music to understanding it, transforming it, and eventually creating independently.

## Where Stage 1 sits

Motiff Lab is built in four stages, each validated before the next is committed. Stage 1 starts with notation; Stages 2 and 3 are specified in the existing PRD, MVP UX, Domain & Architecture and Validation Plan tabs, which stay the long-term specification.

![development stages · 4 stages on one foundation](images/development-stages.png)

*Figure: development stages · 4 stages on one foundation*

| Stage | Goal | Input | Specified in |
| --- | --- | --- | --- |
| 1. Notation-based motif exploration | Validate the value of exploring motifs and making meaningful variations | MIDI, MusicXML | This tab |
| 2. Guided reconstruction and ear training | Turn exploration into explicit learning: attempts, progressive hints, feedback, independent reproduction | Notation, MIDI keyboard | PRD modules C and F, MVP UX flows 2, 3 and 6, Validation Plan studies 1–3 |
| 3. Audio-based reconstruction | Start from a recording instead of a score, through the same workspace | Solo piano recordings, later layered music | PRD module A, MVP UX flow 1, Domain & Architecture analysis pipeline, accuracy benchmark |
| 4. Advanced creative exploration | Reharmonisation, motif development, counter-melodies, personalised paths, DAW, more instruments, sharing where licensing permits | All of the above | Overview ideas backlog and could-have items |

The "phase 1–4" labels inside the PRD and other existing tabs describe the release plan for the audio-based product (Stages 2–3). They are kept as written; their phase 1 now starts after the Stage 1 exit gate.

## Why start with notation

Structured files remove the riskiest technical problem, polyphonic audio transcription, so the first months test the product's core idea: does exploring and transforming a motif help musicians understand and create? Notes arrive exact, so every hint, explanation and variation can be trusted.

| Question | Notation-first answer |
| --- | --- |
| What risk is removed? | Transcription accuracy (Overview risk 1), GPU cost, audio licensing of models, and the backend needed to run them |
| What is still validated? | Motif selection, structure exploration, the variation engine, preserve-and-modify, explanations, comparison, and early learning signals |
| What is not validated yet? | Learning by ear from recordings, which Stages 2 and 3 test with the existing PRD, UX and Validation Plan |
| What carries forward? | The event model, music-core, variation operations V1–V6, playback, the workspace layout and the brand |
| What is the trade-off? | Fewer players own MIDI or MusicXML files of the music they love; Stage 1 must make getting files easy (see Content sources) |

Because audio-derived and file-derived notes share one representation, Stage 3 adds a new input to the same workspace instead of building a second product.

## Scope

Stage 1 must deliver one complete loop on one motif; everything else is labelled with the stage where it arrives. Requirement IDs (S1-…) are defined in the next sections.

| Capability | Priority | Requirement IDs |
| --- | --- | --- |
| Import MIDI and MusicXML; keep tempo, meter, key, tracks, voices | Must | S1-I1 to S1-I6 |
| Correct or remove imported events; edit single notes | Must | S1-I7, S1-E7 |
| Workspace with piano roll, keyboard, playback, loop and tempo | Must | S1-W1 to S1-W6 |
| Manual motif selection by bars, track, voice or individual notes | Must | S1-M1 to S1-M5 |
| Rhythm and melody views (onsets, durations, rests, intervals, contour) | Must | S1-A1, S1-A2 |
| Variation engine with preserve-and-modify and explanations | Must | S1-E1 to S1-E10 |
| Undo, redo, A/B compare, multiple named variations | Must | S1-E5, S1-E6, S1-E8 |
| MIDI export of motifs and variations | Must | S1-X1 |
| Discovery, Guided and Analysis modes | Must | S1-L1 to S1-L3 |
| Projects saved on the device | Must | S1-P1 |
| Score view from MusicXML | Should | S1-W7 |
| Harmony view where chords exist (bass, chord labels, voicings) | Should | S1-A3 |
| Automatic motif suggestions (repeats) | Should | S1-M6 |
| Listening comparison tasks ("what changed?") | Should | S1-L4 |
| Starter library of public-domain files | Should | S1-C1 |
| Playback to a connected MIDI instrument | Should | S1-W8 |
| Reconstruction attempts, progressive hints, MIDI keyboard input and feedback | Later | Stage 2 (PRD module C) |
| Accounts, sync, mobile apps | Later | Stage 2 |
| Audio import and analysis | Later | Stage 3 (PRD module A) |
| PDF or photographed scores (optical music recognition) | Later | After Stage 3 |
| Harmonic colour, reharmonisation, counter-melodies, DAW integration | Later | Stage 4 |

**Out of scope for Stage 1**

- General-purpose audio-to-MIDI transcription, or transcription of full polyphonic recordings.
- PDF or image-based score recognition.
- Advanced reharmonisation and general-purpose AI composition.
- Full DAW functionality, or support for every instrument.
- Social features and public distribution of copyrighted arrangements.

## Functional requirements

These requirements cover everything in Stage 1 except the variation engine and learning modes, which follow in their own sections. Where a Stage 1 requirement matches one in the PRD tab, it is noted so the later stages reuse it.

### Import

| ID | Requirement | Priority |
| --- | --- | --- |
| S1-I1 | Accept .mid (Standard MIDI File type 0 and 1) and .musicxml or .mxl (MusicXML) files up to 5 MB | Must |
| S1-I2 | Parse notes (pitch, onset, duration, velocity), rests, chords, ties (merged into one note), tuplets and grace notes (flagged); exclude MIDI drum channel 10 with a notice | Must |
| S1-I3 | Keep the tempo map, time signatures, key signatures and pickup bars; MIDI without a time signature is read as 4/4 with a notice | Must |
| S1-I4 | Keep track, part and instrument names; split a piano grand staff into right and left hand by staff; for a single-track piano MIDI, infer hands by pitch and continuity, marked as inferred | Must |
| S1-I5 | Build measures; mark phrase boundaries only when the source supports them (slurs or phrase marks, rests of a beat or more, repeat signs) | Must |
| S1-I6 | Detect unquantised performance MIDI and offer to align it to the nearest 16th or triplet 8th; the original timing is kept | Must |
| S1-I7 | Let the player correct or remove imported events; changes go to the edit log as user-edited | Must |
| S1-I8 | Show an import report: tracks, notes, measures and any warnings | Must |

### Workspace and playback

| ID | Requirement | Priority |
| --- | --- | --- |
| S1-W1 | A project holds one source file, its motifs and their variations; it opens on an overview of the whole piece with one lane per track | Must |
| S1-W2 | Piano roll with measure and beat grid, zoom, colour and pattern by track or voice, selectable notes | Must |
| S1-W3 | Keyboard view lights the sounding notes during playback | Must |
| S1-W4 | Playback: play, pause, loop the motif, tempo 25–150%, metronome, count-in, solo and mute per track | Must |
| S1-W5 | Sources: original motif, variation, both layered, and A/B switching at the same position (as PRD FR-E6) | Must |
| S1-W6 | Switching between Explore, Transform and Compare keeps the selection and playback position (as PRD FR-B8) | Must |
| S1-W7 | Score view of the motif and its variation, from the MusicXML source or generated notation | Should |
| S1-W8 | Play through a connected MIDI instrument | Should |

### Motif selection

| ID | Requirement | Priority |
| --- | --- | --- |
| S1-M1 | Select a range by dragging across measures; edges snap to beats; 1–8 bars | Must |
| S1-M2 | Limit the motif to one track, part, hand or voice, for example the melody only | Must |
| S1-M3 | Select individual notes to form a motif (2–32 notes) | Must |
| S1-M4 | A motif keeps a link to its source position; "Show in piece" jumps there | Must |
| S1-M5 | Repeat-play a motif and duplicate it into a new variation | Must |
| S1-M6 | Suggest motifs from exact and transposed repeats in the melody | Should |

### Structure analysis

| ID | Requirement | Priority |
| --- | --- | --- |
| S1-A1 | Rhythm: onsets on the grid, durations, rests, subdivision and grouping, syncopations, accents where marked, repeated rhythmic patterns | Must |
| S1-A2 | Melody: pitch sequence, intervals (semitones and names), contour, repeated notes, scale degrees, phrase boundaries (from the source or set by the player) | Must |
| S1-A3 | Harmony, where two or more notes sound together: bass line, chord labels with inversion and alternatives, voicing spread, shared notes between neighbouring chords, harmonic rhythm | Should |

### Export, storage and content

| ID | Requirement | Priority |
| --- | --- | --- |
| S1-X1 | Export a motif or variation as MIDI (type 1, one track per part or hand, with tempo map), named after the project, motif and variation | Must |
| S1-P1 | Projects autosave in the browser; a project can be exported and re-imported as one file for backup | Must |
| S1-C1 | Starter library of at least 15 public-domain pieces as MusicXML | Should |

**Non-functional targets for Stage 1:** a 5,000-note file imports in under 3 s; variation previews play within 300 ms (as PRD NFR-5); loop restarts with no audible gap; works in current Chrome, Edge, Safari and Firefox on desktop and tablet (MIDI output in Chromium browsers only); WCAG 2.2 AA for app screens.

## Variation engine

The variation engine is the central Stage 1 feature. The player first says what must stay the same, then applies one understandable operation, hears it at once, and reads exactly what changed. Operation IDs continue the catalogue in the PRD (V1–V6), so later stages share one engine.

### Operations in Stage 1

| ID | Category | Operation | Keeps | Changes | Parameters |
| --- | --- | --- | --- | --- | --- |
| V1 | Rhythm | Rhythm reshape | Pitches and their order | Onsets and durations | Syncopate, dotted, half-time, double-time, displace by 1–3 eighths |
| V7 | Rhythm | Density and rests | Pitch order | Note count and rests | Repeat notes, split into 8ths or 16ths, merge repeats, insert or remove a rest at a chosen beat |
| V8 | Rhythm | Accent shift | Pitches and timing | Which notes are accented | Accent pattern: on-beat, off-beat, every 3rd note, custom |
| V4 | Melody | Melodic sequence | Rhythm, interval shape | Pitch level, repeats | Steps −4 to +4 scale degrees, repeats 1–3, diatonic or exact |
| V9 | Melody | Interval reshape | Rhythm, contour | Interval sizes | Widen or narrow selected intervals by 1–2 scale steps |
| V10 | Melody | Change direction | Rhythm, interval sizes in scale steps | Direction of movement | Mirror the whole motif, or flip selected intervals |
| V11 | Melody | Passing and neighbour notes | Main notes and overall length | Added notes between them | Passing, upper neighbour, lower neighbour; where: all gaps or selected |
| V2 | Accompaniment | Accompaniment pattern | Melody, harmony | How chords are played | Broken chord, arpeggio up, up-down, Alberti, repeated chord tones; 8ths, triplets, 16ths |
| V12 | Accompaniment | Bass pattern | Melody, harmony | Bass line | Roots only, root and fifth, octaves, stepwise walk |
| V3 | Voicing | Voicing and register | Melody pitch classes, rhythm | Inversion, spacing, register, hand distribution | Root, 1st or 2nd inversion; close or open; octave shift; move a chord tone to the other hand |
| V6 | Difficulty | Simplify | Melody contour, harmony | Fewer, easier notes | Levels 1–3 as in the PRD |

V6 is a Should for Stage 1. V5 (harmonic colour) and any reharmonisation wait for Stage 4. Accompaniment and voicing operations need chords in the source; on a single melody line they are disabled with a reason.

### Preserve and modify

Before choosing an operation, the player picks what to keep from nine properties: pitches, pitch order, contour, intervals, rhythm, melody, bass, harmony and register. Only operations compatible with those choices stay enabled, which turns each variation into a deliberate experiment. Common starting points are offered as one-tap presets:

- Keep pitches, change rhythm (V1, V7, V8).
- Keep rhythm, change intervals (V9, V10, V4).
- Keep the melody, change the accompaniment (V2, V12, V3).
- Keep the bass, change the upper voices (V3).
- Keep the contour, change selected notes (V9, V11).

### Requirements

| ID | Requirement | Priority |
| --- | --- | --- |
| S1-E1 | Offer the operations above, each changing one musical dimension by default | Must |
| S1-E2 | Preserve choices come before the operation; incompatible operations are disabled with a reason | Must |
| S1-E3 | Each operation has 1–3 parameters with defaults; the result plays within 300 ms of a change | Must |
| S1-E4 | Every variation gets an explanation built from its actual difference to the source, for example "The pitch sequence is unchanged, but 3 notes now start an 8th early, creating a syncopated accent on beat 2." | Must |
| S1-E5 | Variations are chains of steps over the original; the original is never overwritten; undo and redo up to 50 steps | Must |
| S1-E6 | Compare: A/B, both layered, piano-roll overlay of differences, side-by-side score when the score view is on | Must |
| S1-E7 | Edit single notes in a variation; a variation can also be made entirely by hand, and is treated as a first-class result | Must |
| S1-E8 | Accept or reject each step; save, rename, duplicate and delete variations, up to 30 per motif | Must |
| S1-E9 | After every step, check the kept properties; if a manual edit breaks one, its chip shows "no longer kept" without blocking | Must |
| S1-E10 | On request, generate up to 3 alternatives with different parameters; in Discovery mode only after the player's own attempt | Must |

Operation rules from the PRD still apply: results stay on the grid and in range, hand spans over a 10th are flagged, voices do not cross, and the same input always gives the same output.

## Learning modes

Stage 1 is an editing tool designed for learning: three modes decide how much the app suggests before the player acts. The player can switch mode at any time; the choice is stored per motif. The modes prepare for the hint ladder of Stage 2: the player tries first, and help comes on request.

| ID | Mode | The player gets | The player does | The app reveals | Priority |
| --- | --- | --- | --- | --- | --- |
| S1-L1 | Discovery | A musical constraint and a short goal, for example "Keep the pitches; make it feel more urgent"; an optional suggested operation and example | Makes their own variation by hand or with operations | Generated alternatives, only after the player saves an attempt | Must |
| S1-L2 | Guided | A short sequence of prompts | Follows each step and answers one listening question | The next prompt and an explanation | Must |
| S1-L3 | Analysis | Source and variation side by side with the rhythm, melody and harmony views | Inspects, edits, compares | Everything: diff list, explanation, structure views | Must |
| S1-L4 | Comparison task | Two versions played without labels | Says what changed and what was kept | The answer and the explanation | Should |

**Example guided sequence: "Rhythm changes character"**

1. Keep the original notes.
2. Change only the rhythm (V1, syncopate).
3. Listen to both versions.
4. Which feels more stable, energetic or anticipatory? (The answer is recorded, not marked.)
5. Try another rhythmic change (V7 or V8) and compare all three.

The first release ships five guided sequences: rhythm and character, contour and intervals, sequence a motif, melody over a new accompaniment, and voicing and colour.

**What Stage 1 records for learning**

For each motif: mode used, operations tried, kept properties, whether alternatives were opened before or after the player's own attempt, answers to listening questions and comparison tasks, and saved variations. These feed the Stage 1 validation study and later the skill profile (PRD FR-F4). Applying a learned operation to a different motif (transfer) is tested in the study, and becomes an in-app feature in Stage 2.

## UX flows

Stage 1 has seven screens, N1 to N7, built on the same workspace layout as the MVP UX tab so Stage 2 can add Reconstruct mode without a redesign. Requirement IDs in brackets.

![wireframe · N5 Transform in Discovery mode, desktop or tablet](images/wireframe-stage1-transform.png)

*Figure: wireframe · N5 Transform in Discovery mode, desktop or tablet*

The wireframe shows the Transform screen in Discovery mode: kept properties first, a single operation, its parameters, the original and variation overlaid, and the explanation in plain words.

**N1 · Projects**

- Rows of projects: file name, composer, number of motifs and variations, last opened.
- Primary action: **Import a file**. Secondary: a starter-library row "Start with a classic" (S1-C1).
- Empty state: a short line on what Motiff Lab does, the import button, and the starter library.

**N2 · Import**

1. Drop a file or pick one (S1-I1). Unsupported or damaged files get a plain reason and a tip, for example "This looks like a PDF. Motiff Lab reads MIDI and MusicXML; most notation apps can export MusicXML."
2. The import report lists tracks, notes, measures and warnings (S1-I8).
3. For performance MIDI, the app offers to align notes to the grid, with before and after playback (S1-I6).
4. The player picks the main track or hand to start with.

**N3 · Piece overview and motif picker**

- One lane per track with measure numbers and a compact piano roll; playback of any range.
- Suggested motif chips, for example "Motif · bars 5–6, repeats 4×" (S1-M6).
- Drag across measures to select (S1-M1); filter to a track, hand or voice (S1-M2); or click and lasso single notes (S1-M3).
- **Create motif** names it ("Motif 1 · bars 5–6"), then asks for a mode: Discovery, Guided or Analysis.

**N4 · Motif workspace, Explore**

- Transport: play, loop, tempo 25–150%, metronome, count-in, solo and mute (S1-W4).
- Piano roll and keyboard (S1-W2, S1-W3); score view toggle (S1-W7).
- Lens tabs: **Rhythm**, **Melody**, **Harmony** (S1-A1 to S1-A3), each with two or three plain facts, for example "6 notes · range of a 6th · moves down, then up".
- **Show in piece** jumps back to the source position (S1-M4).

**N5 · Transform**

1. **Keep** chips for the nine properties, with the five presets above them (S1-E2).
2. Operation cards; incompatible ones are greyed with a reason.
3. Parameter controls; the result plays automatically (S1-E3), and the explanation appears under the piano roll (S1-E4).
4. **Accept**, **Reject**, **Edit notes**, **Undo**, **Redo**; accepted steps form breadcrumbs, for example "Original › Syncopate › Repeat notes" (S1-E5, S1-E8).
5. In Discovery mode, a goal card sits on top, and **Show ideas** stays locked until the player saves their own attempt (S1-L1, S1-E10). In Guided mode, a prompt stepper replaces the goal card (S1-L2).

**N6 · Compare**

- Lanes for the original and up to three variations on one time axis; A/B, layered playback, and a differences overlay marked by shape as well as colour (S1-E6).
- Comparison task variant: versions play unlabelled, and the player answers what changed and what was kept (S1-L4).

**N7 · Variations and export**

- Saved variations, each with its name, explanation and kept-property chips.
- Actions: play, rename, duplicate, delete, **Export MIDI** (S1-X1).

**States to design**

Unsupported or damaged file; a file with no usable notes; accompaniment and voicing operations disabled for a single line; a very large file (suggest choosing a track); browser storage nearly full (suggest exporting projects); MIDI output unavailable outside Chromium browsers.

## Architecture and data model

Stage 1 runs entirely in the browser: files are parsed, analysed, transformed and played on the device, and only anonymous analytics leave it. It uses the same event model and music-core library described in Domain & Architecture, so Stage 3 can plug audio-derived events into the same pipeline.

| Component (from the vision) | Stage 1 implementation | Reused later |
| --- | --- | --- |
| 1. Import and parsing | TypeScript parsers for Standard MIDI Files and MusicXML (with .mxl unzip), normalising into the shared event model | Kept as the file input next to audio |
| 2. Musical event model | Events as in Domain & Architecture, with a new provenance value `imported` and confidence 1 | Same model for audio events |
| 3. Motif selection | Motif objects with note IDs, derived views and a source reference | Same |
| 4. Transformation engine | music-core operations V1–V12, compiled to WebAssembly | Same library on mobile and server |
| 5. Playback | Web Audio with an AudioWorklet and a sampled piano; Web MIDI output in Chromium | Mobile audio engine replaces it in Stage 2 |
| 6. Visualisation | Canvas piano roll and keyboard; score view rendered from MusicXML by an open-source engraver (OpenSheetMusicDisplay or Verovio, licence to confirm) | Same views |
| 7. Learning interaction | Mode state machine; guided sequences and comparison tasks stored as JSON content | Extended with hints and attempts |
| 8. Persistence | IndexedDB in the browser, autosave, project export file | Synced accounts from Stage 2 |

**Source reference on every imported event**

Each note keeps where it came from, so "Show in piece", corrections and exports stay exact:

```json
{
  "id": "n_0057",
  "type": "note",
  "pitch": 67,
  "pos": { "bar": 5, "beat": 1, "tick": 0 },
  "duration_ticks": 240,
  "velocity": 80,
  "hand": "right",
  "voice": "melody",
  "provenance": "imported",
  "confidence": 1.0,
  "source": { "file_hash": "sha256:…", "format": "musicxml", "part": "P1", "staff": 1, "voice": 1, "measure": 5, "index": 2 }
}
```

**New and changed objects for Stage 1**

| Object | Key fields | Notes |
| --- | --- | --- |
| SourceFile | hash, format, name, parts or tracks, import warnings | Stored with the project; never uploaded |
| Project | id, source file, created and opened times, mode default | The unit the player saves and exports |
| Motif | id, project, note IDs, range, track or voice filter, name, source reference, mode | Same derived views as in Domain & Architecture |
| Variation | id, motif, chain of steps, kept properties, name, explanation, result cache | Same as later stages, plus kept properties |
| LearningRecord | motif, mode, prompts answered, comparison results, alternatives opened before or after own attempt | Feeds the validation study |

The only server piece in Stage 1 is an analytics endpoint and static hosting. Accounts, sync and the analysis backend arrive with Stages 2 and 3.

## Content sources and rights

Stage 1 lives or dies on players having a file of music they care about, so getting a file must take minutes, and the rules on rights must stay simple: personal study, nothing shared.

| Source | How players get files | Rights notes |
| --- | --- | --- |
| Their own notation | Export MusicXML from a notation app, or MIDI from a DAW | Their own work, or their own study copy of someone else's |
| Public-domain score libraries | Download MusicXML or MIDI of older classical pieces | Licences vary per file and per edition; the app links to sources and shows the licence when known |
| Starter library | Built into the app (S1-C1) | Public-domain compositions encoded by us or under CC0 or CC BY; licence stored per file |
| Files found online for copyrighted songs | Players import them themselves | The app does not provide, host or share them; projects stay on the device; exports are for personal practice |

**Rules**

- The composition and a particular edition or arrangement can carry separate rights; the starter library uses only editions with a clear open licence.
- Projects and variations are never uploaded in Stage 1, which keeps copyrighted material on the player's device.
- The terms of use state that imports must be files the player may use and that exports are for personal practice.
- Licensed catalogues from publishers or score platforms are a later partnership option (Stage 4), not a Stage 1 dependency.

This is product thinking, not legal advice; the legal review planned in the Overview covers Stage 1 too.

## Success criteria and validation

Stage 1 validates the product experience, not the whole vision; learning outcomes are tested, not assumed. Thresholds are first proposals, agreed before the study starts.

| Area | Criterion | Measure | Threshold |
| --- | --- | --- | --- |
| Usability | Import a file and select a motif | Unaided completion within 5 minutes | 9 of 10 participants |
| Usability | Understand the operations | Predict what an operation will change before applying it | 80% of predictions right |
| Usability | Hear and compare | A/B or layered playback used without prompting | 90% |
| Usability | Edit and save a variation | Task completion | 90% |
| Creative engagement | Create at least one variation | Saved variations per participant | 90% save one or more |
| Creative engagement | Explain what changed | One-sentence explanation coded against the actual diff | 70% correct |
| Creative engagement | Name the kept property | Comparison task (S1-L4) | 70% correct |
| Creative engagement | Return to experiment | Opens another motif within 7 days, unprompted | 50% |
| Learning potential | Reproduce the original pattern | Play or enter the motif from memory after the session; teacher check | Reported, no threshold |
| Learning potential | Recognise original vs variation | Unlabelled listening task | 70% correct |
| Learning potential | Apply an operation to another motif | Do it by hand on a new motif; teacher rating 1–7 | 60% rated 4 or more |
| Learning potential | Create without relying on suggestions | Saved variations made before opening alternatives or with manual edits | 50% |

**Study design**

- 12 participants: 8 pianists from the primary audience and 4 songwriters who use MIDI. Each brings one file they care about; the starter library covers anyone without one.
- Session, 60 minutes: import and select (10), explore (10), Guided sequence (15), Discovery task with a constraint (15), comparison and listening tasks (10). Half start in Discovery and half in Analysis, to compare preference and the share of player-made variations.
- One week of free use at home, measured by analytics; then a 20-minute interview on whether the workflow solves a real problem, whether the transformations feel musically useful, whether it makes them more willing to experiment, whether they would use it repeatedly, and what they would pay.
- Measures, consent and data rules follow the Validation Plan tab.

## Milestones and exit gate

Stage 1 takes about 10 weeks of part-time work: eight to build and two to run the study. The audio-based roadmap in the Overview starts after this gate.

![Stage 1 milestones · 5 milestones, 1 gate](images/stage1-milestones.png)

*Figure: Stage 1 milestones · 5 milestones, 1 gate*

Milestone 3 is the riskiest: the variation engine must sound musical, and explanations must match every result.

| Outcome at the gate | Condition | Next step |
| --- | --- | --- |
| Continue | All usability and creative-engagement criteria met, and at least 2 of the 3 learning thresholds | Start Stage 2: reconstruction attempts, hints and MIDI input on notation (PRD modules C and F) |
| Change | One area falls short with a clear cause | Fix it in 2–4 weeks and run 4 more sessions |
| Stop or pivot | Fewer than half create a variation they value, and few return | Rethink the variation-centred approach; test the reconstruction-first path from the PRD instead |
