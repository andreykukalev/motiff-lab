using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.MusicXml;

/// <summary>
/// Everything Stage 1 keeps from a partwise MusicXML score, on the canonical timeline, with the import report.
/// </summary>
/// <remarks>
/// <see cref="Notes"/> and <see cref="Meter"/> are the shared event model (decision record 0004).
/// The other lists are source facts: each keeps its exact position in quarter notes or its measure
/// next to the canonical time, and says whether a value was assumed.
/// </remarks>
/// <param name="Version">The <c>version</c> attribute of the score, for example "4.0", when given.</param>
/// <param name="Parts">One track per <c>&lt;part&gt;</c>, in file order.</param>
/// <param name="Notes">Shared-model notes in the same order as <paramref name="SourceNotes"/>, with ids n_0001, n_0002, ….</param>
/// <param name="SourceNotes">The sounding notes the shared notes were built from, with their exact source timing.</param>
/// <param name="GraceNotes">Grace notes, flagged and kept apart from <paramref name="Notes"/>: they take no notated time, so they are not played or analysed yet.</param>
/// <param name="Rests">Rests in every part and voice, in time order.</param>
/// <param name="Measures">The measures of the score, aligned across parts.</param>
/// <param name="KeySignatures">Key signatures per part: parts can differ, for example transposing instruments.</param>
/// <param name="End">The end of the piece: the end of the last measure, or of the last note if that is later.</param>
public sealed record MusicXmlImport(
    string? Version,
    IReadOnlyList<MusicXmlPartInfo> Parts,
    IReadOnlyList<Note> Notes,
    IReadOnlyList<MusicXmlNote> SourceNotes,
    IReadOnlyList<MusicXmlGraceNote> GraceNotes,
    IReadOnlyList<MusicXmlRest> Rests,
    IReadOnlyList<MusicXmlMeasure> Measures,
    IReadOnlyList<MusicXmlTempoChange> Tempos,
    IReadOnlyList<MusicXmlTimeSignature> TimeSignatures,
    MeterMap Meter,
    IReadOnlyList<MusicXmlKeySignature> KeySignatures,
    Tick End,
    ImportReport Report);

/// <param name="Index">Zero-based track index: the position of the <c>&lt;part&gt;</c> in the file.</param>
/// <param name="Id">The part's <c>id</c> attribute, for example "P1".</param>
/// <param name="Staves">The most staves the part declares; 2 for a piano grand staff.</param>
public sealed record MusicXmlPartInfo(int Index, string Id, string? Name, string? InstrumentName, int Staves, int NoteCount)
{
    /// <summary>True when the part is a grand staff, so its notes carry imported hands (S1-I4).</summary>
    public bool HasHands => Staves == 2;
}

/// <summary>A sounding note from a <c>&lt;note&gt;</c> element, or from a chain of notes tied to it.</summary>
/// <param name="MeasureIndex">The measure of the first written note of a tied chain.</param>
/// <param name="NoteIndex">The first written note's position among the notes, rests and grace notes of its measure.</param>
/// <param name="Pitch">Sounding MIDI pitch, after any <c>&lt;transpose&gt;</c> of the part.</param>
/// <param name="Velocity">Playback velocity from <c>dynamics</c>, or the default when <paramref name="IsVelocityAssumed"/>.</param>
/// <param name="Onset">Exact position from the start of the piece, in quarter notes.</param>
/// <param name="Duration">Exact length in quarter notes, across all tied notes; zero only for a note the file gave no length.</param>
/// <param name="IsExact">True when both boundaries landed on canonical ticks without rounding or widening.</param>
/// <param name="WrittenNotes">How many written notes were tied into this one; 1 for a note without ties (S1-I2).</param>
/// <param name="Tuplet">The first written note's tuplet ratio, for example 3:2 for a triplet; null outside tuplets.</param>
public sealed record MusicXmlNote(
    int Track,
    string PartId,
    int MeasureIndex,
    int NoteIndex,
    int Staff,
    string Voice,
    int Pitch,
    int Velocity,
    bool IsVelocityAssumed,
    QuarterFraction Onset,
    QuarterFraction Duration,
    TickSpan Span,
    bool IsExact,
    int WrittenNotes,
    MusicXmlTuplet? Tuplet);

/// <summary>A tuplet ratio from <c>&lt;time-modification&gt;</c>: <paramref name="Actual"/> notes in the time of <paramref name="Normal"/>.</summary>
public sealed record MusicXmlTuplet(int Actual, int Normal);

/// <summary>A grace note: pitched, flagged, and taking no notated time (S1-I2).</summary>
/// <param name="Onset">Exact position in quarter notes: the position of the note it ornaments.</param>
/// <param name="At">The onset on the canonical timeline.</param>
/// <param name="IsSlashed">True for a slashed grace note (acciaccatura), from <c>slash="yes"</c>.</param>
public sealed record MusicXmlGraceNote(
    int Track,
    string PartId,
    int MeasureIndex,
    int NoteIndex,
    int Staff,
    string Voice,
    int Pitch,
    QuarterFraction Onset,
    Tick At,
    bool IsSlashed);

/// <summary>A rest from a <c>&lt;note&gt;</c> element with <c>&lt;rest/&gt;</c>.</summary>
/// <param name="IsMeasureRest">True for a whole-measure rest, from <c>measure="yes"</c>.</param>
public sealed record MusicXmlRest(
    int Track,
    string PartId,
    int MeasureIndex,
    int NoteIndex,
    int Staff,
    string Voice,
    QuarterFraction Onset,
    QuarterFraction Duration,
    TickSpan Span,
    bool IsMeasureRest);

/// <param name="Index">Zero-based position of the <c>&lt;measure&gt;</c> in each part.</param>
/// <param name="Number">The <c>number</c> label as written; it can repeat or be non-numeric.</param>
/// <param name="IsImplicit">True for <c>implicit="yes"</c>: a pickup or split bar left out of the score's numbering.</param>
/// <param name="Start">Exact start in quarter notes.</param>
/// <param name="Span">Canonical span: the longest content of the measure across parts.</param>
public sealed record MusicXmlMeasure(int Index, string? Number, bool IsImplicit, QuarterFraction Start, TickSpan Span);

/// <param name="QuarterNotesPerMinute">Tempo as quarter notes per minute, from <c>&lt;sound tempo&gt;</c> or a metronome mark.</param>
/// <param name="IsAssumed">True for the 120 BPM default when the score sets no tempo at its start.</param>
public sealed record MusicXmlTempoChange(Tick At, int MeasureIndex, decimal QuarterNotesPerMinute, bool IsAssumed)
{
    public const decimal DefaultQuarterNotesPerMinute = 120;

    public int MicrosecondsPerQuarter => (int)Math.Round(60_000_000m / QuarterNotesPerMinute, MidpointRounding.AwayFromZero);
}

/// <param name="Denominator">Note value of the beat as a power of two, 1–128.</param>
/// <param name="IsAssumed">True for the 4/4 default when the score has no time signature at its start.</param>
public sealed record MusicXmlTimeSignature(Tick At, int MeasureIndex, int Numerator, int Denominator, bool IsAssumed);

/// <param name="Fifths">Sharps (positive) or flats (negative).</param>
/// <param name="Mode">The <c>&lt;mode&gt;</c> as written, for example "major" or "minor"; null when not given.</param>
public sealed record MusicXmlKeySignature(Tick At, int Track, int MeasureIndex, int Fifths, string? Mode);