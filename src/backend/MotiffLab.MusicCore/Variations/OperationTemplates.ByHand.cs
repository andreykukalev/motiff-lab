using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Variations;

public static partial class OperationTemplates
{
    /// <summary>
    /// "By hand, E5 at beat 2 of bar 1 is removed.", "By hand, F5 is added at beat 3 of bar 1, lasting
    /// 1 eighth.", or "By hand, E5 at beat 2 of bar 1 changes: pitch to F5, velocity from 80 to 90.",
    /// when exactly one note was removed, added or changed.
    /// </summary>
    private static string? Edited(IReadOnlyList<NoteChange> changes, MotifTexture before, MotifTexture after, MeterMap meter, Words w)
    {
        Note[] removed = Added(after, before);
        Note[] added = Added(before, after);
        NoteChange[] changed = [.. changes.Where(static change => NoteValues.Of(change.Before) != NoteValues.Of(change.After))];
        return (removed.Length, added.Length, changed.Length) switch
        {
            (1, 0, 0) => w.T(
                $"By hand, {removed[0].Pitch} at {Place(removed[0].Span.Onset, meter, w)} is removed.",
                $"Вручную удалена нота {removed[0].Pitch} ({Place(removed[0].Span.Onset, meter, w)})."),
            (0, 1, 0) => w.T(
                $"By hand, {added[0].Pitch} is added at {Place(added[0].Span.Onset, meter, w)}, lasting {VariationFacts.Length(added[0].Span.Duration)}.",
                $"Вручную добавлена нота {added[0].Pitch} ({Place(added[0].Span.Onset, meter, w)}), длина: {VariationFacts.Length(added[0].Span.Duration, w)}."),
            (0, 0, 1) => w.T(
                $"By hand, {changed[0].Before.Pitch} at {Place(changed[0].Before.Span.Onset, meter, w)} changes: {string.Join(", ", Edits(changed[0], meter, w))}.",
                $"Вручную изменена нота {changed[0].Before.Pitch} ({Place(changed[0].Before.Span.Onset, meter, w)}): {string.Join(", ", Edits(changed[0], meter, w))}."),
            _ => null,
        };
    }

    private static IEnumerable<string> Edits(NoteChange change, MeterMap meter, Words w)
    {
        if (change.PitchChanged)
        {
            yield return w.T($"pitch to {change.After.Pitch}", $"высота: {change.After.Pitch}");
        }

        if (change.OnsetChanged)
        {
            yield return w.T($"start to {Place(change.After.Span.Onset, meter, w)}", $"начало: {Place(change.After.Span.Onset, meter, w)}");
        }

        if (change.DurationChanged)
        {
            yield return w.T(
                $"length from {VariationFacts.Length(change.Before.Span.Duration)} to {VariationFacts.Length(change.After.Span.Duration)}",
                $"длина: {VariationFacts.Length(change.Before.Span.Duration, w)} → {VariationFacts.Length(change.After.Span.Duration, w)}");
        }

        if (change.Before.Velocity != change.After.Velocity)
        {
            yield return w.T($"velocity from {change.Before.Velocity} to {change.After.Velocity}", $"громкость: {change.Before.Velocity} → {change.After.Velocity}");
        }
    }
}