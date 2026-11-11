using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// The ideas of Piece DNA, found without a selection (testing item 22): the opening of every sentence, then of every
/// phrase, is a candidate, the line of its first bar (two bars when the first has fewer than
/// <see cref="StructureAnalyzer.MinNotesForRecurrence"/> notes); <see cref="StructureAnalyzer"/> finds where each is
/// heard again, exactly or transposed, across the whole piece; the candidates heard most often, at least twice, become
/// the ideas, at most <see cref="MaxIdeas"/>, and a candidate heard where an idea already is heard is the same idea. A
/// candidate heard exactly earlier in the piece is not an idea of its own: the idea is where it is first heard.
/// </summary>
/// <remarks>
/// Lines are the highest note at each onset of all the notes. Every card states only what its marks show: how many times
/// the idea is heard, how many exactly and how many higher or lower, and the sections it opens. Pure: the same notes,
/// meter and sentences give the same ideas.
/// </remarks>
public static class IdeaFinder
{
    /// <summary>The most ideas the map shows.</summary>
    public const int MaxIdeas = 6;

    /// <summary>A card names at most this many sections an idea opens, then says how many.</summary>
    private const int MaxListed = 4;

    /// <summary>The ideas, the one heard most often first; empty when no opening is heard again.</summary>
    /// <param name="sections">The form's sections, to say which sections an idea opens; empty for none.</param>
    /// <param name="taken">The places of an idea already on the map, the player's selection: an opening heard there is that idea.</param>
    /// <param name="firstId">The number of the first idea's id, after the ideas already on the map.</param>
    public static IReadOnlyList<DnaIdea> Find(IReadOnlyList<Note> notes, MeterMap meter, SentenceMap sentences, IReadOnlyList<DnaSection> sections, Words words = default, IReadOnlyList<IdeaOccurrence>? taken = null, int firstId = 1)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(sentences);
        ArgumentNullException.ThrowIfNull(sections);
        Note[] line = StructureAnalyzer.Line(notes);
        if (line.Length < StructureAnalyzer.MinNotesForRecurrence)
        {
            return [];
        }

        Tick[] starts = [.. sentences.Sentences.Select(sentence => sentence.Span.Onset)
            .Concat(sentences.Sentences.SelectMany(sentence => sentence.Phrases).Select(phrase => phrase.Span.Onset))
            .Distinct()];
        var candidates = new List<(Note[] Motif, List<Recurrence> Recurrences, int Order)>();
        for (int i = 0; i < starts.Length; i++)
        {
            if (Opening(line, meter, starts[i]) is { } motif)
            {
                candidates.Add((motif, StructureAnalyzer.FindRecurrences(line, motif, meter), i));
            }
        }

        var chosen = new List<(Note[] Motif, List<Recurrence> Recurrences)>();
        foreach ((Note[] motif, List<Recurrence> recurrences, _) in candidates
            .Where(candidate => candidate.Recurrences.Count > 0)
            .OrderByDescending(candidate => candidate.Recurrences.Count)
            .ThenBy(candidate => candidate.Order))
        {
            if (chosen.Count + firstId - 1 == MaxIdeas)
            {
                break;
            }

            // An opening heard exactly before it is that earlier idea, met again: the idea is named where it is first heard.
            if (recurrences.Any(recurrence => recurrence.IsExact && recurrence.Span.Onset < motif[0].Span.Onset)
                || chosen.Any(idea => HeardAt(idea.Motif, idea.Recurrences, motif[0].Span.Onset) || HeardAt(motif, recurrences, idea.Motif[0].Span.Onset))
                || (taken ?? []).Any(place => Covers(place.Span.Onset, new Tick(Math.Max(place.Span.Onset.Value, place.Span.End.Value - 1)), motif[0].Span.Onset)))
            {
                continue;
            }

            chosen.Add((motif, recurrences));
        }

