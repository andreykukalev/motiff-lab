using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// The recognised notes the player still has to check, and the doubt they carry into what is read from them (decision
/// record 0027, section 12; #352): a chord, a key or an Insights fact built on such notes is shown as uncertain and says
/// that it rests on recognised notes to check, and in which bars.
/// </summary>
/// <remarks>
/// <para>
/// A note is doubtful when it was recognised (<see cref="Provenance.Detected"/>: read from a scan or heard in a
/// recording) and starts in a bar with an open Check flag. Fixing the note makes it user-edited, and "Looks right"
/// resolves the bar's flags (for a scan its notes become user-confirmed); either way it is no longer doubtful, so the
/// doubt goes from everything that rested on it. Imported notes are never doubtful: a piece without recognised notes
/// reads exactly as before.
/// </para>
/// <para>
/// The doubt is laid over the analysers' results, which stay as they are: the chord finder, the key reading and the
/// facts read the same notes the same way. Pure and deterministic: the same notes, meter and open bars give the same
/// doubt.
/// </para>
/// </remarks>
public sealed class NoteDoubt
{
    private readonly Dictionary<string, (int Bar, Tick Onset)> _notes;
    private readonly Words _words;

    private NoteDoubt(Dictionary<string, (int Bar, Tick Onset)> notes, Words words)
    {
        _notes = notes;
        _words = words;
    }

    /// <summary>No doubtful notes: every result reads as the analysers give it.</summary>
    public static NoteDoubt None { get; } = new(new Dictionary<string, (int, Tick)>(StringComparer.Ordinal), Words.English);

    /// <summary>Whether no note is doubtful.</summary>
    public bool IsEmpty => _notes.Count == 0;

    /// <summary>The doubtful notes' ids, in ordinal order.</summary>
    public IReadOnlyList<string> NoteIds => [.. _notes.Keys.Order(StringComparer.Ordinal)];

    /// <summary>The bars that hold doubtful notes, in order.</summary>
    public IReadOnlyList<int> Bars => [.. _notes.Values.Select(static note => note.Bar).Distinct().Order()];

    /// <summary>
    /// The doubtful notes of a piece: the recognised ones that start in a bar with an open Check flag.
    /// </summary>
    /// <param name="notes">The piece's notes as the player has them now, corrections applied.</param>
    /// <param name="openBars">The bars with an open Check flag.</param>
    public static NoteDoubt Of(IReadOnlyList<Note> notes, MeterMap meter, IEnumerable<int> openBars, Words words = default)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(openBars);
        var bars = openBars.ToHashSet();
        var doubtful = new Dictionary<string, (int Bar, Tick Onset)>(StringComparer.Ordinal);
        if (bars.Count == 0)
        {
            return new NoteDoubt(doubtful, words);
        }

        foreach (Note note in notes)
        {
            if (note.Attribution.Provenance != Provenance.Detected)
            {
                continue;
            }

            int bar = meter.BarAt(note.Span.Onset).Number;
            if (bars.Contains(bar))
            {
                doubtful[note.Id] = (bar, note.Span.Onset);
            }
        }

