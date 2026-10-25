using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Variations;

/// <summary>
/// The opening sentence of a step's explanation, one template per operation (S1-E4). Each template
/// reads only the diff, the key and the meter, never the parameters the step was asked for, so it
/// states what happened to the notes; when the diff does not have the template's shape it says
/// nothing and the shared sentences of <see cref="VariationFacts"/> stand alone.
/// </summary>
public static class OperationTemplates
{
    /// <summary>The template's sentence for <paramref name="operation"/>, or null when it has none or the diff does not fit it.</summary>
    /// <param name="operation">A catalogue id; a chain of steps (ids joined by " › ") has no template.</param>
    public static string? Lead(
        string operation,
        IReadOnlyList<NoteChange> changes,
        MotifTexture before,
        MotifTexture after,
        MusicalKey? key,
        MeterMap meter)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(meter);
        return operation switch
        {
            RhythmReshape.OperationId => Anticipated(changes, meter) ?? Dotted(changes, meter),
            IntervalReshape.OperationId => Reshaped(before, after, key),
            ChangeDirection.OperationId => Turned(before, after, key),
            HandEdit.OperationId => Edited(changes, before, after, meter),
            Articulation.OperationId => Articulated(changes),
            _ => null,
        };
    }

    /// <summary>
    /// "By hand, E5 at beat 2 of bar 1 is removed.", "By hand, F5 is added at beat 3 of bar 1, lasting
    /// 1 eighth.", or "By hand, E5 at beat 2 of bar 1 changes: pitch to F5, velocity from 80 to 90.",
    /// when exactly one note was removed, added or changed.
    /// </summary>
    private static string? Edited(IReadOnlyList<NoteChange> changes, MotifTexture before, MotifTexture after, MeterMap meter)
    {
        Note[] removed = [.. before.Notes.Where(note => !after.Notes.Any(other => other.Id == note.Id))];
        Note[] added = [.. after.Notes.Where(note => !before.Notes.Any(other => other.Id == note.Id))];
        NoteChange[] changed = [.. changes.Where(static change => NoteValues.Of(change.Before) != NoteValues.Of(change.After))];
        return (removed.Length, added.Length, changed.Length) switch
        {
            (1, 0, 0) => $"By hand, {removed[0].Pitch} at {Place(removed[0].Span.Onset, meter)} is removed.",
            (0, 1, 0) => $"By hand, {added[0].Pitch} is added at {Place(added[0].Span.Onset, meter)}, lasting {VariationFacts.Length(added[0].Span.Duration)}.",
            (0, 0, 1) => $"By hand, {changed[0].Before.Pitch} at {Place(changed[0].Before.Span.Onset, meter)} changes: {string.Join(", ", Edits(changed[0], meter))}.",
            _ => null,
        };
    }

    private static IEnumerable<string> Edits(NoteChange change, MeterMap meter)
    {
        if (change.PitchChanged)
        {
            yield return $"pitch to {change.After.Pitch}";
        }

        if (change.OnsetChanged)
        {
            yield return $"start to {Place(change.After.Span.Onset, meter)}";
        }

        if (change.DurationChanged)
        {
            yield return $"length from {VariationFacts.Length(change.Before.Span.Duration)} to {VariationFacts.Length(change.After.Span.Duration)}";
        }

        if (change.Before.Velocity != change.After.Velocity)
        {
            yield return $"velocity from {change.Before.Velocity} to {change.After.Velocity}";
        }
    }

    /// <summary>
    /// "Only note lengths change: 9 notes sound shorter.", or "… 4 notes sound longer.", when every
    /// note that changed kept its pitch and onset and all of them changed length the same way.
    /// </summary>
    private static string? Articulated(IReadOnlyList<NoteChange> changes)
    {
        NoteChange[] changed = [.. changes.Where(static change => change.DurationChanged)];
        if (changed.Length == 0 || changes.Any(static change => change.PitchChanged || change.OnsetChanged))
        {
            return null;
        }

        string notes = changed.Length == 1 ? "1 note sounds" : $"{changed.Length} notes sound";
        return changed.All(static change => change.After.Span.Duration < change.Before.Span.Duration) ? $"Only note lengths change: {notes} shorter."
            : changed.All(static change => change.After.Span.Duration > change.Before.Span.Duration) ? $"Only note lengths change: {notes} longer."
            : null;
    }

    /// <summary>"beat 2 of bar 1", or "1 sixteenth after beat 2 of bar 1" off the beat.</summary>
    private static string Place(Tick onset, MeterMap meter)
    {
        MetricPosition position = meter.ToPosition(onset);
        string beat = $"beat {position.Beat} of bar {position.Bar}";
        return position.BeatOffset == 0 ? beat : $"{VariationFacts.Length(position.BeatOffset)} after {beat}";
    }

    /// <summary>
    /// "3 notes that started on a beat now start 1 eighth ahead of it: beats 2, 3 and 4 of bar 1.",
    /// when every note that moved started on a beat and now starts half that beat earlier.
    /// </summary>
    private static string? Anticipated(IReadOnlyList<NoteChange> changes, MeterMap meter)
    {
        NoteChange[] moved = [.. changes.Where(static change => change.OnsetChanged)];
        if (moved.Length == 0 || !moved.All(change => AheadOfBeat(change, meter)))
        {
            return null;
        }

        long[] early = [.. moved.Select(static change => change.Before.Span.Onset.Value - change.After.Span.Onset.Value).Distinct()];
        string ahead = early.Length == 1 ? $"{VariationFacts.Length(early[0])} ahead of it" : "half a beat ahead of it";
        string notes = moved.Length == 1 ? "1 note that started on a beat now starts" : $"{moved.Length} notes that started on a beat now start";
        return $"{notes} {ahead}: {Beats(moved.Select(static change => change.Before.Span.Onset), meter)}.";
    }

    private static bool AheadOfBeat(NoteChange change, MeterMap meter)
    {
        Tick onset = change.Before.Span.Onset;
        long beat = meter.SignatureAt(onset).BeatTicks;
        return meter.ToPosition(onset).BeatOffset == 0 && beat % 2 == 0 && change.After.Span.Onset.Value == onset.Value - (beat / 2);
    }

    /// <summary>
    /// "2 pairs of equal notes become long–short, 3 sixteenths then 1 sixteenth, starting on beats 1
    /// and 3 of bar 1.", when every note that changed is in a pair a length apart: the first now
    /// sounds half that length longer, and the second starts half that length later.
    /// </summary>
    private static string? Dotted(IReadOnlyList<NoteChange> changes, MeterMap meter)
    {
        NoteChange[] changed = [.. changes.Where(static change => change.OnsetChanged || change.DurationChanged)];
        NoteChange[] firsts = [.. changed.Where(static change => !change.OnsetChanged && change.After.Span.Duration > change.Before.Span.Duration)];
        NoteChange[] seconds = [.. changed.Where(static change => change.After.Span.Onset > change.Before.Span.Onset && change.After.Span.Duration <= change.Before.Span.Duration)];
        if (firsts.Length == 0 || firsts.Length + seconds.Length != changed.Length)
        {
            return null;
        }

        static long Grown(NoteChange first) => first.After.Span.Duration - first.Before.Span.Duration;
        static long Shift(NoteChange second) => second.After.Span.Onset.Value - second.Before.Span.Onset.Value;
        bool paired = firsts.All(first => seconds.Any(second => Shift(second) == Grown(first) && second.Before.Span.Onset.Value == first.Before.Span.Onset.Value + (2 * Grown(first))))
            && seconds.All(second => firsts.Any(first => Grown(first) == Shift(second) && first.Before.Span.Onset.Value == second.Before.Span.Onset.Value - (2 * Shift(second))));
        if (!paired)
        {
            return null;
        }

        Tick[] starts = [.. firsts.Select(static change => change.Before.Span.Onset).Distinct().Order()];
        long[] lengths = [.. firsts.Select(static change => 2 * Grown(change)).Distinct()];
        string shape = lengths.Length == 1
            ? $"{VariationFacts.Length(lengths[0] * 3 / 2)} then {VariationFacts.Length(lengths[0] / 2)}"
            : "the first of each pair 3 times as long as the second";
        string pairs = starts.Length == 1 ? "1 pair of equal notes becomes" : $"{starts.Length} pairs of equal notes become";
        return $"{pairs} long–short, {shape}, starting on {Beats(starts, meter)}.";
    }

    /// <summary>
    /// "4 of 6 melody intervals are 1 scale step wider in the key's scale, keeping their direction.",
    /// when the melody kept its notes and every interval that changed size in scale steps grew or
    /// shrank by the same number of steps without turning.
    /// </summary>
    private static string? Reshaped(MotifTexture before, MotifTexture after, MusicalKey? key)
    {
        if (key is not { } scale || ScaleIntervals(before, after, scale) is not { } intervals)
        {
            return null;
        }

        (int From, int To)[] changed = [.. intervals.Where(static interval => interval.From != interval.To)];
        if (changed.Length == 0 || changed.Any(static interval => Math.Sign(interval.From) != Math.Sign(interval.To)))
        {
            return null;
        }

        int[] growth = [.. changed.Select(static interval => Math.Abs(interval.To) - Math.Abs(interval.From)).Distinct()];
        if (growth.Length != 1 || growth[0] == 0)
        {
            return null;
        }

        string steps = Math.Abs(growth[0]) == 1 ? "1 scale step" : $"{Math.Abs(growth[0])} scale steps";
        string which = changed.Length == intervals.Length
            ? intervals.Length == 1 ? "The melody interval is" : $"All {intervals.Length} melody intervals are"
            : $"{changed.Length} of {intervals.Length} melody intervals {(changed.Length == 1 ? "is" : "are")}";
        return $"{which} {steps} {(growth[0] > 0 ? "wider" : "narrower")} in the key's scale, keeping {(changed.Length == 1 ? "its" : "their")} direction.";
    }

    /// <summary>
    /// "The melody is mirrored around its first note, E5: every interval turns the other way, keeping
    /// its size in steps of the key's scale.", or "2 of 6 melody intervals turn the other way, …",
    /// when the melody kept its notes and every interval that changed is the old one turned over, in
    /// semitones or else in scale steps.
    /// </summary>
    private static string? Turned(MotifTexture before, MotifTexture after, MusicalKey? key)
    {
        if (SemitoneIntervals(before, after) is not { } semitones)
        {
            return null;
        }

        string? size = Flips(semitones) ? "semitones" : null;
        (int From, int To)[] intervals = semitones;
        if (size is null && key is { } scale && ScaleIntervals(before, after, scale) is { } steps && Flips(steps))
        {
            size = "steps of the key's scale";
            intervals = steps;
        }

        if (size is null)
        {
            return null;
        }

        int moving = intervals.Count(static interval => interval.From != 0);
        int turned = intervals.Count(static interval => interval.From != 0 && interval.To == -interval.From);
        if (turned == moving && before.Melody[0].Pitch == after.Melody[0].Pitch)
        {
            return $"The melody is mirrored around its first note, {before.Melody[0].Pitch}: every interval turns the other way, keeping its size in {size}.";
        }

        string which = turned == 1 ? $"1 of {intervals.Length} melody intervals turns" : $"{turned} of {intervals.Length} melody intervals turn";
        return $"{which} the other way, keeping {(turned == 1 ? "its" : "their")} size in {size}.";
    }

    /// <summary>Whether at least one interval turned over and every other interval stayed as it was.</summary>
    private static bool Flips((int From, int To)[] intervals) =>
        intervals.Any(static interval => interval.From != 0 && interval.To == -interval.From)
        && intervals.All(static interval => interval.To == interval.From || interval.To == -interval.From);

    /// <summary>The melody's intervals in semitones before and after, or null when the melody's notes changed or it has fewer than two.</summary>
    private static (int From, int To)[]? SemitoneIntervals(MotifTexture before, MotifTexture after) =>
        SameMelody(before, after) ? Pair(before, after, static note => note.Pitch.MidiNumber) : null;

    /// <summary>The melody's intervals in steps of the key's scale before and after, or null as for <see cref="SemitoneIntervals"/>.</summary>
    private static (int From, int To)[]? ScaleIntervals(MotifTexture before, MotifTexture after, MusicalKey key) =>
        SameMelody(before, after) ? Pair(before, after, note => ScaleSteps.Locate(key, note.Pitch.MidiNumber).Step) : null;

    private static bool SameMelody(MotifTexture before, MotifTexture after) =>
        before.Melody.Count >= 2
        && before.Melody.Select(static note => note.Id).SequenceEqual(after.Melody.Select(static note => note.Id), StringComparer.Ordinal);

    private static (int From, int To)[] Pair(MotifTexture before, MotifTexture after, Func<Note, int> measure)
    {
        int[] from = [.. before.Melody.Select(measure)];
        int[] to = [.. after.Melody.Select(measure)];
        return [.. from.Zip(from.Skip(1), static (a, b) => b - a).Zip(to.Zip(to.Skip(1), static (a, b) => b - a))];
    }

    /// <summary>
    /// "beat 2 of bar 1", "beats 2, 3 and 4 of bar 1 and beat 1 of bar 2", from the meter, in order;
    /// in a bar with a position off the beat, each is named on its own: "beat 1 of bar 1 and 1 eighth
    /// after beat 1 of bar 1".
    /// </summary>
    public static string Beats(IEnumerable<Tick> onsets, MeterMap meter)
    {
        ArgumentNullException.ThrowIfNull(onsets);
        ArgumentNullException.ThrowIfNull(meter);
        string[] bars = [.. onsets
            .Distinct()
            .Order()
            .Select(meter.ToPosition)
            .GroupBy(static position => position.Bar)
            .SelectMany(static bar =>
            {
                if (bar.Any(static position => position.BeatOffset != 0))
                {
                    return bar.Select(static position => position.BeatOffset == 0
                        ? $"beat {position.Beat} of bar {position.Bar}"
                        : $"{VariationFacts.Length(position.BeatOffset)} after beat {position.Beat} of bar {position.Bar}");
                }

                int[] beats = [.. bar.Select(static position => position.Beat)];
                return [beats.Length == 1 ? $"beat {beats[0]} of bar {bar.Key}" : $"beats {List(beats.Select(static beat => $"{beat}"))} of bar {bar.Key}"];
            })];
        return List(bars);
    }

    private static string List(IEnumerable<string> items)
    {
        string[] all = [.. items];
        return all.Length <= 1 ? string.Concat(all) : $"{string.Join(", ", all[..^1])} and {all[^1]}";
    }
}