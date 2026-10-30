# 0005 · Standard MIDI File import

Status: proposed, 2026-10-22

## Context

S1-I1 to S1-I4 and S1-I8 need a Standard MIDI File importer: types 0 and 1 timed in ticks per quarter note, notes with velocity, the tempo map, time and key signatures, track and instrument names, channel 10 excluded with a notice, files up to 50 MB, and an import report that says what was rounded, guessed or left out. Timing must go through the exact model of 0002. music-core is pure and deterministic and runs in Blazor WebAssembly, where download size matters (0001).

## Options considered

| Option | Verdict |
| --- | --- |
| DryWetMidi (MIT, mature, widely used) | Rejected for import. It reads files well, but its value is in features Stage 1 does not need (devices, playback, rich editing tools), and it would add a dependency to the library that ships in the WebAssembly download. Its note detection and its reading policies for damaged files are configurable switches (ignore, fix or throw); turning their outcomes into per-kind counts for our report means re-walking the raw events anyway. Its time conversions do not use our exact `QuarterFraction` path, so we would still convert every tick ourselves |
| Hand-rolled reader in music-core | **Chosen.** The SMF chunk and event format is small and stable (about 250 lines to read it). We control every policy below, count each anomaly for the report, and keep music-core free of dependencies |

If a later stage needs MIDI devices or richer editing (Stage 2 MIDI input and output), DryWetMidi can still be used in the web or MAUI layer; this record does not rule that out.

## Decision

Two layers in `MotiffLab.MusicCore.Import.Midi`:

1. **`SmfReader` (internal)** reads the chunk and event structure: header, `MTrk` chunks, delta times, running status (cancelled by meta and sysex events, as the SMF specification says), meta and sysex events.
2. **`MidiImporter.Import(bytes)`** interprets the events into `MidiImport`: notes, tempo changes, time and key signatures, track info and an `ImportReport`. The source facts (`SourceNotes`, tempo, time and key signatures) keep their MIDI ticks and source position next to canonical time. From them the importer builds the shared model of 0004: `Notes` (ids `n_0001`, … in onset order, `Attribution.Imported`, `MidiSourceLocation(track, channel, note-on index)`) and `Meter`, a `MeterMap` from the deduplicated time signatures.

Policies:

| Situation | Policy | Report |
| --- | --- | --- |
| Type 2, SMPTE timing, RIFF (.rmi) wrapper, not MIDI, header damaged, no tracks, over 50 MB | Fail with `MidiImportException` and a plain reason (N2) | — |
| Timing | Ticks convert through `MidiTimeBase`; onset and end round separately in `CanonicalTime` | Rounded: notes, and tempo/meter/key positions |
| Note pairing | Per track, channel and key, first on first off. A note-on that comes just before the previous note's note-off at the same tick then pairs correctly instead of making a zero-length note | — |
| Same key struck again while sounding | Kept as two overlapping notes, paired in order | Assumed |
| Note-on with velocity 0 | Note-off | — |
| Note-off without a note-on | Left out | Dropped |
| Note-on without a note-off | Ends at the end of the piece, or one quarter later if it starts there | Assumed; the source note's `EndSource` is `Assumed`, and the shared note is `Inferred` with confidence 0.5, since its length is a guess |
| Note-on and note-off at the same tick | Widened to one canonical tick, as in 0002 | Repaired |
| Channel 10 | Notes left out (S1-I2) | Dropped |
| No tempo at tick 0 | 120 BPM until the first tempo change | Assumed; the change is marked `IsAssumed` |
| No time signature at tick 0 | 4/4 (S1-I3) | Assumed; marked `IsAssumed` |
| Two tempo, meter or key changes at the same canonical tick | The last one (file order, earlier tracks first) wins | Dropped |
| Tempo in a type 1 track other than the first | Applied to the whole piece | Assumed |
| Invalid tempo, time or key signature values | Left out | Dropped |
| Sustain pedal, other controllers, pitch bend, aftertouch, sysex, text and marker meta events | Left out in Stage 1; program changes are kept per track | Dropped, with sustain pedal counted on its own |
| Damaged track (bad byte, running status with no status, truncated chunk, overlong variable-length number) | Keep the events before the damage, close open notes, continue with the next track | Repaired |
| Missing end-of-track, data after it, unknown chunks, track count mismatch | Read what is there | Repaired or Dropped |
| Track and instrument names | First of each per track; UTF-8 if valid, otherwise Latin-1 | — |

The report groups issues by code and track with a count, so a 5,000-note file gives a short list. Codes (`MidiIssueCodes`) are stable keys; messages are plain English. The report carries no note content beyond counts, though it stays on the device anyway.

## Consequences

- No new package in music-core. The reader is covered by unit tests on hand-built byte streams and by three self-made fixture files (`tests/MotiffLab.MusicCore.Tests/Import/Midi/Fixtures`, generated by `make_fixtures.py`); none contain published music.
- The sustain pedal is dropped for now. Playback with pedal, and the domain model's `sounding_offset_s`, need it later; adding it is one more record type, since the reader already sees CC64.
- Markers and lyrics are dropped. They could later seed phrase or section boundaries (S1-I5), marked as imported.
- The report's measure count (S1-I8) comes from the meter map: bars from the start to the end of the piece, counting a final partial bar.
- Single-track hand inference (S1-I4) runs at the end of the import: when one track holds all the notes, its channels are pianos (programs 0–7, or none set) and two notes sound at once somewhere, `HandInference` assigns each note a hand by pitch and continuity, marked inferred with a confidence, and the report says so (`midi.hands-inferred`, Assumed). Multi-track files and single melodic lines get no hands. `HandInference.Reassign` records the player's change as user-edited (AC-S1-04).
- Performance detection (S1-I6) builds on `MidiImport` and is a separate increment.
