using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Variations;

/// <summary>What the operation rules found: a violation that blocks the step, and flags shown without blocking.</summary>
/// <param name="Violation">Completes "&lt;operation&gt; …, so it is not available", for example "would put a note off the grid"; null when none.</param>
public sealed record RuleReport(string? Violation, IReadOnlyList<string> Flags);

/// <summary>
/// The PRD's operation rules, checked on every step against the notes it was made from: results
/// stay on the grid and in the piano's range, voices do not cross unless the source already crosses,
/// and hand spans over a 10th are flagged, not changed.
/// </summary>
public static class OperationRules
{
    /// <summary>A0, the lowest key of the piano.</summary>
    public const int LowestPitch = 21;

    /// <summary>C8, the highest key of the piano.</summary>
    public const int HighestPitch = 108;

    /// <summary>A major 10th in semitones; a hand that spans more is flagged.</summary>
    public const int MaxHandSpan = 16;

    /// <summary>
    /// The finest grids a result may use: 32nds and triplet 16ths (S1-R3). A position is on the grid
    /// when it falls on either.
    /// </summary>
    private static readonly long[] s_grids = [CanonicalTime.TicksPerQuarter / 8, CanonicalTime.TicksPerQuarter / 6];

    public static RuleReport Check(IReadOnlyList<Note> before, IReadOnlyList<Note> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        string? violation = Range(after) ?? Grid(before, after) ?? Crossing(before, after);
        return new RuleReport(violation, HandSpans(before, after));
    }

    private static string? Range(IReadOnlyList<Note> after) =>
        after.Any(static note => note.Pitch.MidiNumber is < LowestPitch or > HighestPitch)
            ? "would put a note outside the piano's range (A0–C8)"
            : null;

    /// <summary>A note whose source was on the grid must stay on it; an added note must be on it when every source note was.</summary>
    private static string? Grid(IReadOnlyList<Note> before, IReadOnlyList<Note> after)
    {
        Dictionary<string, Note> byId = before.ToDictionary(static note => note.Id, StringComparer.Ordinal);
        bool allOnGrid = before.All(OnGrid);
        bool leaves = after.Any(note => !OnGrid(note) && (byId.TryGetValue(note.Id, out Note? source) ? OnGrid(source) : allOnGrid));
        return leaves ? "would put a note off the grid of 32nds and triplet 16ths" : null;
    }

    private static bool OnGrid(Note note) => OnGrid(note.Span.Onset.Value) && OnGrid(note.Span.End.Value);

    private static bool OnGrid(long tick) => s_grids.Any(grid => tick % grid == 0);

    /// <summary>
    /// Two voices cross when one sounds above the other where the source only had it below. Voices
    /// are notated voices within a track; without them, hands. Notes with neither are not compared.
    /// </summary>
    private static string? Crossing(IReadOnlyList<Note> before, IReadOnlyList<Note> after)
    {
        HashSet<(string Upper, string Lower)> was = Orders(before);
        return Orders(after).Any(order => !was.Contains(order) && was.Contains((order.Lower, order.Upper)))
            ? "would make voices cross"
            : null;
    }

    private static HashSet<(string Upper, string Lower)> Orders(IReadOnlyList<Note> notes)
    {
        var orders = new HashSet<(string, string)>();
        (Note Note, string Voice)[] voiced = notes
            .Select(static note => (Note: note, Voice: VoiceOf(note)))
            .Where(static pair => pair.Voice is not null)
            .Select(static pair => (pair.Note, pair.Voice!))
            .ToArray();
        for (int i = 0; i < voiced.Length; i++)
        {
            for (int j = i + 1; j < voiced.Length; j++)
            {
                (Note a, string voiceA) = voiced[i];
                (Note b, string voiceB) = voiced[j];
                if (voiceA == voiceB || a.Pitch == b.Pitch || !a.Span.Overlaps(b.Span))
                {
                    continue;
                }

                orders.Add(a.Pitch > b.Pitch ? (voiceA, voiceB) : (voiceB, voiceA));
            }
        }

        return orders;
    }

    private static string? VoiceOf(Note note) => note.Voice is int voice
        ? $"track {note.Track} voice {voice}"
        : note.Hand is { } hand ? $"{hand.Value} hand" : null;

    /// <summary>Each hand's widest chord in the result that is wider than a 10th and was not already in the source.</summary>
    private static IReadOnlyList<string> HandSpans(IReadOnlyList<Note> before, IReadOnlyList<Note> after)
    {
        Dictionary<Hand, int> was = WidestSpans(before);
        return WidestSpans(after)
            .Where(span => span.Value > MaxHandSpan && span.Value > was.GetValueOrDefault(span.Key))
            .OrderBy(static span => span.Key)
            .Select(static span => $"The {span.Key.ToString().ToLowerInvariant()} hand spans {span.Value} semitones, more than a 10th.")
            .ToArray();
    }

    private static Dictionary<Hand, int> WidestSpans(IReadOnlyList<Note> notes)
    {
        var widest = new Dictionary<Hand, int>();
        foreach (IGrouping<Hand, Note> hand in notes.Where(static note => note.Hand is not null).GroupBy(static note => note.Hand!.Value))
        {
            Note[] played = [.. hand];
            int span = played.Max(note => played.Where(other => other.Span.Contains(note.Span.Onset)).Max(static other => other.Pitch.MidiNumber)
                - played.Where(other => other.Span.Contains(note.Span.Onset)).Min(static other => other.Pitch.MidiNumber));
            widest[hand.Key] = span;
        }

        return widest;
    }
}