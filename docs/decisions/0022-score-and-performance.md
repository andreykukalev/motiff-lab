# 0022 · A score merged with a performance

Status: proposed, 2026-11-05. Andrei: "the official score often differs a lot from what the performer really plays". He learns pieces the way they are played on a recording, by ear, with all of the performer's touches, and asked for a way to take the printed score and a performance and bring the performer's choices into the score. Also: the score is musical context that can correct the recognition of the recording, and the recording can correct the recognition of the score. "I have not seen such a feature anywhere."

## Context

A prototype without UI (2026-11-04, `/mnt/project-files/merge-prototype/`, not in the repository because the inputs are Andrei's purchased scores and recordings) took two of his pieces, recognised the printed PDF with homr and the recording with Transkun, aligned the two by onsets and compared them bar by bar:

- Einaudi, "Santiago": 492 of 666 printed notes matched (74 %). Found: a filling figure over held chords repeated in fifteen bars, a pulsing bass note in the introduction, a different bass (the root instead of the pedal note of the print) in four bars, a simplified left hand in six bars.
- Paterlini, "All my joy, all my pain": 438 of 566 printed notes matched in the recorded part (77 %). Found: how the ornaments are actually played, added inner voices in the left hand, no false differences over eight bars of arpeggios, and two bars where homr lost an octave clef and the recording shows it.
- Noise: the lowest octaves, which Transkun often misses; overtones an octave or a twelfth above a sounding note; misalignment in a freely played introduction.

Andrei's verdict: the comparison score (performance on small staves above the print; plum for played but not printed, amber for printed but not heard, grey for a likely artefact) is "very clear", and most useful to experienced players who aim at the composer's real performance rather than the printed score.

What exists: transcription with bars, tempo and key worked out from the playing (0016), the "As played" writing, the ossia above the original in one MEI document and the geometry module for overlays (0021), the Check layer and drawer (0018, section 9), the two views of a piece decided on 2026-11-04 ("PDF" as is, "Notes" engraved and interactive).

## Decision

### 1. One piece, one or more performances

**Another version, not only a performance** (Andrei, 2026-11-05: "there is no split into as played and as printed"). Whatever is open is the base, and any other version of the piece can be added over it: a MIDI file, or a score in MusicXML, which is written out as MIDI through its tempo marks and kept the same way (`VersionFile`, source `Score`). So a piece opened from notes heard in a recording can take the official score over it, and the comparison shows where the two differ in the same words. The interface says "Add a source" and "Sources" (since 2026-11-07; "Add a version" and "Other versions" before); the engine and the project file keep the name `performance`. A PDF read as it is has no notes to compare with; adding a version to it first needs its notes recognised (0023), which since 2026-11-07 go into the same piece; the PDF itself stays as it is.

A performance is added to a piece in the "Notes" view (MusicXML or MIDI import, or a recognised scan). It comes from one of:

- a recording of the player's own file, transcribed on the scan server (0014; Pro, since it runs on a server). Only the player's own file: no links, no YouTube (decided 2026-10-31). Built 2026-11-05 as From a recording in the Other versions drawer: the whole recording is heard (up to 7 minutes until 0014 item 7 lifted the limit in version 0.3), in parts as on Transcribe a recording, with the same line and switch for what leaves the device; its sound is not kept, only the notes heard, as a version from a recording;
- a project already transcribed, so a recording heard once is not sent again;
- a MIDI file of a performance (a digital piano, a performer's MIDI): on the device and free.

Every source takes one of two roles. **The notes** (as written): MusicXML, notes recognised from a printed PDF, or a MIDI file written on the grid (an export from a notation program). **The performance** (as played): notes heard in a recording, or a MIDI file played in by hand, such as the free MIDI a cover performer often shares while the score is sold (Andrei, 2026-11-05). A MIDI file can be either; the app proposes the role from its timing (`PerformanceTiming`: quantised or played) and the player confirms it. The purchased PDF itself is never changed: it stays the "PDF" view with the player's marks, and the merge lives in the "Notes" view. A performance MIDI has no recording, so *Performer* in section 5 plays its notes through the app's own sound.

A piece may hold several performances (a concert and a studio take); one is shown at a time and a chip picks it, as with variations. A performance has a name the player gives ("Live at the National Gallery").

**The Sources sheet** (testing item 16; Andrei, 2026-11-06: "the PDF is available right inside the piece; everything there is is picked up"). The Other versions drawer became Sources, with "My version" as its heading: a line on what the player's version holds beyond the base ("Base: the notes of the file · 1 other version · 12 bars taken · 9 corrections", `PieceSources`), then a row for the base, for the PDF kept inside the piece (Open PDF, and Read as a version, Pro on Motiff Lab's server) and for each other version with its role (Notes or Performance) and, for the one shown, how much of it lines up. One Add a source takes a MIDI or MusicXML file, a PDF (kept in the piece as it is, its pages shared over the bars until its notes are read, never sent online, `Project.WithDocument`) or a recording. Once a version is added, four next steps: Compare (the layer), Hear it alone (the phrase with its first differing bar, in focus, as in the version), Take everything sure (every lined-up bar the comparison is at least 80 % sure of, not dismissed, at once, `PerformanceTake.TakeAll`; Give back returns them) and Adjust it myself (the note editor on the roll at the first differing bar). The second file panel of the old workspace is gone; its track mute and solo stay in the playback options.

### 2. Alignment and the time map (music-core)

`MotiffLab.MusicCore.Performance` aligns the score's onset groups (notes starting together) with the performance's by a global alignment with gaps, scoring exact pitches and pitch classes in common and penalising tempo jumps. Matched groups that agree become anchors; between anchors the map from score ticks to recording milliseconds is linear, and it is monotone everywhere. Everything is in integers and exact fractions, deterministic, and tested on public-domain fixtures.

A bar whose groups find no anchors close enough (a free introduction, a cadenza, a cut) is marked unaligned: no difference is claimed in it, and its marker says so.

The anchors are stored with the performance (section 8), so a later change of the alignment never moves notes the player has already taken into the score.

### 3. The comparison, bar by bar

Within each aligned bar a performance note matches a printed note of the same pitch within half a beat, in the score's time. What is left is:

- **played, not printed** (plum);
- **printed, not heard** (`--warning`, the amber text colour);
- **likely an artefact** of the recording's recognition (grey): a quiet note, or one an octave or a twelfth above a note that sounds at the same time. A printed note below the range Transkun hears reliably is "probably not heard", not a difference.

The differences of a bar are summed up in one short kind, inferred and labelled as such: *added notes* (a filling figure, an inner voice), *different bass*, *ornament as played*, *simplified* (printed notes left out), *octave*, *rhythm*. Each comes with its counts and a confidence. These are facts about two sets of notes; any wording beyond them belongs to the assistant (0003).

### 4. On the page: a clean score, markers under Extra information

Chosen by Andrei on 2026-11-05; changed on 2026-11-07 (testing item 33, "everything about versions only in the draft, no extra staves in the main score"):

- **The score shows the player's notes only.** What the app adds over them is *extra information*, behind the ⓘ button in the top bar (`layers.extra`, off by default); its chevron lists each kind: **Differences from versions** (`layers.performance`: the plum markers above the bars that differ, with their kind and count, "+3 · added notes", and the printed notes the version does not play in the `--warning` text colour), **Bars taken from a version** (`layers.taken`: a thin plum line under each taken bar) and **Check flags** (shown also while the Check drawer is open). The player's own marks (pencil, notes and highlights, bookmarks, analysis) are not extra information and show as before.
- **A tap on a marker** opens a popover "Bar N · differs from «name»" with the kind and counts, **▶ As in the version** (the bar looped as the version plays it) and **Open in the draft**. Nothing is taken from the score itself.
- **The comparison layer** (`layers.version`, the version on small staves above every system) and the artefacts line under it (`layers.artefacts`) were removed from the main score: the version is read in the draft's views instead. The two settings stay in the catalogue so older files still open.

Plum and amber keep their meaning in the design system: plum is what came from another version, amber what the player marked or wrote; "printed but not heard" is a reason to check, like the Check flags, so it takes the amber text colour, never the amber fill of the player's own marks.

### 5. Merging a version through a draft

Superseded on 2026-11-07 by [0024](0024-assembling-a-version.md): the player's own version is assembled from fragments of independent sources, and the draft below leaves the interface once the web app's step of 0024 is in.

Built on 2026-11-07 (testing items 32 and 34; Andrei: "no irreversible actions; explicit Undo, Redo and Reset"). Taking bars one at a time from markers and focus is replaced by a merge draft that changes nothing until applied (`PerformanceMerge`, `MergeDraft` in music-core).

- **Sources in two levels.** The list of the piece's sources; a version's row opens its page, with "‹ Sources" back. The page has a verdict ("fur-elise-cover plays 2 bars differently, 1 of them sure."), a strip of the piece's bars (grey where the version agrees, plum where it differs, darker where sure; a tap goes to the bar) and three ways into the draft: **Keep my notes**, **Take the sure ones (N)** (from 80 %) and **Take the version wherever it differs (N)**. **Apply at once, without a draft** (`merge.direct`, on the device, off by default) applies the chosen start straight away, still undoable from the toast.
- **The draft** (dashed plum): ‹ › through the changes, **Bar | Phrase** (a choice for every differing bar of the phrase), **Take mine** (graphite), **Take from the version** (plum), **Edit in the roll** (the bar written by the player in the piano roll, the version's notes as dashed plum outlines; kept as the player's own bar), the list of changes with each one's state, and **Undo**, **Redo**, **Reset the draft** (itself undoable), **Cancel** and **Apply**.
- **Three views**, colour coded: **Mine** (graphite), **Version** (plum) and **Result** (green, how it will be after Apply: the version's notes in plum, the player's own in amber). While a draft is open a floating "Show: Mine · Version · Result" switch over the score chooses what the score shows; listening offers **Mine · Version · Result · In turn**. A bar chosen anywhere moves the views and the score to it.
- **Compare versions**, the full-screen window over the same draft: the changes grouped by kind (added notes, other notes, rhythm) with filters (all, sure, doubtful, decided) and **Take the group**; the three views stacked, engraved as excerpts, with **Showing: ± 1 bar · the whole phrase · the whole sentence** and a separate note size; **Not a difference** and **Reset the bar**; keys M, V, E, ← and →, ⌘Z and ⇧⌘Z.
- **After Apply** the page says "Merge applied HH:MM · N bars" with **Open the draft again** and **Undo the merge**; undoing keeps the choices to open again. Removing a version can be undone from its toast until the piece is closed.
- **In phrase focus**, with no variation in play, the card still shows the version's bars of the phrase as the ossia "As in «name»", heard as Notes / Version / Alternate; the focus bar offers **Open in the draft** in place of taking bars there.

### 6. The score and the recording correct each other

- **The score helps the recording.** In aligned bars the performance's notes are written with the print's bars, meter, key and spelling, and with the print's lengths where a note matches; a printed low note the recording probably lost is shown as such instead of as a difference. This replaces the bars and tempo worked out from the playing alone (0016) wherever the alignment holds.
- **The recording helps the score**, for a recognised scan only, where the printed notes are themselves inferred. Two rules first: every note of a staff an octave away from the recording over two bars or more (a lost octave clef), and a bar whose length disagrees with its meter while the time map gives it the meter's length. Each becomes a Check flag from the performance (`CheckSource.Performance`) with its own reason, **Recording: other octave** (`RecordingOctave`, which can be fixed by moving the notes) or **Recording: other bar length** (`RecordingBarLength`, which says how long the recording plays the bar and leaves the fix to the player), with **Show**, **Fix from the recording** where it applies, and **Keep as printed**. Nothing changes without the player. On homr's reading of the first page of Für Elise against Transkun's hearing of a recording of the whole piece, the second rule picks out three of the seven bars the scan read too long (16, 21 and 27), each played in 3/8.
- **Every note says where it comes from**: in the print and the recording; only in the print; only in the recording; corrected from the print; corrected from the recording; taken from the performance. Provenance stays `Imported`, `Detected` or `UserEdited` (0004); the relation to the other source is a separate label on the comparison, so no import fact is overwritten.

### 7. Free and Pro

Following 0018's rule: transcribing a recording runs on the server and is Pro. Alignment, comparison, markers, the merge draft, focus, listening and taking run on the device and are free, so a MIDI performance or a recording already transcribed works without Pro.

### 8. What is kept

The project file gets a `performances` list (version 13; the format is written in `project-file.md` with the code): per performance its id and name, its notes (the engine's MIDI, as a transcription's source today, with its transcription origin), the anchors, the bars marked "not a difference", the bars taken with their notes, and the merge draft's choices with when they were applied (an optional `merge`, within version 14). Taken notes are edits of the piece with their source, so the score, analysis and playback read them like any correction.

The recording's sound is not in the project file: the app keeps it on the device (IndexedDB on the web, the app's files on iPad), never sends it to the API, and asks for the file again when it is missing. Listening to the performer needs it; everything else does not.

