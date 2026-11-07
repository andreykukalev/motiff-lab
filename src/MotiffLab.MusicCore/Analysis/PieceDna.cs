using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

using MotiffLab.MusicCore.Import.Transcription;

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
/// <param name="KeyInferred">The file gives no key at all (a recording heard, a MIDI file), so the whole key was chosen from the notes.</param>
public sealed record DnaKey(MusicalKey Key, TickSpan Span, int FirstBar, int LastBar, bool ModeInferred, bool KeyInferred = false)
{
    /// <summary>Some of the key comes from the notes rather than the file: shown as assumed.</summary>
    public bool Inferred => ModeInferred || KeyInferred;
}

/// <summary>The chord a bar of the harmony lane holds most of its length.</summary>
/// <param name="Symbol">Spelt in the key of the bar: "Am", "F♯", "G7/B".</param>
/// <param name="Clear">At least <see cref="HarmonyAnalyzer.ClearConfidence"/> sure; a less sure chord is drawn as uncertain.</param>
public sealed record DnaChord(int Bar, TickSpan Span, string Symbol, bool Clear)
{
    /// <summary>The chord's step of the bar's key as a Roman numeral: "i", "V7", "♭VI" (testing item 30).</summary>
    public string Degree { get; init; } = "";

    /// <summary>What the chord does in the bar's key: tonic, subdominant, dominant; none for a chord with a note outside it.</summary>
    public HarmonicFunction Function { get; init; }
}

/// <summary>A close of a phrase, a dot on the harmony lane: full on the tonic, half on the dominant, inferred.</summary>
public sealed record DnaCadence(int Bar, Tick At, CloseKind Kind)
{
    /// <summary>
    /// What the close is, in words, said when it is tapped (testing item 30): "Bar 16 · G. A half close: the phrase's melody
    /// stops on B, not on the tonic, and the music waits to go on. Then section A″."
    /// </summary>
    public string Explanation { get; init; } = "";
}

/// <summary>
/// The circle of fifths of one section of the form, or of one stretch of the key lane when the form is not read (testing
/// item 30): the section's key, its chords on the circle, and the key it comes from when that differs.
/// </summary>
/// <param name="Id">The form section's id, or <c>key_1</c>, <c>key_2</c>, … for a key stretch.</param>
/// <param name="Label">The section's letter ("A′"), or the key's name for a key stretch.</param>
/// <param name="From">The key of the stretch before when it is another: the circle draws the modulation from it.</param>
public sealed record DnaCircle(string Id, string Label, TickSpan Span, int FirstBar, int LastBar, KeyCircleView Circle, MusicalKey? From)
{
    /// <summary>Where <see cref="From"/> sits on the circle; null when the key does not change.</summary>
    public int? FromPosition => From is { } from ? KeyCircle.PositionOf(from) : null;
}

/// <summary>One bar of a lane measured as a number: notes per beat for density, the mean velocity for dynamics.</summary>
public sealed record DnaBarLevel(int Bar, double Value);

/// <summary>One bar of the register lane: its lowest and highest note, as MIDI numbers.</summary>
public sealed record DnaRange(int Bar, int Low, int High);

