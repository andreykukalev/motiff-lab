# 0004 · Note event and meter map

Status: accepted, 2026-10-22

## Context

Two documents describe the note event differently:

- `docs/05-domain-architecture.md` (shared model, written for audio): `onset_s`, `offset_s`, `sounding_offset_s`, `pos` (bar, beat, tick), `hand` and `voice` as plain strings, one `provenance` (detected, inferred, user_confirmed, user_edited) and one `confidence` for the whole event.
- `docs/01-mvp-stage-1.md` (Stage 1, notation): `onset_tick` and `duration_ticks`, `hand` as an object with its own provenance, `voice` as a role ("melody"), a new provenance value `imported`, and a `source` object with the file hash and a format-specific location.

Decision record 0002 made absolute canonical ticks the stored truth and left bar and beat to a meter map. Stage 1 also needs pickup bars (S1-I3, AC-S1-02) and must handle MIDI files that change meter part-way through a bar.

## Decision: note event

1. **Timing is a `TickSpan`.** One onset and a duration of at least one tick (0002). Seconds come from the tempo map, and bar and beat from the meter map; neither is stored. This takes 01's `onset_tick` and `duration_ticks` and drops 05's `pos` and `onset_s` as stored fields. Sustain-pedal sounding length (05's `sounding_offset_s`) is not modelled until pedal is.
2. **Fields:** `Id`, `Pitch` (MIDI 0–127, no spelling), `Span`, `Velocity` (1–127), `Track` (zero-based lane: a MIDI track or a MusicXML part), `Voice` (the notated voice number, null for MIDI), `Hand`, `Attribution`, `Source`.
3. **Provenance values:** `Imported`, `Detected`, `Inferred`, `UserConfirmed`, `UserEdited`: 05's four plus 01's `imported`.
4. **Uncertain is a confidence, not a provenance.** Every attribution carries a confidence from 0 to 1, and the display shows anything below its threshold as uncertain. Imported, user-confirmed and user-edited values always have confidence 1; the type enforces it.
5. **Per-field attribution where a field can be inferred separately.** The note's `Attribution` covers its existence, pitch, timing and velocity. `Hand` is an `Attributed<Hand>` with its own attribution, as in 01: a grand-staff hand is imported, a single-track MIDI hand is inferred (S1-I4), and a reassigned hand is user-edited. An inferred hand never makes an imported note look uncertain.
6. **Voice number and voice role are separate.** `Voice` is the notated voice from the source. The role (melody, inner, bass) in 01 and 05 is an analysis result; it will be added as an attributed annotation when that analysis exists, not before.
7. **Source location per note, file identity per project.** `MidiSourceLocation` (track chunk, channel, index of the note-on event) or `MusicXmlSourceLocation` (part id, measure position, note index, staff, voice as written). The file hash and format belong to the project's source file (01, SourceFile), not to every note. Notes the player adds have no source.

## Decision: meter map

1. **Beat = denominator note value.** 6/8 has six eighth-note beats. This matches MIDI and MusicXML and gives positions like "bar 5, beat 4". Grouping into a felt pulse (two dotted quarters) is a rhythm-analysis fact, added with that analysis.
2. **Denominators are powers of two up to 128**, so beat and bar lengths are whole ticks at 480 per quarter. Numerators run from 1 to 255; additive numerators (3+2/8) are given as their sum.
3. **Pickup = bar 0, aligned to the end of a full bar.** A one-beat pickup in 4/4 is on beat 4, and bar 1 starts after it. Without a pickup the first bar is bar 1. The meter cannot change inside the pickup.
4. **A change ends the bar in progress.** A time signature change that falls mid-bar cuts that bar short; the bar keeps its number and is reported as incomplete. No note moves, and the importer reports the odd placement. The last signature continues without end.
5. **Strict input.** The first change is at tick 0, changes strictly increase, and the pickup is shorter than a full bar; anything else throws. Merging duplicate MIDI time signature events is the importer's job.
6. **Positions are views.** `MetricPosition` (bar, beat, ticks after the beat) is computed from a tick and converts back exactly; positions outside their bar (beat 4 of a three-beat bar) throw.

## Consequences

- Bars are numbered consecutively. MusicXML measures that are excluded from numbering (`implicit="yes"`, for example split bars at repeats) and irregular bars that MusicXML states directly will need an extension when the MusicXML importer lands.
- MusicXML files without velocities need a default velocity from the importer; it should say so in the import report.
- Grace notes, ties, tuplet flags, rests and pedal are not on the note yet; they arrive with the importers that produce them.
- The JSON examples in 01 and 05 describe the persisted shape loosely; this record is the reference for the in-memory model.
