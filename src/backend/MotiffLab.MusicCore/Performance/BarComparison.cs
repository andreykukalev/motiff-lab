using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Performance;

/// <summary>What a bar's differences between the print and the performance amount to (decision record 0022, section 3). Inferred.</summary>
internal enum BarDifference
{
    /// <summary>The bar is played as printed, give or take one note.</summary>
    None,

    /// <summary>Notes are added: a filling figure, an inner voice, a pulsing note.</summary>
    AddedNotes,

    /// <summary>A printed bass note is replaced by another.</summary>
    DifferentBass,

    /// <summary>A quick stepwise run is played where the print has one note or an ornament sign.</summary>
    Ornament,

    /// <summary>Printed notes are left out.</summary>
    Simplified,

    /// <summary>The printed notes are played an octave away.</summary>
    Octave,

    /// <summary>The printed pitches are played at other times in the bar.</summary>
    Rhythm,

    /// <summary>Played differently in a way none of the others names.</summary>
    Other,
}

/// <summary>A printed note and the played note that matches it.</summary>
/// <param name="Offset">The played note's place in score time minus the printed note's onset, in ticks: late positive.</param>
internal sealed record NoteMatch(string PrintedId, string PlayedId, long Offset);

/// <summary>A played note placed in score time through the alignment.</summary>
internal sealed record PlacedNote(string Id, Pitch Pitch, Tick At, int Velocity);

/// <summary>
/// One bar of the comparison: which printed notes were heard, which were not, what was played that is
/// not printed, and what that amounts to.
/// </summary>
/// <param name="Bar">The bar number, as the meter map numbers it.</param>
/// <param name="Aligned">
/// Whether the alignment holds here: anchors within a bar on each side. An unaligned bar (a free
/// introduction, a cut) claims nothing, and every list is empty.
/// </param>
/// <param name="Confidence">
/// How far the comparison can be trusted here, in percent: the share of printed notes heard in this bar
/// and its neighbours, leaving out the ones the recording probably could not hear.
/// </param>
/// <param name="ProbablyNotHeard">Printed notes not heard that lie below <see cref="PerformanceComparison.ReliableLowestPitch"/>, which transcription often misses.</param>
/// <param name="LikelyArtefacts">Played notes not printed that are probably a hearing error: very quiet, or an octave, twelfth or double octave above a louder note struck with them.</param>
internal sealed record BarComparison(
    int Bar,
    bool Aligned,
    BarDifference Difference,
    int Confidence,
    IReadOnlyList<NoteMatch> Matched,
    IReadOnlyList<string> PrintedNotHeard,
    IReadOnlyList<string> ProbablyNotHeard,
    IReadOnlyList<PlacedNote> PlayedNotPrinted,
    IReadOnlyList<PlacedNote> LikelyArtefacts)
{
    /// <summary>The printed notes not heard that <see cref="Difference"/> explains: all for an octave or a rhythm, the bass replaced, the notes left out.</summary>
    public IReadOnlyList<string> ExplainedNotHeard { get; init; } = [];

    /// <summary>The played notes not printed that <see cref="Difference"/> explains: all for an octave or a rhythm, the bass played instead, the notes added.</summary>
    public IReadOnlyList<PlacedNote> ExplainedPlayed { get; init; } = [];

    /// <summary>The kind in a word or two, for the marker above the bar: «добавлены ноты», "different bass".</summary>
    public static string NameOf(BarDifference difference, Words words) => difference switch
    {
        BarDifference.None => words.T("as printed", "как в нотах"),
        BarDifference.AddedNotes => words.T("added notes", "добавлены ноты"),
        BarDifference.DifferentBass => words.T("different bass", "другой бас"),
        BarDifference.Ornament => words.T("ornament", "украшение"),
        BarDifference.Simplified => words.T("simplified", "упрощено"),
        BarDifference.Octave => words.T("octave", "октава"),
        BarDifference.Rhythm => words.T("rhythm", "ритм"),
        BarDifference.Other => words.T("played differently", "иначе"),
        _ => throw new ArgumentOutOfRangeException(nameof(difference), difference, "Unknown difference."),
    };

    /// <summary>
    /// The marker above the bar (0022, section 4): the kind, after the count of notes added or left out where
    /// that is the point: "+3 · added notes", «−2 · упрощено», "different bass". Empty for a bar played as printed.
    /// </summary>
    public string Marker(Words words) => Difference switch
    {
        BarDifference.None => "",
        BarDifference.AddedNotes or BarDifference.Ornament => $"+{PlayedNotPrinted.Count} · {NameOf(Difference, words)}",
        BarDifference.Simplified => $"−{PrintedNotHeard.Count} · {NameOf(Difference, words)}",
        _ => NameOf(Difference, words),
    };
}