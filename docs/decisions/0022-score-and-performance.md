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

**Another version, not only a performance** (Andrei, 2026-11-05: "there is no split into as played and as printed"). Whatever is open is the base, and any other version of the piece can be added over it: a MIDI file, or a score in MusicXML, which is written out as MIDI through its tempo marks and kept the same way (`VersionFile`, source `Score`). So a piece opened from notes heard in a recording can take the official score over it, and the comparison shows where the two differ in the same words. The interface says "Add a version" and "Other versions"; the engine and the project file keep the name `performance`. A PDF read as it is has no notes to compare with; adding a version to it first needs its notes recognised (0023), and the PDF itself stays as it is.

A performance is added to a piece in the "Notes" view (MusicXML or MIDI import, or a recognised scan). It comes from one of:

- a recording of the player's own file, transcribed on the scan server (0014; Pro, since it runs on a server). Only the player's own file: no links, no YouTube (decided 2026-10-31);
- a project already transcribed, so a recording heard once is not sent again;
- a MIDI file of a performance (a digital piano, a performer's MIDI): on the device and free.

Every source takes one of two roles. **The notes** (as written): MusicXML, notes recognised from a printed PDF, or a MIDI file written on the grid (an export from a notation program). **The performance** (as played): notes heard in a recording, or a MIDI file played in by hand, such as the free MIDI a cover performer often shares while the score is sold (Andrei, 2026-11-05). A MIDI file can be either; the app proposes the role from its timing (`PerformanceTiming`: quantised or played) and the player confirms it. The purchased PDF itself is never changed: it stays the "PDF" view with the player's marks, and the merge lives in the "Notes" view. A performance MIDI has no recording, so *Performer* in section 5 plays its notes through the app's own sound.

A piece may hold several performances (a concert and a studio take); one is shown at a time and a chip picks it, as with variations. A performance has a name the player gives ("Live at the National Gallery").

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

### 4. On the page: markers, and the comparison as a layer

Chosen by Andrei on 2026-11-05.

- **Markers above bars**, always shown when the piece has a performance: a small plum label above a bar that differs, with its kind and count ("+3 · filling", "bass: G instead of D"). Printed notes that were not heard are tinted `--warning` in place. Likely artefacts are hidden by default; a line in the layer's popover counts them and shows them.
- A tap on a marker selects the bar; the contextual menu offers **Compare**, **Listen to the performer**, **Take into the score** and **Not a difference** (which removes the marker and is kept).
- **The comparison layer**, "Other version above the staves" in the layers (`layers.version`, off until asked for; built 2026-11-05 as `PerformanceLayer`; the design kit's "Performance" layer is the transcription's playing marks, a different thing): small staves above every system with what the recording plays, bar for bar, for the whole piece, as the ossia of 0021. Only what is not printed is coloured, so the eye goes to the differences. While the layer is on, the bar-to-page map follows the taller systems. The same view exports to PDF (0017).

Plum and amber keep their meaning in the design system: the ossia is already plum ("not the original, an alternative"), and "printed but not heard" is a reason to check, like the Check flags, so it takes the amber text colour, never the amber fill of the player's own marks.

### 5. In phrase focus: hear, compare, take

Built first on 2026-11-05: without a variation in play, the card shows the shown version's bars of the phrase as the ossia, named "As in «name»", and the focus bar offers Take into the score for every bar of the phrase the version has otherwise and Restore these notes for the bars taken. The focus bar then also offers Notes / Version / Alternate: the phrase as in the score, as in the version (its notes in the score's time, artefacts left out), or one after the other; leaving focus goes back to the notes.

- The focused phrase shows the ossia "As <name> plays it" above the original, in the plum dashed frame of variations.
- **Notes · Performer · Alternate** chooses what is heard. *Performer* plays the recording itself from the bar, through the time map, not a synthesis of the notes heard; loop and 50–100 % tempo (pitch kept) work as for the score. *Alternate* plays the printed phrase, then the recording.
- **Take into the score**, per bar, for the whole phrase or a selection; a tick on a note of the ossia leaves it out. Taken notes replace the printed notes of those bars, written with the score's spelling and rhythm grid (section 6), and are labelled "from the performance". The printed notes are kept: **Restore the print** brings them back, bar by bar.

### 6. The score and the recording correct each other

- **The score helps the recording.** In aligned bars the performance's notes are written with the print's bars, meter, key and spelling, and with the print's lengths where a note matches; a printed low note the recording probably lost is shown as such instead of as a difference. This replaces the bars and tempo worked out from the playing alone (0016) wherever the alignment holds.
- **The recording helps the score**, for a recognised scan only, where the printed notes are themselves inferred. Two rules first: every note of a staff an octave away from the recording over two bars or more (a lost octave clef), and a bar whose length disagrees with its meter while the time map gives it the meter's length. Each becomes a Check flag from the performance (`CheckSource.Performance`) with its own reason, **Recording: other octave** (`RecordingOctave`, which can be fixed by moving the notes) or **Recording: other bar length** (`RecordingBarLength`, which says how long the recording plays the bar and leaves the fix to the player), with **Show**, **Fix from the recording** where it applies, and **Keep as printed**. Nothing changes without the player. On homr's reading of the first page of Für Elise against Transkun's hearing of a recording of the whole piece, the second rule picks out three of the seven bars the scan read too long (16, 21 and 27), each played in 3/8.
- **Every note says where it comes from**: in the print and the recording; only in the print; only in the recording; corrected from the print; corrected from the recording; taken from the performance. Provenance stays `Imported`, `Detected` or `UserEdited` (0004); the relation to the other source is a separate label on the comparison, so no import fact is overwritten.

### 7. Free and Pro

Following 0018's rule: transcribing a recording runs on the server and is Pro. Alignment, comparison, markers, the layer, focus, listening and taking run on the device and are free, so a MIDI performance or a recording already transcribed works without Pro.

### 8. What is kept

The project file gets a `performances` list (version 13; the format is written in `project-file.md` with the code): per performance its id and name, its notes (the engine's MIDI, as a transcription's source today, with its transcription origin), the anchors, the bars marked "not a difference", and the bars taken with their notes. Taken notes are edits of the piece with their source, so the score, analysis and playback read them like any correction.

The recording's sound is not in the project file: the app keeps it on the device (IndexedDB on the web, the app's files on iPad), never sends it to the API, and asks for the file again when it is missing. Listening to the performer needs it; everything else does not.

### 9. Not yet

- **The performance as the base**: the recording's notes as the main score, with bars, chords and spelling corrected from the print. After the score-based flow works.
- Aligning a performance with repeats played differently from the print (a repeat skipped, a da capo): the first version aligns the score as written out and marks what it cannot place as unaligned.

## Consequences

- Order of work: the engine in music-core (alignment, time map, comparison, kinds, the two recognition rules) with tests; the facade and project file version 13; the web UI after the redesign's PDF and Notes views (thread "Build the redesigned app UI"); the iPad screens with the rest of the iPad redesign.
- Tests use public-domain music only: a Mutopia score and its MIDI, and performances made from them by known changes (added notes, a changed bass, a dropped octave, rubato), so each kind and each rule has an expected answer. Andrei's pieces stay out of the repository.
- Bar markers and the comparison layer depend on the geometry module (0021) and the layers menu (0018, section 6); the "Performer" playback needs a second audio source in the playback engine (0007), driven by the time map.