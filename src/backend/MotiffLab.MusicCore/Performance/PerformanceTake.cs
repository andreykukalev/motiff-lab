using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Performance;

/// <summary>A note taken from a performance into the score, written on the score's 16th grid.</summary>
/// <param name="Id">"perf-" followed by the performance's id and the played note's id, so it never meets an imported id.</param>
public sealed record TakenNote(string Id, Pitch Pitch, TickSpan Span, int Velocity, int Track, Hand? Hand);

/// <summary>
/// One bar taken from a performance into the score (decision record 0022, section 5): the printed notes it no
/// longer plays and the notes it adds. Printed notes the performance also plays stay as printed, so taking a bar
/// changes only what differs. Kept in the project, so the score is rebuilt without comparing again.
/// </summary>
/// <param name="Bar">The bar number.</param>
/// <param name="Removed">Ids of printed notes the performance does not play.</param>
/// <param name="Added">The performance's notes the print does not have.</param>
public sealed record TakenBar(int Bar, IReadOnlyList<string> Removed, IReadOnlyList<TakenNote> Added);

/// <summary>Taking bars of a performance into the score, and the score with the bars taken. Pure and deterministic.</summary>
public static class PerformanceTake
{
    /// <summary>The prefix of every taken note's id.</summary>
    public const string IdPrefix = "perf-";

    /// <summary>The grid taken notes are written on: a 16th.</summary>
    public const long Grid = CanonicalTime.TicksPerQuarter / 4;

    /// <summary>Whether a note was taken from a performance.</summary>
    public static bool IsTaken(string noteId) => noteId.StartsWith(IdPrefix, StringComparison.Ordinal);

    /// <summary>
    /// A bar of the comparison taken into the score: the printed notes not heard leave, the played notes not
    /// printed come in. Likely artefacts and printed notes the recording probably could not hear stay as they
    /// are. <paramref name="leaveOut"/> names notes of either side the player unticked.
    /// </summary>
    /// <exception cref="ArgumentException">The bar is not aligned, so there is nothing to take.</exception>
    public static TakenBar Take(
        string performanceId,
        BarComparison bar,
        IReadOnlyList<PlayedNote> played,
        PerformanceAlignment alignment,
        MeterMap meter,
        IReadOnlyList<Note> score,
        IReadOnlyCollection<string>? leaveOut = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(performanceId);
        ArgumentNullException.ThrowIfNull(bar);
        ArgumentNullException.ThrowIfNull(played);
        ArgumentNullException.ThrowIfNull(alignment);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(score);
        if (!bar.Aligned)
        {
            throw new ArgumentException($"Bar {bar.Bar} does not line up with the performance, so nothing can be taken from it.", nameof(bar));
        }

        HashSet<string> skip = leaveOut is null ? [] : [.. leaveOut];
        Dictionary<string, PlayedNote> byId = played.ToDictionary(note => note.Id, StringComparer.Ordinal);
        TickSpan span = meter.GetBar(bar.Bar).Span;
        List<Note> printed = [.. score.Where(note => span.Contains(note.Span.Onset))];

        List<TakenNote> added = [];
        foreach (PlacedNote note in bar.PlayedNotPrinted.Where(note => !skip.Contains(note.Id)))
        {
            (TickSpan place, int track, Hand hand) = Place(note, byId[note.Id], span, alignment, printed);
            added.Add(new TakenNote($"{IdPrefix}{performanceId}-{note.Id}", note.Pitch, place, note.Velocity, track, hand));
        }

        return new TakenBar(
            bar.Bar,
            [.. bar.PrintedNotHeard.Where(id => !skip.Contains(id)).Order(StringComparer.Ordinal)],
            [.. added.OrderBy(note => note.Span.Onset).ThenBy(note => note.Pitch).ThenBy(note => note.Id, StringComparer.Ordinal)]);
    }

    /// <summary>The confidence a bar needs for Take everything automatically (testing item 16): 80 %.</summary>
    public const int AutomaticConfidence = 80;

