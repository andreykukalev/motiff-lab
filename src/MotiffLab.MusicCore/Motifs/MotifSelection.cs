using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Motifs;

/// <summary>
/// A passage the player picked to explore: a beat-aligned range of 1–8 bars (S1-M1), optionally
/// limited to one hand or track (S1-M2), and the ids of the notes it holds.
/// </summary>
/// <remarks>
/// A note belongs to the selection when it starts inside <see cref="Span"/> and passes the filter;
/// a note held over from before the range does not. The motif model (#17) builds on this.
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

    /// <summary>The number of bars the selection touches, counting partly covered bars.</summary>
    public int BarCount => LastBar - FirstBar + 1;
}

/// <summary>Which notes of a range a selection keeps: all of them, one hand or one track (S1-M2).</summary>
public sealed record SelectionFilter
{
    private SelectionFilter(Hand? hand, int? track)
    {
        Hand = hand;
        Track = track;
    }

    public static SelectionFilter All { get; } = new(null, null);

    public Hand? Hand { get; }

    /// <summary>Zero-based track, as on <see cref="Note.Track"/>.</summary>
    public int? Track { get; }

    public static SelectionFilter ForHand(Hand hand) =>
        Enum.IsDefined(hand) ? new(hand, null) : throw new ArgumentOutOfRangeException(nameof(hand), hand, "Unknown hand.");

    public static SelectionFilter ForTrack(int track)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(track);
        return new(null, track);
    }

    /// <summary>A hand filter keeps notes with that hand, imported or inferred; notes with no hand are left out.</summary>
    public bool Matches(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        return (Hand is not { } hand || note.Hand?.Value == hand) && (Track is not { } track || note.Track == track);
    }
}