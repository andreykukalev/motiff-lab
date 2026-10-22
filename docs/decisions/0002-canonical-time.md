# 0002 · Canonical musical time

Status: accepted, 2026-10-22

## Context

Every Motiff Lab feature reads note timing: display, playback, motif selection, analysis, variations, explanations and export. Inputs arrive in different units:

- **Standard MIDI Files:** absolute ticks at a PPQ from the header division field, one value for the whole file. Some files use SMPTE frames instead.
- **MusicXML:** durations in `<divisions>` per quarter note. The value is declared per part and can change at any `<attributes>`. Positions are built by summing note, `<forward>` and `<backup>` durations.
- **Audio (Stage 3):** seconds, mapped to musical time through a beat-derived time map.

`docs/05-domain-architecture.md` fixes 480 ticks per quarter and stores `onset_s` with `pos` (bar, beat, tick). `docs/01-mvp-stage-1.md` stores `pos` with `duration_ticks`. The first code (`TickResolution.FromSource`) took one integer "source PPQ" for both MIDI and MusicXML, rounded through `decimal`, and had no rules for spans.

## Decision

1. **One canonical timeline.** Note timing is an absolute, non-negative tick count from the start of the piece at 480 ticks per quarter note (`CanonicalTime.TicksPerQuarter`). Bar and beat are derived from a meter map, and seconds from a tempo map (and, from Stage 3, the audio time map). They are views of the timeline, not stored truth.
2. **Strong types.** `Tick` is a canonical position. `TickSpan` is an onset plus a duration of at least one tick, with an exclusive end. Source units have their own types (`MidiTimeBase`, `MusicXmlDivisions`) and never convert implicitly.
3. **Exact intermediate time.** Source values convert exactly to `QuarterFraction`, a reduced 64-bit rational number of quarter notes. Importers accumulate positions in `QuarterFraction`, so a MusicXML divisions change or a non-dividing MIDI PPQ adds no error. No floating point or decimal is used for timing.
4. **Round once, at boundaries.** `CanonicalTime.ToTick` rounds a non-negative exact position to the nearest tick, halves up. `CanonicalTime.ToSpan` rounds onset and end independently and takes the difference, so error is at most half a tick per boundary and never accumulates. A span that rounds to zero length becomes one tick long.
5. **Report rounding.** Every conversion returns `IsExact`. Importers use it for the import report (S1-I8), in line with technical honesty.
6. **Invalid input fails loudly.** Negative positions or durations, zero or negative resolutions, a span whose end is not after its onset, and SMPTE-timed MIDI throw `ArgumentOutOfRangeException` or `NotSupportedException`. Results that do not fit in 64 bits throw `OverflowException`. Nothing wraps or clamps silently.

## Why 480

480 = 2⁵·3·5. Every note value down to a 128th, and triplet and quintuplet subdivisions of those, are whole tick counts. It matches the domain doc and common MIDI practice. Septuplets and other rare tuplets round by at most half a tick per boundary, and the import report marks them. If real files show that matters, raising the resolution is a one-constant change, as long as it happens before projects are stored.

## Consequences

- MusicXML decimal divisions and durations are allowed by the format but rare. They are reported as unsupported for now.
- Widening a sub-tick span to one tick can make it overlap the next event by one tick. That is preferred over dropping a note.
- The next increment adds the meter map (bar, beat and pickup bars) and the note event. That will reconcile the `pos` and `duration_ticks` fields in 01 with the event shape in 05.
