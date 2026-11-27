using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>The direction from one melody note to the next.</summary>
public enum ContourStep
{
    Up,
    Down,
    Same,
}

/// <summary>The key a motif was read in, how, and the key that came closest.</summary>
/// <param name="RunnerUp">The next best key when the key was inferred from the notes; null when the file states it.</param>
public sealed record KeyReading(MusicalKey Key, FactCertainty Certainty, MusicalKey? RunnerUp);

/// <summary>What the engine measured in a motif, as values and as plain facts.</summary>
/// <param name="Line">The melody: the notes in order, or the top line (<see cref="MotifTexture.Melody"/>) when notes sound together.</param>
/// <param name="LineIsTopNotes">True when notes sound together, so <paramref name="Line"/> is inferred as the top line.</param>
/// <param name="Intervals">Semitones from each melody note to the next; positive is up.</param>
/// <param name="Contour">The direction of each interval.</param>
/// <param name="Key">Null when the motif has no notes.</param>
/// <param name="Facts">The facts in display order: melody, rhythm, then key.</param>
public sealed record MotifAnalysis(
    IReadOnlyList<Note> Line,
    bool LineIsTopNotes,
    IReadOnlyList<int> Intervals,
    IReadOnlyList<ContourStep> Contour,
    KeyReading? Key,
    IReadOnlyList<MotifFact> Facts);

/// <summary>
/// Plain facts about a selected motif: melody line, intervals, contour, range, scale degrees
/// (S1-A2), durations, onset grid, silences, beat grouping, syncopation, accents where marked and
/// repeated rhythms (S1-A1), and key. No AI: every fact is measured
/// from the notes or read from the file, and labelled detected, inferred or uncertain.
/// </summary>
/// <remarks>
/// Analysis is pure: the same notes, meter, selection and key signature give the same facts.
/// Interval names go by size in semitones, not by spelling, so three semitones is a minor 3rd
/// even where the score writes an augmented 2nd.
/// Each lens measures in its own file (<c>.Melody</c>, <c>.Rhythm</c>, <c>.Character</c> with <c>.Texture</c>) and
/// writes its sentences from those measures in another (<c>.Melody.Facts</c>, <c>.Rhythm.Facts</c>,
/// <c>.Character.Facts</c>); this file picks the notes, reads the key and holds the note values and grids they share,
/// and <c>.Facts</c> the key fact and the wording every lens uses.
/// </remarks>
internal static partial class MotifAnalyzer
{
    /// <summary>A key read from fewer notes or pitch classes than this is uncertain.</summary>
    public const int MinNotesForKey = 6;

    public const int MinPitchClassesForKey = 5;

    /// <summary>A key inferred from the notes is uncertain when the next key scores within this margin.</summary>
    public const double ClearKeyMargin = 0.05;

    private static readonly string[] s_majorDegrees = ["1", "♯1", "2", "♯2", "3", "4", "♯4", "5", "♯5", "6", "♭7", "7"];

    private static readonly string[] s_minorDegrees = ["1", "♭2", "2", "3", "♯3", "4", "♯4", "5", "6", "♯6", "7", "♯7"];

    /// <summary>A note value by its length in ticks: English singular and plural, Russian for one, few and many.</summary>
    private readonly record struct NoteValue(long Ticks, string One, string Many, string RussianOne, string RussianFew, string RussianMany)
    {
        public string Name(Words words) => words.T(One, RussianOne);

        public string Counted(int count, Words words) => words.Count(count, One, Many, RussianOne, RussianFew, RussianMany);
    }

    private static readonly NoteValue[] s_values =
    [
        new(1920, "whole note", "whole notes", "целая", "целые", "целых"),
        new(1440, "dotted half", "dotted halves", "половинная с точкой", "половинные с точкой", "половинных с точкой"),
        new(960, "half", "halves", "половинная", "половинные", "половинных"),
        new(720, "dotted quarter", "dotted quarters", "четверть с точкой", "четверти с точкой", "четвертей с точкой"),
        new(480, "quarter", "quarters", "четверть", "четверти", "четвертей"),
        new(360, "dotted eighth", "dotted eighths", "восьмая с точкой", "восьмые с точкой", "восьмых с точкой"),
        new(320, "quarter-note triplet", "quarter-note triplets", "триольная четверть", "триольные четверти", "триольных четвертей"),
        new(240, "eighth", "eighths", "восьмая", "восьмые", "восьмых"),
        new(180, "dotted sixteenth", "dotted sixteenths", "шестнадцатая с точкой", "шестнадцатые с точкой", "шестнадцатых с точкой"),
        new(160, "eighth-note triplet", "eighth-note triplets", "триольная восьмая", "триольные восьмые", "триольных восьмых"),
        new(120, "sixteenth", "sixteenths", "шестнадцатая", "шестнадцатые", "шестнадцатых"),
        new(80, "sixteenth-note triplet", "sixteenth-note triplets", "триольная шестнадцатая", "триольные шестнадцатые", "триольных шестнадцатых"),
        new(60, "thirty-second", "thirty-seconds", "тридцать вторая", "тридцать вторые", "тридцать вторых"),
    ];

    /// <summary>The note value a length in ticks is, or null when it is no single note value.</summary>
    private static NoteValue? ValueOf(long ticks)
    {
        int index = Array.FindIndex(s_values, value => value.Ticks == ticks);
        return index < 0 ? null : s_values[index];
    }