    /// <summary>
    /// Take everything automatically (testing item 16): every bar the performance plays differently, aligned, not marked
    /// "not a difference" and not taken yet, whose comparison is at least <paramref name="confidence"/> percent sure, taken
    /// as <see cref="Take"/> takes one. Restoring the bars taken brings the print back, so it can be undone.
    /// </summary>
    public static IReadOnlyList<TakenBar> TakeAll(
        SavedPerformance performance,
        PerformanceComparison comparison,
        MeterMap meter,
        IReadOnlyList<Note> score,
        int confidence = AutomaticConfidence)
    {
        ArgumentNullException.ThrowIfNull(performance);
        ArgumentNullException.ThrowIfNull(comparison);
        IReadOnlyList<PlayedNote> played = performance.Notes();
        HashSet<int> skip = [.. performance.Dismissed, .. performance.Taken.Select(bar => bar.Bar)];
        return [.. comparison.Differences
            .Where(bar => bar.Aligned && bar.Confidence >= confidence && !skip.Contains(bar.Bar))
            .Select(bar => Take(performance.Id, bar, played, comparison.Alignment, meter, score))
            .Where(bar => bar.Added.Count > 0 || bar.Removed.Count > 0)];
    }

    /// <summary>
    /// The notes with the bars taken: removed notes left out, added notes marked user-edited (the player chose
    /// them) after the rest, in onset order. A removed id that is not there is ignored, so an edit made since
    /// cannot block the piece from opening.
    /// </summary>
    public static IReadOnlyList<Note> Apply(IReadOnlyList<Note> notes, IEnumerable<TakenBar> taken)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(taken);
        TakenBar[] bars = [.. taken];
        if (bars.Length == 0)
        {
            return notes;
        }

        HashSet<string> removed = [.. bars.SelectMany(bar => bar.Removed)];
        HashSet<string> present = [.. notes.Select(note => note.Id)];
        IEnumerable<Note> added = bars
            .SelectMany(bar => bar.Added)
            .Where(note => !present.Contains(note.Id))
            .OrderBy(note => note.Span.Onset).ThenBy(note => note.Pitch).ThenBy(note => note.Id, StringComparer.Ordinal)
            .Select(note => new Note
            {
                Id = note.Id,
                Pitch = note.Pitch,
                Span = note.Span,
                Velocity = note.Velocity,
                Track = note.Track,
                Hand = note.Hand is { } hand ? new Attributed<Hand>(hand, Attribution.UserEdited) : null,
                Attribution = Attribution.UserEdited,
            });
        return [.. notes.Where(note => !removed.Contains(note.Id)), .. added];
    }

    /// <summary>
    /// Where a played note not printed is written in its bar: its onset on the 16th grid inside the bar, its end
    /// through the alignment on the same grid, at least a 16th long; its staff and hand those of the printed
    /// note nearest in pitch, or by register when the bar has none.
    /// </summary>
    internal static (TickSpan Span, int Track, Hand Hand) Place(PlacedNote note, PlayedNote heard, TickSpan bar, PerformanceAlignment alignment, IReadOnlyList<Note> printed)
    {
        long onset = Math.Clamp(Snap(note.At.Value, bar.Onset.Value), bar.Onset.Value, bar.End.Value - Grid);
        long end = Math.Max(onset + Grid, Snap(alignment.TickAt(heard.Onset + heard.Duration).Value, bar.Onset.Value));
        Note? nearest = printed.OrderBy(other => Math.Abs(other.Pitch.MidiNumber - note.Pitch.MidiNumber)).ThenBy(other => other.Id, StringComparer.Ordinal).FirstOrDefault();
        Hand hand = nearest?.Hand?.Value ?? (note.Pitch.MidiNumber < 60 ? Hand.Left : Hand.Right);
        return (TickSpan.FromBounds(new Tick(onset), new Tick(end)), nearest?.Track ?? 0, hand);
    }

    /// <summary>A tick moved to the nearest 16th from the bar line, halves away from it; integers only.</summary>
    private static long Snap(long tick, long origin)
    {
        long offset = tick - origin;
        long steps = offset >= 0 ? (offset + (Grid / 2)) / Grid : -((-offset + (Grid / 2)) / Grid);
        return origin + (steps * Grid);
    }
}