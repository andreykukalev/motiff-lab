using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Motifs;

/// <summary>
/// A passage the player picked to explore: a beat-aligned range of 1–8 bars (S1-M1), optionally
/// limited to one hand, track or voice (S1-M2), and the ids of the notes it holds; or 2–32 notes
/// picked one by one (S1-M3), with the beat-aligned range that holds them.
/// </summary>
/// <remarks>
/// For a range, a note belongs to the selection when it starts inside <see cref="Span"/> and passes
/// the filter; a note held over from before the range does not. A <see cref="Motif"/> builds on this.
/// Selecting is pure: the same notes, meter and range give the same selection.
/// </remarks>
/// <param name="Span">Where the selection sounds; both edges are on beats.</param>
/// <param name="FirstBar">The bar the selection starts in.</param>
/// <param name="LastBar">The bar the selection ends in.</param>
/// <param name="Filter">Which notes in the range count.</param>
/// <param name="NoteIds">The selected notes, ordered by onset, then pitch, then id.</param>
/// <param name="HandInferred">True when the filter is a hand and any selected note's hand is inferred, not imported.</param>
public sealed record MotifSelection(
    TickSpan Span,
    int FirstBar,
    int LastBar,
    SelectionFilter Filter,
    IReadOnlyList<string> NoteIds,
    bool HandInferred)
{
    public const int MinBars = 1;

    public const int MaxBars = 8;

    /// <summary>
    /// The most bars a structure the piece's maps know (a phrase or a sentence) is selected whole (decision
    /// record 0013): a range the player drags or clicks still holds at most <see cref="MaxBars"/>.
    /// </summary>
    public const int MaxStructureBars = 32;

    /// <summary>The fewest notes a selection of single notes holds (S1-M3).</summary>
    public const int MinNotes = 2;

    /// <summary>The most notes a selection of single notes holds (S1-M3).</summary>
    public const int MaxNotes = 32;

    /// <summary>The number of bars the selection touches, counting partly covered bars.</summary>
    public int BarCount => LastBar - FirstBar + 1;
}

/// <summary>Which notes of a range a selection keeps: all of them, one hand, one track or one voice of a track (S1-M2).</summary>
public sealed record SelectionFilter
{
    private SelectionFilter(Hand? hand, int? track, int? voice)
    {
        Hand = hand;
        Track = track;
        Voice = voice;
    }

    public static SelectionFilter All { get; } = new(null, null, null);

    public Hand? Hand { get; }

    /// <summary>Zero-based track, as on <see cref="Note.Track"/>.</summary>
    public int? Track { get; }

    /// <summary>The notated voice within <see cref="Track"/>, as on <see cref="Note.Voice"/>; set only together with a track.</summary>
    public int? Voice { get; }

    public static SelectionFilter ForHand(Hand hand) =>
        Enum.IsDefined(hand) ? new(hand, null, null) : throw new ArgumentOutOfRangeException(nameof(hand), hand, "Unknown hand.");

    public static SelectionFilter ForTrack(int track)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(track);
        return new(null, track, null);
    }

    /// <summary>One notated voice of a track, for example the melody voice of a MusicXML part. MIDI notes have no voice.</summary>
    public static SelectionFilter ForVoice(int track, int voice)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(track);
        ArgumentOutOfRangeException.ThrowIfLessThan(voice, 1);
        return new(null, track, voice);
    }

    /// <summary>A hand filter keeps notes with that hand, imported or inferred; notes with no hand are left out.</summary>
    public bool Matches(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        return (Hand is not { } hand || note.Hand?.Value == hand)
            && (Track is not { } track || note.Track == track)
            && (Voice is not { } voice || note.Voice == voice);
    }
}