# Transcription test fixtures

MIDI files written by the transcription engines (decision records 0014 and 0016), as the scan server returns them.

| File | What it is | Exercises |
| --- | --- | --- |
| `basic-pitch-melody.mid` | Basic Pitch 0.4.0's MIDI for a synthesised C E G C' G E C melody, half a second a note | Transcribed notes import and are marked detected |
| `transkun-fur-elise.mid` | Transkun 2.0.1's MIDI for Beethoven's Für Elise (public domain), synthesised with the FluidR3 GM soundfont from the Mutopia Project's public-domain MIDI file: 904 notes in 130 s, at the engine's 120 BPM and 4/4 | `BarGrid` finds 3/8, a quarter at 72 and the two-16th pickup |