        return [.. chosen.Select((idea, i) => Idea(i + 1, firstId + i, idea.Motif, idea.Recurrences, meter, sections, words))];
    }

    /// <summary>The line from a start to the end of its bar, or of the next bar when that is too short; null when still too short.</summary>
    private static Note[]? Opening(Note[] line, MeterMap meter, Tick start)
    {
        Bar bar = meter.BarAt(start);
        for (int bars = 1; bars <= 2; bars++)
        {
            Tick end = bars == 1 ? bar.Span.End : meter.BarAt(bar.Span.End).Span.End;
            Note[] motif = [.. line.Where(note => note.Span.Onset >= start && note.Span.Onset < end)];
            if (motif.Length >= StructureAnalyzer.MinNotesForRecurrence)
            {
                return motif;
            }
        }

        return null;
    }

    /// <summary>Whether a note starting at a tick falls inside the idea or one of the places it is heard again.</summary>
    private static bool HeardAt(Note[] motif, List<Recurrence> recurrences, Tick at) =>
        Covers(motif[0].Span.Onset, motif[^1].Span.Onset, at)
        || recurrences.Any(recurrence => Covers(recurrence.Span.Onset, LastOnset(recurrence, motif), at));

    private static Tick LastOnset(Recurrence recurrence, Note[] motif) =>
        new(recurrence.Span.Onset.Value + (motif[^1].Span.Onset.Value - motif[0].Span.Onset.Value));

    private static bool Covers(Tick first, Tick last, Tick at) => at >= first && at <= last;

    private static DnaIdea Idea(int number, int id, Note[] motif, List<Recurrence> recurrences, MeterMap meter, IReadOnlyList<DnaSection> sections, Words w)
    {
        TickSpan span = TickSpan.FromBounds(motif[0].Span.Onset, motif.Max(note => note.Span.End));
        int firstBar = meter.BarAt(span.Onset).Number;
        int lastBar = meter.BarAt(motif[^1].Span.Onset).Number;
        var occurrences = new List<IdeaOccurrence> { new(OccurrenceKind.Original, span, firstBar, lastBar, 0, [.. motif.Select(note => note.Id)]) };
        occurrences.AddRange(recurrences.Select(recurrence => new IdeaOccurrence(
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
        string title = w.T($"Idea {number} · {bars}", $"Идея {number} · {bars}");
        return new DnaIdea($"idea_{id}", false, span, firstBar, lastBar, [.. motif.Select(note => note.Id)], occurrences, title, Summary(occurrences, meter, sections, w));
    }

    /// <summary>"Heard 7 times: 5 exactly, 2 higher. Opens A and A′."; "Звучит 7 раз: 5 точно, 2 выше. Открывает A и A′."</summary>
    internal static string Summary(IReadOnlyList<IdeaOccurrence> occurrences, MeterMap meter, IReadOnlyList<DnaSection> sections, Words w)
    {
        int exact = occurrences.Count(occurrence => occurrence.Transposition == 0);
        int higher = occurrences.Count(occurrence => occurrence.Transposition > 0);
        int lower = occurrences.Count(occurrence => occurrence.Transposition < 0);
        string[] kinds = [.. new[]
        {
            exact > 0 && exact < occurrences.Count ? w.Count(exact, "exactly", "exactly", "точно", "точно", "точно") : null,
            higher > 0 ? w.Count(higher, "higher", "higher", "выше", "выше", "выше") : null,
            lower > 0 ? w.Count(lower, "lower", "lower", "ниже", "ниже", "ниже") : null,
        }.OfType<string>()];
        string summary = w.T("Heard ", "Звучит ") + w.Times(occurrences.Count)
            + (exact == occurrences.Count ? w.T(", always exactly", ", всегда точно") : ": " + w.Joined(kinds)) + ".";

        string[] opens = [.. sections
            .Where(section => occurrences.Any(occurrence => occurrence.Span.Onset >= section.Span.Onset && occurrence.Span.Onset < meter.GetBar(section.FirstBar).Span.End))
            .Select(section => section.Label)];
        string[] within = [.. sections
            .Where(section => occurrences.Any(occurrence => occurrence.Span.Onset >= section.Span.Onset && occurrence.Span.Onset < section.Span.End))
            .Select(section => section.Label)
            .Distinct()];
        if (opens.Length > MaxListed)
        {
            summary += " " + w.T("Opens ", "Открывает ") + w.Count(opens.Length, "section", "sections", "раздел", "раздела", "разделов") + ".";
        }
        else if (opens.Length > 0)
        {
            summary += " " + w.T("Opens ", "Открывает ") + w.Joined(opens) + ".";
        }
        else if (sections.Count > 1 && within.Length == 1)
        {
            summary += " " + w.T($"Only in {within[0]}.", $"Только в {within[0]}.");
        }

        return summary;
    }
}