        return new NoteDoubt(doubtful, words);
    }

    /// <summary>Whether a note is doubtful.</summary>
    public bool Contains(string noteId) => _notes.ContainsKey(noteId);

    /// <summary>The bars of the doubtful notes among <paramref name="noteIds"/>, in order; empty when none is doubtful.</summary>
    public IReadOnlyList<int> BarsOf(IEnumerable<string> noteIds)
    {
        ArgumentNullException.ThrowIfNull(noteIds);
        return [.. noteIds.Where(_notes.ContainsKey).Select(id => _notes[id].Bar).Distinct().Order()];
    }

    /// <summary>The bars of the doubtful notes that start in a span, in order.</summary>
    public IReadOnlyList<int> BarsIn(TickSpan span) =>
        [.. _notes.Values.Where(note => note.Onset >= span.Onset && note.Onset < span.End).Select(static note => note.Bar).Distinct().Order()];

    /// <summary>
    /// "Rests on recognised notes still to check in bar 5." for the bars given; null for none.
    /// </summary>
    public string? Sentence(IReadOnlyList<int> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);
        if (bars.Count == 0)
        {
            return null;
        }

        string list = _words.Joined([.. bars.Select(static bar => bar.ToString(System.Globalization.CultureInfo.InvariantCulture))]);
        return _words.T(
            $"Rests on recognised notes still to check in {(bars.Count == 1 ? "bar" : "bars")} {list}.",
            $"Опирается на распознанные ноты, которые ещё нужно проверить: {(bars.Count == 1 ? "такт" : "такты")} {list}.");
    }

    /// <summary>
    /// The fact with the doubt of the notes it rests on: the notes of its places, or the whole selection when it names
    /// none. A detected or inferred fact becomes uncertain; an interpretation stays a reading. Either way it says
    /// where the notes to check are. A fact on no doubtful note is returned as it is.
    /// </summary>
    public MotifFact On(MotifFact fact, IEnumerable<string> selection)
    {
        ArgumentNullException.ThrowIfNull(fact);
        ArgumentNullException.ThrowIfNull(selection);
        if (IsEmpty)
        {
            return fact;
        }

        string[] placed = [.. fact.Places.SelectMany(static place => place.NoteIds)];
        if (Sentence(BarsOf(placed.Length > 0 ? placed : selection)) is not { } doubt)
        {
            return fact;
        }

        return fact with
        {
            Certainty = fact.Certainty == FactCertainty.Interpretation ? FactCertainty.Interpretation : FactCertainty.Uncertain,
            Doubt = doubt,
        };
    }

    /// <summary>The facts with the doubt of the notes each rests on (<see cref="On(MotifFact, IEnumerable{string})"/>).</summary>
    public IReadOnlyList<MotifFact> On(IReadOnlyList<MotifFact> facts, MotifSelection selection)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(selection);
        return IsEmpty ? facts : [.. facts.Select(fact => On(fact, selection.NoteIds))];
    }

    /// <summary>
    /// The key reading with the doubt of the selection's notes: a key read from notes that include doubtful ones is
    /// uncertain. A key the file states does not rest on the notes and stays as it is.
    /// </summary>
    public KeyReading? On(KeyReading? key, MotifSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return key is { Certainty: FactCertainty.Inferred } inferred && BarsOf(selection.NoteIds).Count > 0
            ? inferred with { Certainty = FactCertainty.Uncertain }
            : key;
    }

    /// <summary>A motif's analysis with the doubt in its key and facts.</summary>
    public MotifAnalysis On(MotifAnalysis analysis, MotifSelection selection)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        return IsEmpty ? analysis : analysis with { Key = On(analysis.Key, selection), Facts = On(analysis.Facts, selection) };
    }

    /// <summary>The structure around a motif with the doubt in its facts.</summary>
    public StructureAnalysis On(StructureAnalysis structure, MotifSelection selection)
    {
        ArgumentNullException.ThrowIfNull(structure);
        return IsEmpty ? structure : structure with { Facts = On(structure.Facts, selection) };
    }

    /// <summary>The harmony of a selection with the doubt in every chord built on doubtful notes and in its facts.</summary>
    public HarmonyAnalysis On(HarmonyAnalysis harmony, MotifSelection selection)
    {
        ArgumentNullException.ThrowIfNull(harmony);
        if (IsEmpty)
        {
            return harmony;
        }

        return harmony with
        {
            Chords = [.. harmony.Chords.Select(chord => chord with { Doubt = Sentence(BarsOf(chord.NoteIds)) })],
            Facts = On(harmony.Facts, selection),
        };
    }

    /// <summary>
    /// Piece DNA with the doubt in its harmony lane and its keys: a bar's chord over doubtful notes is drawn as
    /// uncertain and says so; a key read from the notes of a stretch with doubtful notes says so too.
    /// </summary>
    public PieceDna On(PieceDna dna)
    {
        ArgumentNullException.ThrowIfNull(dna);
        if (IsEmpty)
        {
            return dna;
        }

        DnaChord[] harmony = [.. dna.Harmony.Select(chord => Sentence(BarsIn(chord.Span)) is { } doubt
            ? chord with { Clear = false, Doubt = doubt }
            : chord)];
        DnaKey[] keys = [.. dna.Keys.Select(key => key.Inferred && Sentence(BarsIn(key.Span)) is { } doubt ? key with { Doubt = doubt } : key)];
        DnaCircle[] circles = [.. dna.Circles.Select(circle => circle.Circle.Certainty == FactCertainty.Inferred && BarsIn(circle.Span).Count > 0
            ? circle with { Circle = circle.Circle with { Certainty = FactCertainty.Uncertain } }
            : circle)];
        return dna with { Harmony = harmony, Keys = keys, Circles = circles };
    }
}