# 0016 · Bars and a tempo for notes heard in a recording

Status: accepted, 2026-10-30. Andrei: Transkun hears the notes almost perfectly and its MIDI is clean, but the score still looks frightening; the problem is now the notation, not the recognition.

## Context

The transcription engine (0014, Transkun 2.0.1) writes what it hears as MIDI at 120 BPM in 4/4, with each note at the time it was played. The score view (0008) writes those notes in the file's bars on a 32nd grid. The bar lines therefore fall at arbitrary places in the music, every onset is a little off any beat, and a pianist's slightly early key releases turn into short notes followed by short rests. On Für Elise heard from a recording, the notes were right and the page was unreadable.

A prototype on that recording (904 notes, 130 s) showed that three steps make the generated score read almost like the printed one: find the beat, put bar lines where the bass falls, and write a note held to the next one when the gap is shorter than an eighth.

## Decision

1. **The bars and tempo are an inference about the notes, not a change to them.** `BarGrid` (music-core, `Import/Transcription`) finds a 16th note in microseconds and a grid origin that leave the onsets closest to a steady beat, relative to the beat's length: a coarse search over the first 16 seconds (16ths of 80 to 500 ms), a finer one over the first 32, then a least-squares line over ever longer stretches up to the whole piece, all in integer arithmetic. When the shortest notes found read as a quarter slower than 60 a minute, they are taken as eighths. Onsets that sit on average more than a sixth of a step from the grid follow no steady beat, and no bars are offered.
2. **Bar lines go where the bass falls.** Onsets whose lowest note is below middle C and the lowest within a dotted eighth either way are likely downbeats. For each bar length offered (2/4, 3/4, 4/4, 3/8, 6/8) the downbeat's place is judged over four bars around each bar line; when it moves for two windows running (a repeat, a held beat), that bar is cut short and the bars start afresh. The bars that hold the most likely downbeats, better than chance and less a cost per short bar, are chosen; within a tenth of the best, 4/4 is preferred, then the shorter bar (3/8 over 6/8 or 3/4, which the bass cannot tell apart), and 6/8 over 3/4 when the bass falls on the half bar more than on the second and third quarters. A first downbeat after the first note makes a pickup.
3. **Applying the grid moves no note.** `BarGrid.Apply` re-expresses every note's onset and end, through `QuarterFraction` and `CanonicalTime`, against the found beat, with one tempo of four 16ths a quarter, so playback sounds as the recording did. Ids, pitches, velocities, hands and attributions are kept. The meter is a `MeterMap` with the chosen signature, the pickup, and a change of the same signature at each place the bars start afresh. The import report drops the engine's 120 BPM and 4/4 notices and adds an Assumed notice (`transcription.bars-inferred`) that names the signature, the tempo, the pickup and the short bars. The tempo fact (S1-A5) says the tempo was worked out from the notes.
4. **The player sees and controls it.** After transcription the import screen shows the bars and tempo found, with the time signature to choose, twice or half the tempo, and a switch back to the engine's bars. The choice is stored as the grid itself in the project's `TranscriptionOrigin` (project file version 10: origin tick, 16th in microseconds, signature, bar starts), not worked out again on open, so a later change to the search never moves a saved project's notes or its corrections. The engine's MIDI stays the project's source.
5. **Held notes in the score.** For notes heard in a recording (MIDI whose notes are all detected), the score view writes a note that ends less than an eighth before the next onset on its staff up to that onset (`NotationGenerator`'s `holdShortGaps`). The notes keep their lengths; the score note under the view counts the notes shown held.

## Consequences

- The first transcription of a piece opens in readable bars without the player doing anything, and every inference is named where it is shown.
- A tempo that changes a lot (strong rubato, a ritardando) fits one beat less well: bars drift and more of them are cut short. A tempo map that follows the playing is the next step, and Restore rhythm (S1-R1–R7) can build on it.
- Triplets still land on the nearest 32nd (0008), and a transcription has no key signature, so pitches are spelled in C major.
- Projects saved by this version open in earlier versions only after updating (version 10), as with every format change.
