using MotiffLab.MusicCore.Events;

namespace MotiffLab.MusicCore.Notation;

/// <summary>
/// One staff over the whole piece: its number, track, clef and hand, its notes, voices and triplet beats, the bars written
/// as printed, and the piece numbers its notes' ids take.
/// </summary>
internal sealed record StaffPlan(int Number, int Track, Clef Clef, Hand? Hand, bool IsSplitInferred, ShownNote[] Notes)
{
    private readonly Dictionary<string, int> _pieces = new(StringComparer.Ordinal);

    public List<List<Chord>> Voices { get; set; } = [];

    /// <summary>The staff's beats written as triplets, in order.</summary>
    public TupletSpan[] Tuplets { get; init; } = [];

    /// <summary>The bars written as the score prints them («Как напечатано», <see cref="PrintedBars"/>), by bar index.</summary>
    public Dictionary<int, PrintedCell> Printed { get; set; } = [];

    /// <summary>The ids of the notes written as printed in every bar written that they sound in.</summary>
    public HashSet<string> AsPrinted { get; set; } = [];

    /// <summary>
    /// Takes the piece number of the next element that writes <paramref name="notes"/>, in the order the bars are laid out:
    /// 0 for a note's first piece (its id as it is), then 1, 2, … (its id with "-2", "-3", …), so no two elements share an id
    /// whichever way each bar is written.
    /// </summary>
    public int TakePiece(IEnumerable<ShownNote> notes)
    {
        string[] ids = [.. notes.Select(note => note.Note.Id)];
        int piece = ids.Max(id => _pieces.GetValueOrDefault(id));
        foreach (string id in ids)
        {
            _pieces[id] = piece + 1;
        }

        return piece;
    }
}