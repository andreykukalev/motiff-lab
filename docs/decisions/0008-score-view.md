# 0008 · Score view

Status: proposed, 2026-10-22

## Context

S1-W7 moved from Could to Must on 2026-10-22: players want to read the piece as notation while they listen, not only as a piano roll. The view has to:

- show a MusicXML import as it is written;
- show a MIDI import, which has no notation, and later a motif's variation, which exists only as notes in the shared model (0004);
- follow playback: the notes being heard are marked and the view scrolls with them; clicking a note moves playback there;
- work offline like the rest of the PWA, in Chrome, Edge, Safari and Firefox, without slowing the first load for players who never open it.

Reading PDF or scanned scores (optical music recognition) is not part of this. It stays postponed until after Stage 3: its output is uncertain in the same way audio transcription is, and it needs the same detected-versus-confirmed handling. Meanwhile, MusicXML exported by an OMR tool such as Audiveris imports like any other MusicXML.

## Options considered

| Option | Licence | Size | Input | Verdict |
| --- | --- | --- | --- | --- |
| [Verovio](https://www.verovio.org) 6.3 (C++ compiled to WebAssembly) | LGPL-3.0-or-later | 7.3 MB as one ES module with the WebAssembly inside; 1.8 MB as served (Brotli) | MEI, MusicXML, Humdrum, ABC | **Chosen.** Engraves MusicXML directly and MEI that we write ourselves. Keeps our element ids in the SVG, and its timemap gives every note's position in quarter notes, so syncing to the canonical timeline needs no tempo arithmetic. Line breaking, beams, stems, accidentals and spacing are done for us |
| [OpenSheetMusicDisplay](https://opensheetmusicdisplay.org) 2.2 (TypeScript on VexFlow) | BSD-3-Clause | About 1.3 MB minified, 0.4 MB gzip | MusicXML only | Rejected. Smaller and permissively licensed, but it reads only MusicXML, its cursor steps through the score instead of answering "what sounds at this tick", and its note objects do not carry ids we choose, so highlighting motifs and variation differences by note id would need a second mapping |
| [VexFlow](https://www.vexflow.com) 5 alone | MIT | About 1 MB | A drawing API | Rejected for now. It draws staves, notes and beams but leaves measure layout, line breaks and collisions to us; that is an engraver to write and maintain |
| abcjs | MIT | About 0.5 MB | ABC | Rejected. ABC loses voices and ties that MusicXML piano scores rely on |

Licence check: Verovio is LGPL-3.0-or-later (`COPYING.LESSER` in its repository). We ship the unmodified npm build as separate files that the page loads at run time, so players can replace them, and we give the licence text, the version and a link to the source next to them (`tools/verovio/README.md`, `wwwroot/lib/verovio/`). That meets LGPL section 4 for a combined work without putting our own code under the LGPL.

## Decision

1. **Verovio engraves, in the TypeScript layer.** `Scripts/score/score-view.ts` loads `wwwroot/lib/verovio/` (Verovio 6.3.0 from npm, unmodified) with a dynamic import the first time a score is shown, renders every page to SVG at the width of the view, and lays out again when the width changes.
2. **Two sources, one view.**
   - *As written*: a MusicXML file goes to Verovio as it is, so spelling, beaming, voices and layout follow the source.
   - *Generated from the notes*: music-core writes MEI from the shared model (`MotiffLab.MusicCore.Notation.NotationGenerator`). This is how MIDI imports, and later variations and motifs, are shown, and it works the same for every input (one event model, one workspace).
   MusicXML imports open as written and can switch to generated; MIDI imports have only the generated view.
3. **Generated notation is an inference and says so.** The generator is pure and deterministic. It shows note values on a 32nd-note grid and counts every note whose onset or end moved to get there, spells pitches from the key signature (sharps in sharp keys and C, flats in flat keys), assigns overlapping notes to up to four voices per staff and counts any note it had to shorten, splits a single-track piano part without hands at middle C, splits a part with imported hands by hand, ties notes across bar lines, writes each piece as the longest value that starts on a multiple of its own length or of the beat (the dotted quarter in compound meters) without hiding the middle of a 4/4 or 12/8 bar, beams eighths and shorter within the beat (the dotted quarter in compound meters), and writes accidentals per bar. The view labels it "generated from the notes" with those counts. Tuplets, grace notes, pedal and dynamics were not generated at first; since 2026-10-31 triplets and sextuplets within a beat are written as tuplets, and notes heard in a recording get grace notes, pedal and dynamics (0016 items 8, 11 and 12).
4. **Note ids are the shared ids.** In generated MEI each note's `xml:id` is its shared-model id (`n_0057`); the pieces of a tied note after the first get `-2`, `-3`. Motif selection and variation differences can then mark notes in the score by the ids they already use.
5. **Sync through Verovio's timemap.** The timemap lists, for each moment, the notes that start and stop there, with the position in quarter notes (`qstamp`). The view turns that into tick intervals (`qstamp × 480`), so it lines up with the canonical timeline whatever the tempo map says. Every animation frame it asks the player for `currentTick()` (0007), marks the notes sounding at that tick, and scrolls the system they are in into view; after the player scrolls the score by hand, it waits 4 s before following again. A click on a note seeks to its onset. Repeats are not expanded, like the importers.
6. **Loaded on first use, cached for offline.** The Verovio files are not in the offline manifest that the service worker installs, so the first visit does not download them. The service worker caches them the first time the score view loads them, in a cache named after the Verovio version that app updates do not clear.

## Consequences

- The first score view on a device downloads about 1.8 MB (Brotli); later views, and offline use, read the cache.
- Engraving is the slow step for long pieces: the 5,000-note fixture takes about 5 s in headless Chromium, most of it Verovio's layout. If that matters, render the pages near the playhead first.
- Generated notation from performance MIDI looks rough until rhythm restoration (#27) aligns it; the approximated count makes that visible. Spelling ignores harmony, so a chromatic passage can be spelled in a way a musician would not choose; spelling from analysis can replace it later.
- Generated MEI is tested in music-core (bar fill, ties, voices, accidentals, beams, ids, determinism). The TypeScript timeline is tested in Node against the vendored Verovio build, which also checks that the vendored files load.
- Side-by-side scores in Compare (#43) reuse the generated view for the original motif and the variation.
- Updating Verovio means replacing the two files, the licence notice and the cache name together; `tools/verovio/README.md` lists the steps.