    // Onset grids from coarse to fine, measured from the bar line; the Russian names the grid ("сетка восьмых") and goes after "с" ("с триольными восьмыми").
    private static readonly (long Ticks, string Name, bool Triplet, string RussianGrid, string RussianWith)[] s_grids =
    [
        (480, "quarter-note", false, "четвертей", "четвертями"),
        (240, "eighth-note", false, "восьмых", "восьмыми"),
        (160, "eighth-note triplet", true, "триольных восьмых", "триольными восьмыми"),
        (120, "sixteenth-note", false, "шестнадцатых", "шестнадцатыми"),
        (80, "sixteenth-note triplet", true, "триольных шестнадцатых", "триольными шестнадцатыми"),
        (60, "thirty-second-note", false, "тридцать вторых", "тридцать вторыми"),
    ];

    /// <param name="notes">The notes of the piece; the selection's notes are picked by id.</param>
    /// <param name="signature">The key signature in force where the selection starts, or null when the file has none.</param>
    /// <param name="accents">The accents the file writes, or null when the format cannot mark accents (MIDI), which leaves out the accent fact.</param>
    /// <param name="phrases">
    /// The phrase boundaries of the notes the motif was picked from, found in the source and set by
    /// the player (<see cref="Phrasing.PhraseBoundaryFinder"/>), or null to leave out the phrase fact.
    /// </param>
    /// <param name="words">The language of the facts' text, bases and place labels; English by default.</param>
    public static MotifAnalysis Analyze(
        IEnumerable<Note> notes,
        MeterMap meter,
        MotifSelection selection,
        Pieces.KeySignature? signature,
        IEnumerable<Pieces.AccentMark>? accents = null,
        IEnumerable<Phrasing.PhraseBoundary>? phrases = null,
        Words words = default)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(selection);

        Note[] chosen = Chosen(notes, selection);
        if (chosen.Length == 0)
        {
            return new MotifAnalysis([], false, [], [], null, []);
        }

        LineMeasure line = MeasureLine(chosen);
        KeyReading key = ReadKey(chosen, signature);
        Hit[] hits = Hits(chosen.GroupBy(note => note.Span.Onset));
        List<MotifFact> facts =
        [
            .. MelodyFacts(chosen, line, key, meter, phrases, words),
            .. RhythmFacts(chosen, hits, meter, selection.Span, key.Key, accents, words),
            KeyFact(key, signature, chosen.Length, PitchClassCount(chosen), words),
        ];
        return new MotifAnalysis(line.Line, line.TopNotes, line.Intervals, line.Contour, key, WithHand(facts, selection, words));
    }

    /// <summary>The selection's notes, picked from the piece's by id, in time order, then by pitch and id.</summary>
    private static Note[] Chosen(IEnumerable<Note> notes, MotifSelection selection)
    {
        var byId = new Dictionary<string, Note>(StringComparer.Ordinal);
        foreach (Note note in notes)
        {
            byId.TryAdd(note.Id, note);
        }

        return [.. selection.NoteIds.Where(byId.ContainsKey).Select(id => byId[id])
            .OrderBy(note => note.Span.Onset).ThenBy(note => note.Pitch.MidiNumber).ThenBy(note => note.Id, StringComparer.Ordinal)];
    }

    /// <summary>The scale degree of a pitch in a key: "5", or "♯7" for a note outside the scale.</summary>
    public static string Degree(Pitch pitch, MusicalKey key)
    {
        int offset = ((pitch.PitchClass - key.TonicPitchClass) % 12 + 12) % 12;
        return key.Mode == KeyMode.Major ? s_majorDegrees[offset] : s_minorDegrees[offset];
    }

    /// <summary>The key the notes are in: stated by the file, or read from the pitches with the signature's help when it gives only the accidentals.</summary>
    internal static KeyReading ReadKey(Note[] notes, Pieces.KeySignature? signature)
    {
        var weights = new double[12];
        foreach (Note note in notes)
        {
            weights[note.Pitch.PitchClass] += note.Span.Duration;
        }

        bool thin = notes.Length < MinNotesForKey || PitchClassCount(notes) < MinPitchClassesForKey;
        if (signature is { Mode: { } mode })
        {
            return new KeyReading(new MusicalKey(signature.Fifths, mode), FactCertainty.Detected, null);
        }

        if (signature is not null)
        {
            var major = new MusicalKey(signature.Fifths, KeyMode.Major);
            double majorScore = KeyFinder.Score(weights, major);
            double minorScore = KeyFinder.Score(weights, major.Relative);
            (MusicalKey best, MusicalKey other) = minorScore > majorScore ? (major.Relative, major) : (major, major.Relative);
            bool close = Math.Abs(majorScore - minorScore) < ClearKeyMargin;
            return new KeyReading(best, thin || close ? FactCertainty.Uncertain : FactCertainty.Inferred, other);
        }

        IReadOnlyList<(MusicalKey Key, double Score)> ranked = KeyFinder.Rank(weights);
        bool unclear = ranked[0].Score - ranked[1].Score < ClearKeyMargin;
        return new KeyReading(ranked[0].Key, thin || unclear ? FactCertainty.Uncertain : FactCertainty.Inferred, ranked[1].Key);
    }

    /// <summary>True when an onset falls on one of the grids, measured from the bar line.</summary>
    private static bool OnGrid(MeterMap meter, Tick onset) => s_grids.Any(grid => OffsetInBar(meter, onset) % grid.Ticks == 0);

    private static int OffGridCount(Note[] notes, MeterMap meter) => notes.Count(note => !OnGrid(meter, note.Span.Onset));

    /// <summary>Ticks from the bar line, counting a pickup from where a full bar would start.</summary>
    private static long OffsetInBar(MeterMap meter, Tick position)
    {
        Bar bar = meter.BarAt(position);
        return position.Value - bar.Origin;
    }

    private static int PitchClassCount(Note[] notes) => notes.Select(note => note.Pitch.PitchClass).Distinct().Count();

    private static FactCertainty Weaker(FactCertainty a, FactCertainty b) => (FactCertainty)Math.Max((int)a, (int)b);
}