using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>How far the engine fills one part of Piece DNA.</summary>
public enum DnaStatus
{
    /// <summary>Not analysed yet: the screen shows the part's empty state.</summary>
    NotYet,

    /// <summary>Filled from what the engine already measures; later analyses add to it.</summary>
    Partial,

    /// <summary>Filled as the redesign describes it.</summary>
    Ready,
}

/// <summary>How an idea is heard again (0018 section 11: filled for exact, hollow for varied, a triangle for inverted).</summary>
public enum OccurrenceKind
{
    /// <summary>The place the idea was taken from.</summary>
    Original,

    /// <summary>The same pitches and rhythm.</summary>
    Exact,

    /// <summary>The same rhythm and intervals, starting on another note.</summary>
    Transposed,

    /// <summary>Changed in some notes or rhythm but still the idea. Not detected yet.</summary>
    Varied,

    /// <summary>The intervals turned upside down. Not detected yet.</summary>
    Inverted,
}

/// <summary>A section of the form strip (A, A′, B, Coda), with the sections it shares material with.</summary>
public sealed record DnaSection(string Id, string Label, TickSpan Span, int FirstBar, int LastBar, IReadOnlyList<string> SharesWith);

/// <summary>One place where an idea is heard.</summary>
/// <param name="Transposition">Semitones from the idea, 0 for the original and exact repeats.</param>
public sealed record IdeaOccurrence(OccurrenceKind Kind, TickSpan Span, int FirstBar, int LastBar, int Transposition, IReadOnlyList<string> NoteIds);

/// <summary>An idea that makes the piece: its lane of occurrences and its card.</summary>
/// <param name="Id">Stable within one answer: <c>idea_1</c>, <c>idea_2</c>, …</param>
/// <param name="FromSelection">True when the idea is the player's selection rather than one the engine found.</param>
/// <param name="Title">The card's title in the piece's language.</param>
/// <param name="Summary">The card's one line, stating only what the occurrences show.</param>
public sealed record DnaIdea(string Id, bool FromSelection, TickSpan Span, int FirstBar, int LastBar, IReadOnlyList<string> NoteIds, IReadOnlyList<IdeaOccurrence> Occurrences, string Title, string Summary);

/// <summary>A stretch of the key lane.</summary>
/// <param name="ModeInferred">The file gives only sharps or flats, so major or minor was chosen from the notes.</param>
public sealed record DnaKey(MusicalKey Key, TickSpan Span, int FirstBar, int LastBar, bool ModeInferred);