### 9. Not yet

- **The performance as the base**: the recording's notes as the main score, with bars, chords and spelling corrected from the print. After the score-based flow works.
- Aligning a performance with repeats played differently from the print (a repeat skipped, a da capo): the first version aligns the score as written out and marks what it cannot place as unaligned.

## Consequences

- Order of work: the engine in music-core (alignment, time map, comparison, kinds, the two recognition rules) with tests; the facade and project file version 13; the web UI after the redesign's PDF and Notes views (thread "Build the redesigned app UI"); the iPad screens with the rest of the iPad redesign (since [0025](0025-two-studios.md) section 7 the iPad gets a simplified compare instead, listen to another version and choose, and taking bars into the score stays on the web; the steps below are the plan as it was). On iPad the first step brings the bar markers, the printed notes the version does not have tinted, and Other versions from More (add a MIDI or MusicXML file, Show, Not a difference, Take into the score, Restore, rename, remove); taking or restoring a bar engraves the score again and the player and the piano roll take the new notes. The second brings the comparison layer (the layers `version` and `artefacts` in More, the score engraved again with the version above every system) and, in focus with no variation open, the version above the phrase «Как в «…»», Notes / Version / Alternate, and Take into the score or Restore these notes for the phrase's bars. The third brings a recording as a version: in Other versions, with Motiff Lab's scan server, an account and Pro, the iPad reads the sound of an audio or video file (mono, 22,050 Hz, up to 7 minutes), sends each part music-core plans to the scan server only once the player turns on sending, and music-core joins the notes heard and lines them up as the web app does; a build without the hosted scan server says so. The fourth (2026-11-07, testing items 32–34) brings the merge draft as on the web: Sources in two levels with the version's page, the draft with Undo, Redo and Reset, the score showing Mine, Version or Result while it is open (`performance.merge.notation` without `bars` engraves the whole piece), Compare versions full screen with the keys of a keyboard, the marker's popover, Open in the draft from focus, and ⓘ for extra information; writing a bar in the roll as the player's own comes to the iPad later.
- Tests use public-domain music only: a Mutopia score and its MIDI, and performances made from them by known changes (added notes, a changed bass, a dropped octave, rubato), so each kind and each rule has an expected answer. Andrei's pieces stay out of the repository.
- Bar markers and the comparison layer depend on the geometry module (0021) and the layers menu (0018, section 6); the "Performer" playback needs a second audio source in the playback engine (0007), driven by the time map.