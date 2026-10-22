using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Midi;

/// <summary>
/// Everything Stage 1 keeps from a Standard MIDI File, on the canonical timeline, with the import report.
/// </summary>
/// <remarks>
/// These are source facts, not the shared event model: each record keeps its MIDI ticks and track
/// position next to the canonical time, and says whether a value was assumed. Mapping to the
/// shared note event and meter map happens on top of this result.
/// </remarks>
/// <param name="End">The end of the piece: the latest end-of-track position across tracks.</param>
public sealed record MidiImport(
    int Format,
    MidiTimeBase TimeBase,
    IReadOnlyList<MidiTrackInfo> Tracks,
    IReadOnlyList<MidiNote> Notes,
    IReadOnlyList<MidiTempoChange> Tempos,
    IReadOnlyList<MidiTimeSignature> TimeSignatures,
    IReadOnlyList<MidiKeySignature> KeySignatures,
    Tick End,
    ImportReport Report);

/// <param name="Index">Zero-based track index in file order.</param>
/// <param name="Channels">Channels whose notes were imported from this track, ascending.</param>
/// <param name="Programs">The first program change on each channel of this track, by channel.</param>
public sealed record MidiTrackInfo(
    int Index,
    string? Name,
    string? InstrumentName,
    IReadOnlyList<int> Channels,
    int NoteCount,
    IReadOnlyList<MidiChannelProgram> Programs);

/// <param name="Program">General MIDI program number, 0–127.</param>
public sealed record MidiChannelProgram(int Channel, int Program);

/// <summary>A paired note-on and note-off.</summary>
/// <param name="NoteOnIndex">Index of the note-on in its track's event list; with Track and Channel, the source reference.</param>
/// <param name="Channel">Zero-based MIDI channel (channel 10 is 9).</param>
/// <param name="NoteOffTick">Source tick of the note's end; assumed when <see cref="EndSource"/> says so.</param>
/// <param name="IsExact">True when both boundaries landed on canonical ticks without rounding.</param>
public sealed record MidiNote(
    int Track,
    int Channel,
    int NoteOnIndex,
    int Pitch,
    int Velocity,
    long NoteOnTick,
    long NoteOffTick,
    TickSpan Span,
    bool IsExact,
    MidiNoteEnd EndSource);

public enum MidiNoteEnd
{
    /// <summary>A note-off message.</summary>
    NoteOff,

    /// <summary>A note-on with velocity 0, the usual running-status way to end a note.</summary>
    NoteOnVelocityZero,

    /// <summary>No note-off was found; the end was assumed (see the import report).</summary>
    Assumed,

    /// <summary>Note-on and note-off at the same tick; widened to one canonical tick.</summary>
    Widened,
}

/// <param name="At">Canonical position, rounded from <see cref="MidiTick"/> when needed.</param>
/// <param name="IsAssumed">True for the 120 BPM default when the file sets no tempo at its start.</param>
public sealed record MidiTempoChange(Tick At, long MidiTick, int MicrosecondsPerQuarter, bool IsAssumed)
{
    public const int DefaultMicrosecondsPerQuarter = 500_000;
}

/// <param name="Denominator">Note value of the beat as a power of two, 1–128.</param>
/// <param name="IsAssumed">True for the 4/4 default when the file sets no time signature at its start (S1-I3).</param>
public sealed record MidiTimeSignature(Tick At, long MidiTick, int Numerator, int Denominator, bool IsAssumed);

/// <param name="Sharps">Sharps (positive) or flats (negative), −7 to 7.</param>
public sealed record MidiKeySignature(Tick At, long MidiTick, int Sharps, bool IsMinor);