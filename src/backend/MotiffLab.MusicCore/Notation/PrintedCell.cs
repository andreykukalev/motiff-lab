using MotiffLab.MusicCore.Pieces;

namespace MotiffLab.MusicCore.Notation;

/// <summary>A staff in a bar written as the score prints it (<see cref="PrintedBars"/>): its voices in the score's order.</summary>
/// <param name="AsPrinted">The ids of the notes written as printed here, not by the generator's rules.</param>
internal sealed record PrintedCell(PrintedVoice[] Voices, HashSet<string> AsPrinted);

/// <summary>
/// One voice of a printed bar: the score's own notes, rests and spaces with its beams, or, for a voice with a note the
/// player corrected, added or removed, its chords for the generator to write (<see cref="StaffLayout.Layer"/>).
/// </summary>
/// <param name="Beams">The first and last item under each of the score's beams; null when the score writes no beams here.</param>
internal sealed record PrintedVoice(PrintedItem[] Items, (int First, int Last)[]? Beams, Chord[]? Generated);

internal enum PrintedKind
{
    /// <summary>Notes written together, with their written value.</summary>
    Chord,

    /// <summary>A rest with its written value.</summary>
    Rest,

    /// <summary>A rest for the whole bar.</summary>
    MeasureRest,

    /// <summary>A rest whose values the generator works out: a whole-measure rest in a bar that is not full, a rest whose written value is not its length.</summary>
    FilledRest,

    /// <summary>Time the voice skips without a sign.</summary>
    Space,
}

/// <summary>An element of a printed voice, as the score writes it.</summary>
internal sealed record PrintedItem(PrintedKind Kind, long Start, long End)
{
    public int Dur { get; init; }

    public int Dots { get; init; }

    /// <summary>A chord's notes, lowest pitch first.</summary>
    public ShownNote[] Notes { get; init; } = [];

    public bool TieIn { get; init; }

    public bool TieOut { get; init; }

    /// <summary>How the score writes it: the tuplet, stem, beam and bracket.</summary>
    public MusicXmlWriting? Writing { get; init; }

    /// <summary>The tuplet it is written in, as the score groups it.</summary>
    public TupletSpan? Tuplet { get; init; }
}