/// <summary>
/// Piece DNA (0018 section 11, preparation item 18): the map of the whole composition behind the third place in the
/// app, as lanes the screen draws: the form strip, one lane per defining idea, the key lane and the bars that bring new
/// material. Each lane says how far the engine fills it, so the screen shows an empty state for a part not analysed yet
/// and new analyses only fill the contract. Today: no form; the player's selection as the one idea with its exact and
/// transposed repeats (<see cref="StructureAnalyzer"/>); the key signatures of the file; new material by bar.
/// </summary>
/// <param name="Form">The sections of the form strip; empty until the form of the whole piece is analysed.</param>
/// <param name="Ideas">Three to six motifs once they are found; until then the selection, or none.</param>
/// <param name="Keys">The key lane, in time order, covering the piece.</param>
/// <param name="NewMaterialBars">Bars whose line repeats no earlier bar, exactly or transposed.</param>
public sealed record PieceDna(
    int FirstBar,
    int LastBar,
    DnaStatus FormStatus,
    IReadOnlyList<DnaSection> Form,
    DnaStatus IdeasStatus,
    IReadOnlyList<DnaIdea> Ideas,
    DnaStatus KeysStatus,
    IReadOnlyList<DnaKey> Keys,
    DnaStatus NewMaterialStatus,
    IReadOnlyList<int> NewMaterialBars)
{
    /// <summary>The whole map: ready when every lane is, not yet when none has anything.</summary>
    public DnaStatus Status =>
        new[] { FormStatus, IdeasStatus, KeysStatus, NewMaterialStatus } switch
        {
            var all when all.All(status => status == DnaStatus.Ready) => DnaStatus.Ready,
            var all when all.All(status => status == DnaStatus.NotYet) => DnaStatus.NotYet,
            _ => DnaStatus.Partial,
        };

    /// <summary>
    /// The map from what the engine measures today. Pure: the same notes, meter, keys, selection and analysis give the
    /// same map.
    /// </summary>
    /// <param name="selection">The player's selection, which becomes the one idea; null for none.</param>
    /// <param name="structure">The structure analysis of that selection; null when there is no selection.</param>
    public static PieceDna Build(IReadOnlyList<Note> notes, MeterMap meter, IReadOnlyList<KeySignature> keys, Tick end, MotifSelection? selection, StructureAnalysis? structure, Words words = default)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(keys);
        if (notes.Count == 0)
        {
            return new PieceDna(1, 1, DnaStatus.NotYet, [], DnaStatus.NotYet, [], DnaStatus.NotYet, [], DnaStatus.NotYet, []);
        }

        Tick first = notes.Min(note => note.Span.Onset);
        Tick last = Later(end, notes.Max(note => note.Span.End));
        int firstBar = meter.BarAt(first).Number;
        int lastBar = meter.BarAt(new Tick(Math.Max(first.Value, last.Value - 1))).Number;

        IReadOnlyList<DnaIdea> ideas = selection is { NoteIds.Count: > 0 } && structure is not null ? [Idea(notes, meter, selection, structure, words)] : [];
        IReadOnlyList<DnaKey> keyLane = KeyLane(notes, meter, keys, first, last);
        IReadOnlyList<int> newBars = StructureAnalyzer.NewMaterialBars(notes, meter);

        return new PieceDna(
            firstBar,
            lastBar,
            DnaStatus.NotYet,
            [],
            ideas.Count > 0 ? DnaStatus.Partial : DnaStatus.NotYet,
            ideas,
            keyLane.Count > 0 ? DnaStatus.Partial : DnaStatus.NotYet,
            keyLane,
            DnaStatus.Ready,
            newBars);
    }

    private static DnaIdea Idea(IReadOnlyList<Note> notes, MeterMap meter, MotifSelection selection, StructureAnalysis structure, Words w)
    {
        var chosen = selection.NoteIds.ToHashSet(StringComparer.Ordinal);
        Note[] motif = [.. notes.Where(note => chosen.Contains(note.Id)).OrderBy(note => note.Span.Onset).ThenBy(note => note.Id, StringComparer.Ordinal)];
        TickSpan span = TickSpan.FromBounds(motif.Min(note => note.Span.Onset), motif.Max(note => note.Span.End));
        int firstBar = meter.BarAt(span.Onset).Number;
        int lastBar = meter.BarAt(motif.Max(note => note.Span.Onset)).Number;

        var occurrences = new List<IdeaOccurrence> { new(OccurrenceKind.Original, span, firstBar, lastBar, 0, [.. motif.Select(note => note.Id)]) };
        occurrences.AddRange(structure.Recurrences.Select(recurrence => new IdeaOccurrence(
            recurrence.IsExact ? OccurrenceKind.Exact : OccurrenceKind.Transposed,
            recurrence.Span,
            recurrence.FirstBar,
            recurrence.LastBar,
            recurrence.Transposition,
            recurrence.NoteIds)));
        occurrences.Sort((a, b) => a.Span.Onset.CompareTo(b.Span.Onset));

        string bars = firstBar == lastBar
            ? w.T($"bar {firstBar}", $"такт {firstBar}")
            : w.T($"bars {firstBar}–{lastBar}", $"такты {firstBar}–{lastBar}");
        string title = w.T($"Your selection, {bars}", $"Ваше выделение, {bars}");

        int exact = structure.Recurrences.Count(recurrence => recurrence.IsExact);
        int transposed = structure.Recurrences.Count - exact;
        string summary = structure.Recurrences.Count == 0
            ? w.T("Not heard again with the same rhythm.", "Больше не звучит с тем же ритмом.")
            : w.T("Heard again ", "Звучит ещё ") + w.Times(structure.Recurrences.Count) + ": "
                + w.Joined([.. new[]
                {
                    exact > 0 ? w.Count(exact, "exact", "exact", "точно", "точно", "точно") : null,
                    transposed > 0 ? w.Count(transposed, "transposed", "transposed", "в другой высоте", "в другой высоте", "в другой высоте") : null,
                }.OfType<string>()]) + ".";

        return new DnaIdea("idea_1", true, span, firstBar, lastBar, [.. motif.Select(note => note.Id)], occurrences, title, summary);
    }

    /// <summary>
    /// The key signatures of the file as stretches of the piece. A signature without a mode is read as the major or
    /// minor key that fits its notes better (<see cref="KeyFinder"/>), and says so.
    /// </summary>
    private static List<DnaKey> KeyLane(IReadOnlyList<Note> notes, MeterMap meter, IReadOnlyList<KeySignature> keys, Tick first, Tick last)
    {
        KeySignature[] signatures = [.. keys.OrderBy(key => key.At).GroupBy(key => key.At).Select(group => group.Last())];
        if (signatures.Length == 0)
        {
            return [];
        }

        var lane = new List<DnaKey>();
        for (int i = 0; i < signatures.Length; i++)
        {
            Tick start = i == 0 ? first : Later(first, signatures[i].At);
            Tick stop = i + 1 < signatures.Length ? Earlier(last, signatures[i + 1].At) : last;
            if (stop <= start)
            {
                continue;
            }

            var span = TickSpan.FromBounds(start, stop);
            KeySignature signature = signatures[i];
            bool inferred = signature.Mode is null;
            KeyMode mode = signature.Mode ?? ModeFromNotes(notes, span, signature.Fifths);
            var key = new MusicalKey(signature.Fifths, mode);
            if (lane.Count > 0 && lane[^1].Key == key && lane[^1].ModeInferred == inferred)
            {
                DnaKey previous = lane[^1];
                lane[^1] = previous with { Span = TickSpan.FromBounds(previous.Span.Onset, stop), LastBar = meter.BarAt(new Tick(stop.Value - 1)).Number };
                continue;
            }

            lane.Add(new DnaKey(key, span, meter.BarAt(start).Number, meter.BarAt(new Tick(stop.Value - 1)).Number, inferred));
        }

        return lane;
    }

    private static KeyMode ModeFromNotes(IReadOnlyList<Note> notes, TickSpan span, int fifths)
    {
        double[] weights = new double[12];
        foreach (Note note in notes.Where(note => note.Span.Onset >= span.Onset && note.Span.Onset < span.End))
        {
            weights[note.Pitch.MidiNumber % 12] += note.Span.Duration;
        }

        double major = KeyFinder.Score(weights, new MusicalKey(fifths, KeyMode.Major));
        double minor = KeyFinder.Score(weights, new MusicalKey(fifths, KeyMode.Minor));
        return minor > major ? KeyMode.Minor : KeyMode.Major;
    }

    private static Tick Later(Tick a, Tick b) => a >= b ? a : b;

    private static Tick Earlier(Tick a, Tick b) => a <= b ? a : b;
}