/// <summary>
/// Piece DNA (0018 section 11, preparation item 18): the map of the whole composition behind the third place in the
/// app, as lanes the screen draws: the form strip, one lane per defining idea, the key lane and the bars that bring new
/// material. Each lane says how far the engine fills it, so the screen shows an empty state for a part not analysed yet
/// and new analyses only fill the contract. Filled (testing item 22, read top to bottom in this order): the form from the
/// sentences (<see cref="FormAnalyzer"/>); the key lane; the harmony lane, a chord per bar with the closes as dots; density
/// in notes per beat; new material by bar; register and dynamics, folded; then the ideas, found from the openings of the
/// sentences and phrases (<see cref="IdeaFinder"/>), after the player's selection when there is one.
/// </summary>
/// <param name="Form">The sections of the form strip; empty when the piece's sentences are not given.</param>
/// <param name="Ideas">The selection, then up to six ideas found in the piece; none when no opening is heard again.</param>
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
    /// <summary>Where each bar of the map starts, in ticks, from the first bar to one past the last (its end), to place the lanes by bar.</summary>
    public IReadOnlyList<long> BarStarts { get; init; } = [];

    /// <summary>Whether the harmony lane is filled: partial when chords are read, not yet for a single line.</summary>
    public DnaStatus HarmonyStatus { get; init; }

    /// <summary>The harmony lane: a chord per bar that holds one, in bar order.</summary>
    public IReadOnlyList<DnaChord> Harmony { get; init; } = [];

    /// <summary>The full and half closes of the phrases, in time order.</summary>
    public IReadOnlyList<DnaCadence> Cadences { get; init; } = [];

    /// <summary>The circle of fifths of every section of the form, or every key stretch without a form; empty without chords.</summary>
    public IReadOnlyList<DnaCircle> Circles { get; init; } = [];

    /// <summary>
    /// The map in one line, read before any lane (testing item 30): "Form A A′ B A″ · A minor · 3 full closes, 2 half
    /// closes · busiest in bar 23". Empty without notes.
    /// </summary>
    public string Summary { get; init; } = "";

    /// <summary>Whether the density lane is filled: ready whenever there are notes.</summary>
    public DnaStatus DensityStatus { get; init; }

    /// <summary>Notes starting per beat, every bar of the piece.</summary>
    public IReadOnlyList<DnaBarLevel> Density { get; init; } = [];

    /// <summary>The register lane: the lowest and highest note of every bar with notes.</summary>
    public IReadOnlyList<DnaRange> Register { get; init; } = [];

    /// <summary>Whether the dynamics lane is filled: ready when the notes are played louder and softer, not yet when all are as loud.</summary>
    public DnaStatus DynamicsStatus { get; init; }

    /// <summary>The mean velocity (1–127) of the notes starting in every bar with notes; empty when all notes are as loud.</summary>
    public IReadOnlyList<DnaBarLevel> Dynamics { get; init; } = [];

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
    /// <param name="selection">The player's selection, which becomes the first idea; null for none.</param>
    /// <param name="structure">The structure analysis of that selection; null when there is no selection.</param>
    /// <param name="sentences">The piece's sentences, for the form, the closes and the ideas found; null leaves them out.</param>
    public static PieceDna Build(IReadOnlyList<Note> notes, MeterMap meter, IReadOnlyList<KeySignature> keys, Tick end, MotifSelection? selection, StructureAnalysis? structure, Words words = default, SentenceMap? sentences = null)
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

        IReadOnlyList<DnaKey> keyLane = KeyLane(notes, meter, keys, first, last);
        IReadOnlyList<int> newBars = StructureAnalyzer.NewMaterialBars(notes, meter);
        IReadOnlyList<DnaSection> form = sentences is null ? [] : FormAnalyzer.Sections(notes, meter, sentences);

        var ideas = new List<DnaIdea>();
        if (selection is { NoteIds.Count: > 0 } && structure is not null)
        {
            ideas.Add(Idea(notes, meter, selection, structure, words));
        }

        if (sentences is not null)
        {
            IReadOnlyList<IdeaOccurrence> taken = ideas.Count > 0 ? ideas[0].Occurrences : [];
            ideas.AddRange(IdeaFinder.Find(notes, meter, sentences, form, words, taken, firstId: ideas.Count + 1));
        }

        List<(DnaChord Chord, ChordSpan Span)> read = HarmonyLane(notes, meter, keyLane, firstBar, lastBar, first, last);
        IReadOnlyList<DnaChord> harmony = [.. read.Select(pair => pair.Chord)];
        IReadOnlyList<DnaBarLevel> dynamics = DynamicsLane(notes, meter);
        IReadOnlyList<DnaCadence> cadences = sentences is null ? [] : CadenceLane(sentences, meter, harmony, form, words);
        IReadOnlyList<DnaBarLevel> density = DensityLane(notes, meter, firstBar, lastBar);
        return new PieceDna(
            firstBar,
            lastBar,
            form.Count > 0 ? DnaStatus.Partial : DnaStatus.NotYet,
            form,
            ideas.Count > 0 ? DnaStatus.Partial : sentences is null ? DnaStatus.NotYet : DnaStatus.Ready,
            ideas,
            keyLane.Count > 0 ? DnaStatus.Partial : DnaStatus.NotYet,
            keyLane,
            DnaStatus.Ready,
            newBars)
        {
            BarStarts = [.. Enumerable.Range(firstBar, lastBar - firstBar + 1).Select(number => meter.GetBar(number).Span.Onset.Value), meter.GetBar(lastBar).Span.End.Value],
            HarmonyStatus = harmony.Count > 0 ? DnaStatus.Partial : DnaStatus.NotYet,
            Harmony = harmony,
            Cadences = cadences,
            Circles = CircleLane(read, form, keyLane, meter, words),
            Summary = SummaryOf(form, keyLane, cadences, density, words),
            DensityStatus = DnaStatus.Ready,
            Density = density,
            Register = RegisterLane(notes, meter),
            DynamicsStatus = dynamics.Count > 0 ? DnaStatus.Ready : DnaStatus.NotYet,
            Dynamics = dynamics,
        };
    }

    /// <summary>
    /// The chord each bar holds longest, from the chords of the whole piece (<see cref="HarmonyAnalyzer.FindChords"/>),
    /// read and spelt in the key of the bar's lane.
    /// </summary>
    private static List<(DnaChord Chord, ChordSpan Span)> HarmonyLane(IReadOnlyList<Note> notes, MeterMap meter, IReadOnlyList<DnaKey> keys, int firstBar, int lastBar, Tick first, Tick last)
    {
        if (last <= first)
        {
            return [];
        }

        MusicalKey? opening = keys.Count > 0 ? keys[0].Key : null;
        IReadOnlyList<ChordSpan> chords = HarmonyAnalyzer.FindChords(notes, meter, TickSpan.FromBounds(first, last), opening);
        var lane = new List<(DnaChord, ChordSpan)>();
        int next = 0;
        for (int number = firstBar; number <= lastBar; number++)
        {
            TickSpan bar = meter.GetBar(number).Span;
            while (next < chords.Count && chords[next].Span.End <= bar.Onset)
            {
                next++;
            }

            ChordSpan? best = null;
            long longest = 0;
            for (int i = next; i < chords.Count && chords[i].Span.Onset < bar.End; i++)
            {
                long overlap = Math.Min(chords[i].Span.End.Value, bar.End.Value) - Math.Max(chords[i].Span.Onset.Value, bar.Onset.Value);
                if (overlap > longest)
                {
                    (best, longest) = (chords[i], overlap);
                }
            }

            if (best is null)
            {
                continue;
            }

            MusicalKey spelling = keys.LastOrDefault(key => key.Span.Onset <= bar.Onset)?.Key ?? opening ?? new MusicalKey(0, KeyMode.Major);
            var chord = new DnaChord(number, best.Span, best.Symbol(spelling), best.Confidence >= HarmonyAnalyzer.ClearConfidence)
            {
                Degree = HarmonyAnalyzer.Numeral(best.Chord, spelling),
                Function = HarmonyAnalyzer.FunctionOf(best.Chord, spelling),
            };
            lane.Add((chord, best));
        }

        return lane;
    }

    /// <summary>The phrases' full and half closes, at the last notes that close them, each with what it is in words.</summary>
    private static List<DnaCadence> CadenceLane(SentenceMap sentences, MeterMap meter, IReadOnlyList<DnaChord> harmony, IReadOnlyList<DnaSection> form, Words w) =>
        [.. sentences.Closes
            .Where(close => close.Kind != CloseKind.None)
            .Select(close => (Close: close, At: close.Place?.Span.Onset ?? new Tick(Math.Max(close.Phrase.Span.Onset.Value, close.Phrase.Span.End.Value - 1))))
            .Select(close =>
            {
                int bar = meter.BarAt(close.At).Number;
                return new DnaCadence(bar, close.At, close.Close.Kind) { Explanation = Explain(close.Close, bar, harmony, form, w) };
            })
            .OrderBy(cadence => cadence.At)];

    /// <summary>
    /// "Bar 16 · G. A half close: the phrase's melody stops on B, not on the tonic, and the music waits to go on. Then
    /// section A″." Only what the close and the lanes state: the close is read from the melody, the chord from the lane.
    /// </summary>
    private static string Explain(PhraseClose close, int bar, IReadOnlyList<DnaChord> harmony, IReadOnlyList<DnaSection> form, Words w)
    {
        string? chord = harmony.FirstOrDefault(chord => chord.Bar == bar)?.Symbol;
        string place = w.T($"Bar {bar}", $"Такт {bar}") + (chord is null ? "." : $" · {chord}.");
        string top = close.TopName is { } name ? w.T($" on {name}", $" на {name}") : "";
        string tonic = close.TopName is { } note ? $" ({note})" : "";
        string what = close.Kind == CloseKind.Full
            ? w.T(
                $"A full close: the phrase's melody comes to the tonic{tonic}, and the music comes to rest.",
                $"Полный каданс: мелодия фразы приходит к тонике{tonic}, музыка приходит к устою.")
            : w.T(
                $"A half close: the phrase's melody stops{top}, not on the tonic, and the music waits to go on.",
                $"Половинный каданс: мелодия фразы останавливается{top}, не на тонике, музыка ждёт продолжения.");
        string then = form.FirstOrDefault(section => section.FirstBar == bar + 1) is { } next
            ? " " + w.T($"Then section {next.Label}.", $"Дальше раздел {next.Label}.")
            : "";
        return $"{place} {what}{then}";
    }

    /// <summary>
    /// The circle of every section of the form, in the key the key lane gives at its start, with the chords of its bars; a
    /// piece without a form gets one circle per key stretch. A section in another key than the one before says where it
    /// comes from.
    /// </summary>
    private static List<DnaCircle> CircleLane(List<(DnaChord Chord, ChordSpan Span)> harmony, IReadOnlyList<DnaSection> form, IReadOnlyList<DnaKey> keys, MeterMap meter, Words w)
    {
        if (harmony.Count == 0 || keys.Count == 0)
        {
            return [];
        }

        IEnumerable<(string Id, string Label, TickSpan Span, int FirstBar, int LastBar)> stretches = form.Count > 0
            ? form.Select(section => (section.Id, section.Label, section.Span, section.FirstBar, section.LastBar))
            : keys.Select((key, index) => ($"key_{index + 1}", key.Key.NameIn(w), key.Span, key.FirstBar, key.LastBar));
        var circles = new List<DnaCircle>();
        foreach ((string id, string label, TickSpan span, int firstBar, int lastBar) in stretches)
        {
            DnaKey key = keys.LastOrDefault(key => key.Span.Onset <= span.Onset) ?? keys[0];
            ChordSpan[] chords = [.. harmony.Where(pair => pair.Chord.Bar >= firstBar && pair.Chord.Bar <= lastBar).Select(pair => pair.Span)];
            MusicalKey? from = circles.Count > 0 && circles[^1].Circle.Key != key.Key ? circles[^1].Circle.Key : null;
            circles.Add(new DnaCircle(id, label, span, firstBar, lastBar, KeyCircle.ForKey(key.Key, key.Inferred, chords, meter, w), from));
        }

        return circles;
    }

    /// <summary>"Form A A′ B A″ · A minor · 3 full closes, 2 half closes · busiest in bar 23": only what the lanes hold.</summary>
    private static string SummaryOf(IReadOnlyList<DnaSection> form, IReadOnlyList<DnaKey> keys, IReadOnlyList<DnaCadence> cadences, IReadOnlyList<DnaBarLevel> density, Words w)
    {
        var parts = new List<string>();
        if (form.Count > 0)
        {
            parts.Add(w.T("Form ", "Форма ") + string.Join(" ", form.Select(section => section.Label)));
        }

        string[] names = [.. keys.Select(key => key.Key.NameIn(w)).Where((name, index) => index == 0 || name != keys[index - 1].Key.NameIn(w))];
        if (names.Length > 0)
        {
            parts.Add(string.Join(" → ", names));
        }

        int full = cadences.Count(cadence => cadence.Kind == CloseKind.Full);
        int half = cadences.Count(cadence => cadence.Kind == CloseKind.Half);
        string[] closes =
        [
            .. new[]
            {
                full > 0 ? w.Count(full, "full close", "full closes", "полный каданс", "полных каданса", "полных кадансов") : null,
                half > 0 ? w.Count(half, "half close", "half closes", "половинный каданс", "половинных каданса", "половинных кадансов") : null,
            }.OfType<string>(),
        ];
        if (closes.Length > 0)
        {
            parts.Add(string.Join(", ", closes));
        }

        if (density.Count > 0 && density.MaxBy(level => level.Value) is { Value: > 0 } busiest)
        {
            parts.Add(w.T($"busiest in bar {busiest.Bar}", $"плотнее всего такт {busiest.Bar}"));
        }

        return string.Join(" · ", parts);
    }

    /// <summary>Notes starting per beat in every bar: a pickup bar counts the beats it has.</summary>
    private static List<DnaBarLevel> DensityLane(IReadOnlyList<Note> notes, MeterMap meter, int firstBar, int lastBar)
    {
        Dictionary<int, int> counts = notes.GroupBy(note => meter.BarAt(note.Span.Onset).Number).ToDictionary(group => group.Key, group => group.Count());
        var lane = new List<DnaBarLevel>();
        for (int number = firstBar; number <= lastBar; number++)
        {
            Bar bar = meter.GetBar(number);
            double beats = (double)bar.Span.Duration / bar.Signature.BeatTicks;
            lane.Add(new DnaBarLevel(number, beats > 0 ? Math.Round(counts.GetValueOrDefault(number) / beats, 2) : 0));
        }

        return lane;
    }

    private static List<DnaRange> RegisterLane(IReadOnlyList<Note> notes, MeterMap meter) =>
        [.. notes
            .GroupBy(note => meter.BarAt(note.Span.Onset).Number)
            .OrderBy(group => group.Key)
            .Select(group => new DnaRange(group.Key, group.Min(note => note.Pitch.MidiNumber), group.Max(note => note.Pitch.MidiNumber)))];

    /// <summary>The mean velocity by bar, when the notes carry more than one; a score's notes all carry the same.</summary>
    private static List<DnaBarLevel> DynamicsLane(IReadOnlyList<Note> notes, MeterMap meter)
    {
        if (notes.Select(note => note.Velocity).Distinct().Take(2).Count() < 2)
        {
            return [];
        }

        return [.. notes
            .GroupBy(note => meter.BarAt(note.Span.Onset).Number)
            .OrderBy(group => group.Key)
            .Select(group => new DnaBarLevel(group.Key, Math.Round(group.Average(note => note.Velocity), 1)))];
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
    /// minor key that fits its notes better (<see cref="KeyFinder"/>), and says so. A file with no key at all gets the key
    /// that fits all its notes best, marked inferred, as Insights does for a selection (testing, 2026-11-06: the lane said
    /// the file gave no key while Insights named one).
    /// </summary>
    private static List<DnaKey> KeyLane(IReadOnlyList<Note> notes, MeterMap meter, IReadOnlyList<KeySignature> keys, Tick first, Tick last)
    {
        KeySignature[] signatures = [.. keys.OrderBy(key => key.At).GroupBy(key => key.At).Select(group => group.Last())];
        if (signatures.Length == 0)
        {
            return KeyFromNotes(notes, meter, first, last);
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

    private static List<DnaKey> KeyFromNotes(IReadOnlyList<Note> notes, MeterMap meter, Tick first, Tick last)
    {
        if (last <= first)
        {
            return [];
        }

        // The reading a transcription's key signature is worked out with (0016): the bass weighs more, long notes count up
        // to a whole note, so a piece's tonic and its harmony outweigh passing chromatic notes.
        var span = TickSpan.FromBounds(first, last);
        if (Transcribed.KeyOf(notes) is not { } key)
        {
            return [];
        }

        return [new DnaKey(key, span, meter.BarAt(first).Number, meter.BarAt(new Tick(last.Value - 1)).Number, ModeInferred: false, KeyInferred: true)];
    }

    private static double[] Weights(IReadOnlyList<Note> notes, TickSpan span)
    {
        double[] weights = new double[12];
        foreach (Note note in notes.Where(note => note.Span.Onset >= span.Onset && note.Span.Onset < span.End))
        {
            weights[note.Pitch.MidiNumber % 12] += note.Span.Duration;
        }

        return weights;
    }

    private static KeyMode ModeFromNotes(IReadOnlyList<Note> notes, TickSpan span, int fifths)
    {
        double[] weights = Weights(notes, span);

        double major = KeyFinder.Score(weights, new MusicalKey(fifths, KeyMode.Major));
        double minor = KeyFinder.Score(weights, new MusicalKey(fifths, KeyMode.Minor));
        return minor > major ? KeyMode.Minor : KeyMode.Major;
    }

    private static Tick Later(Tick a, Tick b) => a >= b ? a : b;

    private static Tick Earlier(Tick a, Tick b) => a <= b ? a : b;
}