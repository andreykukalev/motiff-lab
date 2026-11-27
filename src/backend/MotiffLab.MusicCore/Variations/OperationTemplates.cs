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
/// <remarks>
/// One file per family of operations: rhythm, articulation, density and accents (<c>OperationTemplates.Rhythm.cs</c>);
/// the melody's intervals, direction, passing notes and ornaments (<c>.Melody.cs</c>); accompaniment, bass, voicing,
/// simplifying and harmony (<c>.Texture.cs</c>); and a step by hand (<c>.ByHand.cs</c>). This file holds the dispatch
/// and the places, beats and lists every template writes.
/// </remarks>
internal static partial class OperationTemplates
{
    /// <summary>The template's sentence for <paramref name="operation"/>, or null when it has none or the diff does not fit it.</summary>
    /// <param name="operation">A catalogue id; a chain of steps (ids joined by " › ") has no template.</param>
    public static string? Lead(
        string operation,
        IReadOnlyList<NoteChange> changes,
        MotifTexture before,
        MotifTexture after,
        MusicalKey? key,
        MeterMap meter,
        Words words = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(meter);
        return operation switch
        {
            RhythmReshape.OperationId => Anticipated(changes, meter, words) ?? Dotted(changes, meter, words),
            IntervalReshape.OperationId => Reshaped(before, after, key, words),
            ChangeDirection.OperationId => Turned(before, after, key, words),
            HandEdit.OperationId => Edited(changes, before, after, meter, words),
            Articulation.OperationId => Articulated(changes, words),
            AccompanimentPattern.OperationId => Repatterned(before, after, meter, words),
            DensityAndRests.OperationId => Densified(changes, before, after, words),
            BassPattern.OperationId => Rebassed(before, after, meter, key, words),
            Ornament.OperationId => Ornamented(before, after, key, words),
            AccentShift.OperationId => Accented(changes, words),
            VoicingAndRegister.OperationId => Revoiced(changes, before, after, key, meter, words),
            Simplify.OperationId => Simplified(changes, before, after, words),
            PassingNotes.OperationId => Filled(before, after, key, words),
            Reharmonise.OperationId => Reharmonised(changes, before, after, key, meter, words),
            _ => null,
        };
    }

    /// <summary>The notes of <paramref name="after"/> whose ids <paramref name="before"/> does not have: the notes added; the other way round, the notes removed.</summary>
    private static Note[] Added(MotifTexture before, MotifTexture after) =>
        [.. after.Notes.Where(note => !before.Notes.Any(other => other.Id == note.Id))];

    /// <summary>"3 notes", "3 ноты": a count of notes in the language.</summary>
    private static string NotesIn(int count, Words w) => w.Count(count, "note", "notes", "нота", "ноты", "нот");

    /// <summary>"beat 2 of bar 1", or "1 sixteenth after beat 2 of bar 1" off the beat; "доля 2 такта 1", "1 шестнадцатая после доли 2 такта 1".</summary>
    private static string Place(Tick onset, MeterMap meter, Words w)
    {
        MetricPosition position = meter.ToPosition(onset);
        if (w.IsRussian)
        {
            return position.BeatOffset == 0
                ? $"доля {position.Beat} такта {position.Bar}"
                : $"{VariationFacts.Length(position.BeatOffset, w)} после доли {position.Beat} такта {position.Bar}";
        }

        string beat = $"beat {position.Beat} of bar {position.Bar}";
        return position.BeatOffset == 0 ? beat : $"{VariationFacts.Length(position.BeatOffset)} after {beat}";
    }

    /// <summary>
    /// "beat 2 of bar 1", "beats 2, 3 and 4 of bar 1 and beat 1 of bar 2", from the meter, in order;
    /// in a bar with a position off the beat, each is named on its own: "beat 1 of bar 1 and 1 eighth
    /// after beat 1 of bar 1".
    /// </summary>
    public static string Beats(IEnumerable<Tick> onsets, MeterMap meter) => Beats(onsets, meter, Words.English);

    /// <summary>The beats in a language: "beats 2 and 3 of bar 1"; "доли 2 и 3 такта 1".</summary>
    public static string Beats(IEnumerable<Tick> onsets, MeterMap meter, Words words)
    {
        ArgumentNullException.ThrowIfNull(onsets);
        ArgumentNullException.ThrowIfNull(meter);
        Words w = words;
        string[] bars = [.. onsets
            .Distinct()
            .Order()
            .Select(meter.ToPosition)
            .GroupBy(static position => position.Bar)
            .SelectMany(bar =>
            {
                if (bar.Any(static position => position.BeatOffset != 0))
                {
                    return bar.Select(position => position.BeatOffset == 0
                        ? w.T($"beat {position.Beat} of bar {position.Bar}", $"доля {position.Beat} такта {position.Bar}")
                        : w.T(
                            $"{VariationFacts.Length(position.BeatOffset)} after beat {position.Beat} of bar {position.Bar}",
                            $"{VariationFacts.Length(position.BeatOffset, w)} после доли {position.Beat} такта {position.Bar}"));
                }

                int[] beats = [.. bar.Select(static position => position.Beat)];
                return [beats.Length == 1
                    ? w.T($"beat {beats[0]} of bar {bar.Key}", $"доля {beats[0]} такта {bar.Key}")
                    : w.T($"beats {List(beats.Select(static beat => $"{beat}"), w)} of bar {bar.Key}", $"доли {List(beats.Select(static beat => $"{beat}"), w)} такта {bar.Key}")];
            })];
        return List(bars, w);
    }

    private static string List(IEnumerable<string> items, Words w)
    {
        string[] all = [.. items];
        return all.Length <= 1 ? string.Concat(all) : w.Joined(all);
    }
}