using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Variations;

/// <summary>
/// What every operation shares: the motif's notes, the diff, the kept-property checks (S1-E9) and
/// the explanation, which states only facts read from the diff (S1-E4).
/// </summary>
public static class VariationFacts
{
    /// <summary>The selected notes, in the selection's order.</summary>
    public static IReadOnlyList<Note> MotifNotes(IEnumerable<Note> notes, MotifSelection selection)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(selection);
        Dictionary<string, Note> byId = notes.ToDictionary(static note => note.Id, StringComparer.Ordinal);
        return selection.NoteIds.Select(id => byId[id]).ToArray();
    }

    /// <summary>Builds the variation from its diff: ordered notes, checks of the kept properties and the explanation.</summary>
    public static Variation Build(string operation, TickSpan span, IReadOnlyList<NoteChange> changes, IReadOnlySet<KeepProperty> keep)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(keep);
        Note[] before = changes.Select(static change => change.Before).ToArray();
        Note[] after = Ordered(changes.Select(static change => change.After));
        KeepCheck[] checks = Enum.GetValues<KeepProperty>()
            .Where(keep.Contains)
            .Select(property => new KeepCheck(property, Check(property, before, after, span)))
            .ToArray();
        return new Variation(operation, after, changes, checks, Explain(changes));
    }

    /// <summary>"3 quarter notes", "1 eighth", "5 sixteenths", or ticks when the length is finer than that.</summary>
    public static string Length(long ticks)
    {
        (long unit, string name) = ticks switch
        {
            _ when ticks % CanonicalTime.TicksPerQuarter == 0 => (CanonicalTime.TicksPerQuarter, "quarter note"),
            _ when ticks % (CanonicalTime.TicksPerQuarter / 2) == 0 => (CanonicalTime.TicksPerQuarter / 2, "eighth"),
            _ when ticks % (CanonicalTime.TicksPerQuarter / 4) == 0 => (CanonicalTime.TicksPerQuarter / 4, "sixteenth"),
            _ => (1L, "tick"),
        };
        long count = ticks / unit;
        return count == 1 ? $"1 {name}" : $"{count} {name}s";
    }

    private static Note[] Ordered(IEnumerable<Note> notes) => notes
        .OrderBy(static note => note.Span.Onset)
        .ThenBy(static note => note.Pitch.MidiNumber)
        .ThenBy(static note => note.Id, StringComparer.Ordinal)
        .ToArray();

    private static KeepStatus Check(KeepProperty property, Note[] before, Note[] after, TickSpan span)
    {
        if (property is KeepProperty.Melody or KeepProperty.Bass or KeepProperty.Harmony)
        {
            return KeepStatus.NotChecked;
        }

        int[] pitchesBefore = before.Select(static note => note.Pitch.MidiNumber).ToArray();
        int[] pitchesAfter = after.Select(static note => note.Pitch.MidiNumber).ToArray();
        bool kept = property switch
        {
            KeepProperty.Pitches => pitchesBefore.Order().SequenceEqual(pitchesAfter.Order()),
            KeepProperty.PitchOrder => pitchesBefore.SequenceEqual(pitchesAfter),
            KeepProperty.Contour => Steps(pitchesBefore).Select(Math.Sign).SequenceEqual(Steps(pitchesAfter).Select(Math.Sign)),
            KeepProperty.Intervals => Steps(pitchesBefore).SequenceEqual(Steps(pitchesAfter)),
            KeepProperty.Rhythm => Rhythm(before, span).SequenceEqual(Rhythm(after, span)),
            KeepProperty.Register => pitchesBefore.Length == pitchesAfter.Length
                && (pitchesBefore.Length == 0 || (pitchesBefore.Min() == pitchesAfter.Min() && pitchesBefore.Max() == pitchesAfter.Max())),
            _ => throw new ArgumentOutOfRangeException(nameof(property), property, "Unknown property."),
        };
        return kept ? KeepStatus.Kept : KeepStatus.NoLongerKept;
    }

    private static IEnumerable<int> Steps(int[] pitches) => pitches.Zip(pitches.Skip(1), static (a, b) => b - a);

    private static IEnumerable<(long Offset, long Duration)> Rhythm(Note[] notes, TickSpan span) =>
        notes.Select(note => (note.Span.Onset.Value - span.Onset.Value, note.Span.Duration));

    private static string Explain(IReadOnlyList<NoteChange> changes)
    {
        int count = changes.Count;
        if (count == 0)
        {
            return "The selection has no notes, so nothing changed.";
        }

        var sentences = new List<string>();
        int pitchChanges = changes.Count(static change => change.PitchChanged);
        sentences.Add(pitchChanges == 0 ? "The pitch sequence is unchanged." : $"{pitchChanges} of {count} notes change pitch.");

        int lengthChanges = changes.Count(static change => change.DurationChanged);
        if (changes.All(static change => change.After.Span.Duration == change.Before.Span.Duration * 2))
        {
            sentences.Add($"{Every(count, "lasts twice as long", "last twice as long")}.");
        }
        else if (changes.All(static change => change.After.Span.Duration * 2 == change.Before.Span.Duration))
        {
            sentences.Add($"{Every(count, "lasts half as long", "last half as long")}.");
        }
        else
        {
            sentences.Add(lengthChanges == 0 ? "Every note keeps its length." : $"{lengthChanges} of {count} notes change length.");
        }

        long[] shifts = changes.Select(static change => change.After.Span.Onset.Value - change.Before.Span.Onset.Value).ToArray();
        int moved = shifts.Count(static shift => shift != 0);
        long[] gapsBefore = Gaps(changes.Select(static change => change.Before));
        long[] gapsAfter = Gaps(changes.Select(static change => change.After));
        if (moved == 0)
        {
            sentences.Add("Every note starts where it did.");
        }
        else if (shifts.Distinct().Count() == 1)
        {
            long shift = shifts[0];
            sentences.Add($"{Every(count, "starts", "start")} {Length(Math.Abs(shift))} {(shift > 0 ? "later" : "earlier")}.");
        }
        else if (gapsBefore.Length > 0 && gapsBefore.Zip(gapsAfter).All(static gap => gap.Second == gap.First * 2))
        {
            sentences.Add($"The time between onsets doubles, so {Moved(moved, count)}.");
        }
        else if (gapsBefore.Length > 0 && gapsBefore.Zip(gapsAfter).All(static gap => gap.Second * 2 == gap.First))
        {
            sentences.Add($"The time between onsets halves, so {Moved(moved, count)}.");
        }
        else
        {
            sentences.Add($"{moved} of {count} notes start at a new place.");
        }

        long lengthBefore = Extent(changes.Select(static change => change.Before));
        long lengthAfter = Extent(changes.Select(static change => change.After));
        if (lengthBefore != lengthAfter)
        {
            sentences.Add($"From its first onset to its last note's end, the motif now takes {Length(lengthAfter)} instead of {Length(lengthBefore)}.");
        }

        return string.Join(" ", sentences);
    }

    private static string Every(int count, string one, string many) => count == 1 ? $"The note {one}" : $"All {count} notes {many}";

    private static string Moved(int moved, int count) => moved == 1 ? $"1 of {count} notes moves" : $"{moved} of {count} notes move";

    private static long[] Gaps(IEnumerable<Note> notes)
    {
        long[] onsets = notes.Select(static note => note.Span.Onset.Value).ToArray();
        return onsets.Zip(onsets.Skip(1), static (a, b) => b - a).ToArray();
    }

    private static long Extent(IEnumerable<Note> notes)
    {
        Note[] all = notes.ToArray();
        return all.Max(static note => note.Span.End.Value) - all.Min(static note => note.Span.Onset.Value);
    }
}