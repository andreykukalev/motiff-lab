using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Variations;

/// <summary>
/// What every operation and every chain of steps shares: the motif's notes, the diff, the
/// kept-property checks (S1-E9) and the explanation, which states only facts read from the diff (S1-E4).
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

    /// <summary>
    /// Builds the variation from the notes before and after: the diff by note id, ordered notes,
    /// checks of the kept properties against <paramref name="before"/>, and the explanation.
    /// </summary>
    public static Variation Build(
        string operation,
        TickSpan span,
        IReadOnlyList<Note> before,
        IEnumerable<Note> after,
        IReadOnlySet<KeepProperty> keep,
        IReadOnlyList<string> flags)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(keep);
        ArgumentNullException.ThrowIfNull(flags);
        Note[] ordered = Ordered(after);
        Dictionary<string, Note> afterById = ordered.ToDictionary(static note => note.Id, StringComparer.Ordinal);
        var beforeIds = before.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        NoteChange[] changes = before
            .Where(note => afterById.ContainsKey(note.Id))
            .Select(note => new NoteChange(note, afterById[note.Id]))
            .ToArray();
        Note[] added = ordered.Where(note => !beforeIds.Contains(note.Id)).ToArray();
        Note[] removed = before.Where(note => !afterById.ContainsKey(note.Id)).ToArray();
        Note[] source = [.. before];
        var textures = (Before: MotifTexture.Read(source), After: MotifTexture.Read(ordered));
        KeepCheck[] properties = Enum.GetValues<KeepProperty>()
            .Select(property => new KeepCheck(property, Check(property, source, ordered, span, textures.Before, textures.After)))
            .ToArray();
        KeepCheck[] checks = properties.Where(check => keep.Contains(check.Property)).ToArray();
        string explanation = Explain(changes, added, removed, source, ordered);
        string unchanged = Unchanged(properties, textures.Before);
        if (changes.Length + added.Length + removed.Length > 0 && unchanged.Length > 0)
        {
            explanation = $"{explanation} {unchanged}";
        }

        return new Variation(operation, ordered, changes, added, removed, checks, properties, flags, explanation);
    }

    /// <summary>
    /// "Unchanged: pitches, pitch order and register.", naming every property the diff shows the step
    /// left as it was; the bass is named only when the source has one apart from its melody.
    /// </summary>
    private static string Unchanged(IReadOnlyList<KeepCheck> properties, MotifTexture before)
    {
        string[] names = [.. properties
            .Where(check => check.Status == KeepStatus.Kept && (check.Property != KeepProperty.Bass || !before.IsSingleLine))
            .Select(check => check.Property switch
            {
                KeepProperty.PitchOrder => "pitch order",
                KeepProperty.Melody => "melody pitches",
                KeepProperty.Bass => "bass pitches",
                _ => check.Property.ToString().ToLowerInvariant(),
            })];
        return names.Length switch
        {
            0 => "",
            1 => $"Unchanged: {names[0]}.",
            _ => $"Unchanged: {string.Join(", ", names[..^1])} and {names[^1]}.",
        };
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

    /// <remarks>
    /// Pitches, pitch order and register read every note. Contour and intervals read the melody, as
    /// the melody facts do (S1-A2), so the notes of a chord under it are not melodic steps. Melody
    /// and bass are kept when their lines keep their pitches in order; their rhythm may change
    /// (the Keep table's footnote 1).
    /// </remarks>
    private static KeepStatus Check(KeepProperty property, Note[] before, Note[] after, TickSpan span, MotifTexture textureBefore, MotifTexture textureAfter)
    {
        if (property is KeepProperty.Harmony)
        {
            return KeepStatus.NotChecked;
        }

        int[] pitchesBefore = before.Select(static note => note.Pitch.MidiNumber).ToArray();
        int[] pitchesAfter = after.Select(static note => note.Pitch.MidiNumber).ToArray();
        int[] melodyBefore = Pitches(textureBefore.Melody);
        int[] melodyAfter = Pitches(textureAfter.Melody);
        bool kept = property switch
        {
            KeepProperty.Pitches => pitchesBefore.Order().SequenceEqual(pitchesAfter.Order()),
            KeepProperty.PitchOrder => pitchesBefore.SequenceEqual(pitchesAfter),
            KeepProperty.Contour => Steps(melodyBefore).Select(Math.Sign).SequenceEqual(Steps(melodyAfter).Select(Math.Sign)),
            KeepProperty.Intervals => Steps(melodyBefore).SequenceEqual(Steps(melodyAfter)),
            KeepProperty.Melody => melodyBefore.SequenceEqual(melodyAfter),
            KeepProperty.Bass => Pitches(textureBefore.Bass).SequenceEqual(Pitches(textureAfter.Bass)),
            KeepProperty.Rhythm => Rhythm(before, span).SequenceEqual(Rhythm(after, span)),
            KeepProperty.Register => pitchesBefore.Length == pitchesAfter.Length
                && (pitchesBefore.Length == 0 || (pitchesBefore.Min() == pitchesAfter.Min() && pitchesBefore.Max() == pitchesAfter.Max())),
            _ => throw new ArgumentOutOfRangeException(nameof(property), property, "Unknown property."),
        };
        return kept ? KeepStatus.Kept : KeepStatus.NoLongerKept;
    }

    private static int[] Pitches(IReadOnlyList<Note> line) => line.Select(static note => note.Pitch.MidiNumber).ToArray();

    private static IEnumerable<int> Steps(int[] pitches) => pitches.Zip(pitches.Skip(1), static (a, b) => b - a);

    private static IEnumerable<(long Offset, long Duration)> Rhythm(Note[] notes, TickSpan span) =>
        notes.Select(note => (note.Span.Onset.Value - span.Onset.Value, note.Span.Duration));

    private static string Explain(IReadOnlyList<NoteChange> changes, Note[] added, Note[] removed, Note[] before, Note[] after)
    {
        int count = changes.Count;
        if (count == 0 && added.Length == 0 && removed.Length == 0)
        {
            return "The selection has no notes, so nothing changed.";
        }

        var sentences = new List<string>();
        if (added.Length > 0)
        {
            sentences.Add(added.Length == 1 ? "1 note is added." : $"{added.Length} notes are added.");
        }

        if (removed.Length > 0)
        {
            sentences.Add(removed.Length == 1 ? "1 note is removed." : $"{removed.Length} notes are removed.");
        }

        if (count == 0)
        {
            return string.Join(" ", sentences);
        }

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

        long lengthBefore = Extent(before);
        long lengthAfter = Extent(after);
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

    private static long Extent(Note[] notes) =>
        notes.Length == 0 ? 0 : notes.Max(static note => note.Span.End.Value) - notes.Min(static note => note.Span.Onset.Value);
}