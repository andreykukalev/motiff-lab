using MotiffLab.MusicCore.Analysis.Phrasing;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>What an idea of Piece DNA stands for, so a card can say why it was chosen (#582 item 15).</summary>
public enum IdeaKind
{
    /// <summary>An opening of a sentence or phrase heard again (<see cref="IdeaFinder"/>, the cards as they were).</summary>
    Found,

    /// <summary>The player's selection.</summary>
    Selection,

    /// <summary>The piece's first phrase, or its first two when they ask and answer.</summary>
    Theme,

    /// <summary>The first phrase of the first section whose material the opening section does not share.</summary>
    Contrast,

    /// <summary>A bar heard three times or more in a row, each time at another pitch (an octave apart is the same pitch here).</summary>
    Sequence,

    /// <summary>The opening heard most often apart from the theme's.</summary>
    Motif,

    /// <summary>The phrase that holds the highest note of the piece, above anything in the theme.</summary>
    Peak,
}

/// <summary>
/// The ideas of Piece DNA chosen by what they are in the music (#582 item 15, once behind «Экспериментальное»: «Идеи по смыслу»):
/// the theme (the first phrase, with the next when they begin alike and the first closes half and the second fully), the
/// contrast (the first phrase of the first section with a letter of its own), a sequence (a bar heard three times or more
/// in a row, each statement moved from the one before), the motif heard most often outside them, and the phrase with the
/// piece's highest note when it lies above the theme. Each is chosen only when the engine measures it, and the card's line
/// says why from what was measured: the closes, the sections, the repeats and their transpositions, notes per beat, the
/// highest note and the key lane.
/// </summary>
/// <remarks>
/// The melody is the highest note at each onset of the right hand's notes when the notes carry both hands, else of all
/// the notes, so the left hand's figures do not stand in for the tune. The phrases, closes, sections and keys are readings
/// the map marks inferred. The cards of phrases share no bars; a sequence or a motif may lie inside one. Pure: the same notes, meter, structure, key
/// lane and language give the same ideas.
/// </remarks>
internal static partial class MusicalIdeas
{
    /// <summary>The most ideas the map shows besides the selection.</summary>
    public const int MaxIdeas = 5;

    /// <summary>A sequence has at least this many statements in a row.</summary>
    public const int MinStatements = 3;

    /// <summary>A card plays at most this many bars of a phrase, as a long phrase is read (<see cref="PhraseMap.LongPhraseBars"/>).</summary>
    private const int MaxBars = PhraseMap.LongPhraseBars;

    /// <summary>A highest note this many semitones away from the theme's is said to reach higher or stay lower.</summary>
    private const int RegisterStep = 5;

    /// <summary>The ideas in the order theme, contrast, sequence, motif, peak, each only where the music has it.</summary>
    /// <param name="keys">The key lane, for the key a contrast sounds in.</param>
    /// <param name="taken">The places of an idea already on the map, the player's selection: no card starts in them.</param>
    /// <param name="firstId">The number of the first idea's id, after the ideas already on the map.</param>
    public static IReadOnlyList<DnaIdea> Find(IReadOnlyList<Note> notes, MeterMap meter, PieceStructure structure, IReadOnlyList<DnaKey> keys, Words words = default, IReadOnlyList<IdeaOccurrence>? taken = null, int firstId = 1)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(keys);
        SentenceMap sentences = structure.Levels.Sentences;
        Note[] line = MelodyLine(notes);
        if (sentences.Sentences.Count == 0 || line.Length < StructureAnalyzer.MinNotesForRecurrence)
        {
            return [];
        }

        var piece = new Piece(notes, line, meter, sentences, structure.Sections, keys, words);
        var chosen = new List<DnaIdea>();
        // A phrase's card (theme, contrast, peak) shares no bars with another card; a sequence or a motif, a shorter thing
        // heard inside a phrase, only never starts where another card does.
        void Add(DnaIdea? idea)
        {
            bool phrase = idea?.Kind is IdeaKind.Theme or IdeaKind.Contrast or IdeaKind.Peak;
            if (idea is not null && chosen.Count < MaxIdeas
                && !chosen.Any(other => phrase ? Meet(other.Span, idea.Span) : other.Span.Onset == idea.Span.Onset)
                && !(taken ?? []).Any(place => Meet(place.Span, idea.Span)))
            {
                chosen.Add(idea with { Id = $"idea_{firstId + chosen.Count}" });
            }
        }

        DnaIdea? theme = Theme(piece);
        Add(theme);
        Add(Contrast(piece, theme));
        Add(Sequence(piece));
        Add(Motif(piece, chosen, taken ?? []));
        Add(Peak(piece, theme));
        return chosen;
    }

    /// <summary>The melody: the highest note at each onset of the right hand when the notes carry both hands, else of all of them.</summary>
    internal static Note[] MelodyLine(IReadOnlyList<Note> notes)
    {
        bool hands = notes.Any(note => note.Hand?.Value == Hand.Right) && notes.Any(note => note.Hand?.Value == Hand.Left);
        return StructureAnalyzer.Line(hands ? notes.Where(note => note.Hand?.Value == Hand.Right) : notes);
    }

    private sealed record Piece(IReadOnlyList<Note> Notes, Note[] Line, MeterMap Meter, SentenceMap Sentences, IReadOnlyList<DnaSection> Sections, IReadOnlyList<DnaKey> Keys, Words W)
    {
        public IEnumerable<Phrase> Phrases => Sentences.Sentences.SelectMany(sentence => sentence.Phrases);
    }

    /// <summary>What the theme's card line is written from (<c>MusicalIdeas.Facts.cs</c>): whether it is a question and its answer, how its first phrase closes, how often its opening comes back elsewhere, how many of those at another pitch, and the later sections whose start it opens.</summary>
    private sealed record ThemeFound(bool Period, CloseKind FirstClose, int Returns, int Moved, IReadOnlyList<string> Opens);

    /// <summary>The contrast's section and the opening's, the notes per beat of both, how much higher its highest note is, and its key when the theme's differs.</summary>
    private sealed record ContrastFound(string Label, string OpeningLabel, double Density, double Against, int Higher, DnaKey? Key);

    /// <summary>How many times the bar is heard in a row, and the step in semitones from each statement to the next.</summary>
    private sealed record SequenceFound(int Statements, int[] Steps);

    // MARK: - The theme

    private static DnaIdea? Theme(Piece piece)
    {
        Phrase[] phrases = [.. piece.Phrases];
        Phrase first = phrases[0];
        Note[]? head = IdeaFinder.Opening(piece.Line, piece.Meter, first.Span.Onset);
        List<Recurrence> returns = head is null ? [] : StructureAnalyzer.FindRecurrences(piece.Line, head, piece.Meter);
        CloseKind firstClose = piece.Sentences.CloseOf(first).Kind;

        // A question and its answer: the next phrase begins as the first does, the first closes half or not at all, the next fully.
        Phrase? answer = phrases.Length > 1 ? phrases[1] : null;
        bool period = answer is not null && firstClose != CloseKind.Full && piece.Sentences.CloseOf(answer).Kind == CloseKind.Full
            && returns.Any(recurrence => Near(recurrence.Span.Onset, answer.Span.Onset, piece.Meter));
        TickSpan span = Capped(piece.Meter, period ? TickSpan.FromBounds(first.Span.Onset, answer!.Span.End) : first.Span);

        List<Recurrence> heardAgain = [.. returns.Where(recurrence => !Inside(recurrence.Span.Onset, span))];
        string[] opens = [.. piece.Sections.Skip(1)
            .Where(section => heardAgain.Any(recurrence => Inside(recurrence.Span.Onset, piece.Meter.GetBar(section.FirstBar).Span) || recurrence.Span.Onset == section.Span.Onset))
            .Select(section => section.Label)];
        var found = new ThemeFound(period, firstClose, heardAgain.Count, heardAgain.Count(recurrence => !recurrence.IsExact), opens);
        return Idea(IdeaKind.Theme, span, piece, ThemeText(found, piece.W), [.. heardAgain.Select(Occurrence)]);
    }

    // MARK: - The contrast

    private static DnaIdea? Contrast(Piece piece, DnaIdea? theme)
    {
        if (piece.Sections.Count < 2 || theme is null)
        {
            return null;
        }

        DnaSection opening = piece.Sections[0];
        DnaSection? other = piece.Sections.Skip(1).FirstOrDefault(section =>
            Letter(section.Label) != Letter(opening.Label) && !section.SharesWith.Contains(opening.Id) && !opening.SharesWith.Contains(section.Id));
        Phrase? phrase = other is null ? null : piece.Phrases.FirstOrDefault(candidate => candidate.Span.Onset >= other.Span.Onset);
        if (other is null || phrase is null)
        {
            return null;
        }

        TickSpan span = Capped(piece.Meter, TickSpan.FromBounds(phrase.Span.Onset, Earlier(phrase.Span.End, other.Span.End)));
        DnaKey? key = KeyAt(piece, span.Onset) is { } own && KeyAt(piece, theme.Span.Onset) is { } themeKey && own.Key != themeKey.Key ? own : null;
        var found = new ContrastFound(other.Label, opening.Label, Density(piece, span), Density(piece, theme.Span), Highest(piece, span) - Highest(piece, theme.Span), key);
        return Idea(IdeaKind.Contrast, span, piece, ContrastText(found, piece.W), []);
    }

    private static char Letter(string label) => label.Length > 0 ? label[0] : ' ';

    /// <summary>Notes starting per beat over a passage, to a tenth.</summary>
    private static double Density(Piece piece, TickSpan span)
    {
        double beats = (double)span.Duration / piece.Meter.BarAt(span.Onset).Signature.BeatTicks;
        return beats > 0 ? Math.Round(piece.Notes.Count(note => Inside(note.Span.Onset, span)) / beats, 1) : 0;
    }

    private static int Highest(Piece piece, TickSpan span) =>
        piece.Notes.Where(note => Inside(note.Span.Onset, span)).Select(note => note.Pitch.MidiNumber).DefaultIfEmpty(0).Max();

    private static DnaKey? KeyAt(Piece piece, Tick at) => piece.Keys.LastOrDefault(key => key.Span.Onset <= at) ?? piece.Keys.FirstOrDefault();

    // MARK: - The sequence

    /// <summary>The longest run of a bar's melody heard again in the bars right after it, each statement moved from the one before.</summary>
    private static DnaIdea? Sequence(Piece piece)
    {
        Note[] line = piece.Line;
        (Note[] Cell, List<Recurrence> Statements)? best = null;
        foreach (IGrouping<int, Note> bar in line.GroupBy(note => piece.Meter.BarAt(note.Span.Onset).Number))
        {
            Note[] cell = [.. bar];
            if (cell.Length < StructureAnalyzer.MinNotesForRecurrence || cell.Select(note => note.Pitch.MidiNumber).Distinct().Count() < 2)
            {
                continue;
            }

            List<Recurrence> heard = StructureAnalyzer.FindRecurrences(line, cell, piece.Meter);
            var statements = new List<Recurrence>();
            Tick at = cell[0].Span.Onset;
            int shift = 0;
            Bar now = piece.Meter.BarAt(at);
            while (heard.FirstOrDefault(recurrence => recurrence.Span.Onset.Value == at.Value + now.Span.Duration && (recurrence.Transposition - shift) % 12 != 0) is { } next)
            {
                statements.Add(next);
                at = next.Span.Onset;
                shift = next.Transposition;
                now = piece.Meter.BarAt(at);
            }

            if (statements.Count + 1 >= MinStatements && statements.Count > (best?.Statements.Count ?? 0))
            {
                best = (cell, statements);
            }
        }

        if (best is not { } run)
        {
            return null;
        }

        (Note[] first, List<Recurrence> moved) = run;
        int[] shifts = [0, .. moved.Select(recurrence => recurrence.Transposition)];
        var found = new SequenceFound(shifts.Length, [.. shifts.Zip(shifts.Skip(1), (from, to) => to - from)]);
        TickSpan span = TickSpan.FromBounds(first[0].Span.Onset, moved[^1].Span.End);
        return Idea(IdeaKind.Sequence, span, piece, SequenceText(found, piece.W), [.. moved.Select(Occurrence)]);
    }

    // MARK: - The motif and the peak

    /// <summary>The opening heard most often outside the cards chosen and the places they are heard, with more than one pitch.</summary>
    private static DnaIdea? Motif(Piece piece, List<DnaIdea> chosen, IReadOnlyList<IdeaOccurrence> taken)
    {
        IdeaOccurrence[] places = [.. taken, .. chosen.SelectMany(idea => idea.Occurrences)];
        DnaIdea? found = IdeaFinder.Find(piece.Line, piece.Meter, piece.Sentences, piece.Sections, piece.W, places)
            .FirstOrDefault(idea => !places.Any(place => Inside(idea.Span.Onset, place.Span))
                && idea.NoteIds.Select(id => piece.Line.First(note => note.Id == id).Pitch.MidiNumber).Distinct().Count() > 1);
        if (found is null)
        {
            return null;
        }

        return found with { Kind = IdeaKind.Motif, Title = MotifTitle(found.FirstBar, found.LastBar, piece.W) };
    }

    private static DnaIdea? Peak(Piece piece, DnaIdea? theme)
    {
        if (theme is null)
        {
            return null;
        }

        Note top = piece.Notes.OrderByDescending(note => note.Pitch.MidiNumber).ThenBy(note => note.Span.Onset).First();
        int above = top.Pitch.MidiNumber - Highest(piece, theme.Span);
        Phrase? phrase = piece.Phrases.LastOrDefault(candidate => candidate.Span.Onset <= top.Span.Onset);
        if (above < RegisterStep || phrase is null)
        {
            return null;
        }

        int bar = piece.Meter.BarAt(top.Span.Onset).Number;
        TickSpan span = Capped(piece.Meter, phrase.Span);
        if (!Inside(top.Span.Onset, span))
        {
            span = Capped(piece.Meter, TickSpan.FromBounds(piece.Meter.GetBar(bar).Span.Onset, phrase.Span.End));
        }

        return Idea(IdeaKind.Peak, span, piece, PeakText(bar, above, piece.W), []);
    }

    // MARK: - Cards

    private static DnaIdea Idea(IdeaKind kind, TickSpan span, Piece piece, (string Name, string Why) text, List<IdeaOccurrence> heardAgain)
    {
        int firstBar = piece.Meter.BarAt(span.Onset).Number;
        Note[] inside = [.. piece.Notes.Where(note => Inside(note.Span.Onset, span)).OrderBy(note => note.Span.Onset).ThenBy(note => note.Id, StringComparer.Ordinal)];
        int lastBar = inside.Length > 0 ? piece.Meter.BarAt(inside[^1].Span.Onset).Number : firstBar;
        string[] ids = [.. inside.Select(note => note.Id)];
        List<IdeaOccurrence> occurrences = [new(OccurrenceKind.Original, span, firstBar, lastBar, 0, ids), .. heardAgain];
        occurrences.Sort((a, b) => a.Span.Onset.CompareTo(b.Span.Onset));
        return new DnaIdea("", false, span, firstBar, lastBar, ids, occurrences, Title(text.Name, firstBar, lastBar, piece.W), text.Why) { Kind = kind };
    }

    private static IdeaOccurrence Occurrence(Recurrence recurrence) => new(
        recurrence.IsExact ? OccurrenceKind.Exact : OccurrenceKind.Transposed,
        recurrence.Span,
        recurrence.FirstBar,
        recurrence.LastBar,
        recurrence.Transposition,
        recurrence.NoteIds);

    /// <summary>A passage of at most <see cref="MaxBars"/> bars from its start.</summary>
    private static TickSpan Capped(MeterMap meter, TickSpan span)
    {
        Bar first = meter.BarAt(span.Onset);
        Tick limit = meter.GetBar(first.Number + MaxBars - 1).Span.End;
        return TickSpan.FromBounds(span.Onset, Earlier(span.End, limit));
    }

    /// <summary>Whether two onsets are within a beat of each other, as a phrase boundary and the opening heard there may be.</summary>
    private static bool Near(Tick a, Tick b, MeterMap meter) => Math.Abs(a.Value - b.Value) <= meter.BarAt(b).Signature.BeatTicks;

    private static bool Inside(Tick at, TickSpan span) => at >= span.Onset && at < span.End;

    private static bool Meet(TickSpan a, TickSpan b) => a.Onset < b.End && b.Onset < a.End;

    private static Tick Earlier(Tick a, Tick b) => a <= b ? a : b;
}