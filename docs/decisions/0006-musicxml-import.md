# 0006 · MusicXML import

Status: proposed, 2026-10-22

## Context

S1-I1, S1-I3, S1-I4 and S1-I8 need a MusicXML importer next to the MIDI one (0005): parts as tracks, a grand staff split into hands, key, time signature and tempo changes, pickup bars, and the same kind of import report. MusicXML has no absolute time. Positions are built by summing note, `<forward>` and `<backup>` durations in `<divisions>` per quarter, which each part declares and can change at any `<attributes>` (0002). Ties, tuplet and grace-note flags, rest events and `.mxl` are issue #10; this record covers the timing frame they will build on.

## Options considered

| Option | Verdict |
| --- | --- |
| A MusicXML library (for example a port of music21's reader, or a schema-generated object model) | Rejected. None is small, maintained and .NET-native, and generated models are large in the WebAssembly download. We would still walk the elements ourselves to apply the policies below and count each anomaly |
| `System.Xml.Linq` in music-core, hand-written walk | **Chosen.** Part of the base library, no new package, about 900 lines with the policies. DTDs are ignored and never fetched |

## Decision

`MusicXmlImporter.Import(bytes)` in `MotiffLab.MusicCore.Import.MusicXml` returns `MusicXmlImport`: the shared `Notes` and `Meter` (0004), and source facts that keep their exact position (`MusicXmlNote.Onset` and `Duration` as `QuarterFraction`) or their measure next to canonical time: parts, measures, tempo changes, time signatures and per-part key signatures, plus an `ImportReport`.

Timing works in two passes:

1. **Per part,** a cursor moves through each measure in document order. Durations convert through the part's current `MusicXmlDivisions` to exact quarters. A `<chord/>` note takes the onset of the note before it and does not move the cursor. Attributes and directions are placed at the cursor. A measure lasts as long as its cursor reached.
2. **Across parts,** measures align by index; each lasts as long as its longest part, and an empty measure lasts a full bar of the time signature in force. Measure starts are summed exactly, and onsets and ends round once each in `CanonicalTime`.

Policies:

| Situation | Policy | Report |
| --- | --- | --- |
| Over 5 MB, not XML or not a score, broken XML, no parts or measures, a duration that is not a number | Fail with `MusicXmlImportException` and a plain reason (N2) | — |
| Compressed `.mxl`, `score-timewise`, decimal divisions or durations | Fail as unsupported for now (0002) | — |
| Part ids | Tracks follow `<part>` order; names come from the part list. A part missing from the list keeps its notes | Repaired |
| Two staves in a part | Staff 1 is the right hand, staff 2 the left, both `Imported` (S1-I4). Other parts have no hand; inference is #12 | — |
| `<transpose>` | Pitches are sounding pitches: written plus chromatic and octave change | — |
| Voices | `Note.Voice` is the `<voice>` number; the source location keeps it as written | — |
| Velocity | `dynamics` on the note, else the last `<sound dynamics>` in the part, as a percentage of forte (velocity 90); else 80 | Assumed, counted |
| Tempo | `<sound tempo>`; a metronome mark (beat unit, dots, first number of per-minute) when its direction has no sound tempo. The same tempo in several parts is one change; a different one at the same tick replaces it. None at the start: 120 BPM | Superseded: Dropped; default: Assumed |
| Time signatures | Merged across parts like tempo. Additive numerators and repeated pairs with one beat type are summed. Free time, mixed beat types and invalid values are left out. None at the start: 4/4 | Dropped, Assumed |
| Key signatures | Kept per part (parts can differ). Non-traditional keys are left out | Dropped |
| Pickup | When there are at least two measures and the first is shorter than a full bar, it is the pickup (bar 0) and bar 1 starts after it (0004) | — |
| A later measure shorter or longer than its time signature | The signature is restated at the next measure, so the meter map ends the bar there (0004) and every measure starts on a bar line. A longer measure becomes a full bar and a short one, so later bar numbers move on by one. A short last measure needs nothing | Assumed |
| `implicit="yes"` measures after the pickup | Numbered consecutively like any other bar for now; `MusicXmlMeasure` keeps the flag and the written number | — |
| Backup past the measure start | Position set to the start of the measure | Repaired |
| Zero-length note | Widened to one tick | Repaired |
| Septuplets and other non-dividing tuplets | Rounded at each boundary (AC-S1-03) | Rounded |
| Microtonal `<alter>` | Rounded to the nearest semitone | Assumed |
| Ties | Kept as separate notes until #10 merges them | Assumed, counted |
| Grace notes, cue notes, unpitched notes, invalid pitches | Left out; cue and unpitched notes still take time | Dropped |

Codes (`MusicXmlIssueCodes`) are stable keys; messages are plain English. The report's track is the part index.

## Consequences

- No new package in music-core. Unit tests use inline scores, and three self-made fixtures (`importer-*.musicxml` in `tests/MotiffLab.MusicCore.Tests/Import/MusicXml/Fixtures`, named so they sit beside the #7 corpus) cover a grand staff with a pickup and triplets, two parts with different divisions and a transposing instrument, and septuplets. None contain published music. Tests that pin what the #7 corpus imports as can follow once it lands.
- #10 adds tie merging, tuplet and grace flags, rest events and `.mxl` on top of the same cursor.
- Bars excluded from numbering, and score measure numbers in the UI, need a meter map extension (0004); the measure list already carries what it needs.
- Repeats and endings are not unfolded: notes follow the written order.