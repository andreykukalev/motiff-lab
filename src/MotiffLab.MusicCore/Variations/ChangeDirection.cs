using MotiffLab.MusicCore.Analysis;

namespace MotiffLab.MusicCore.Variations;

/// <summary>The parameters of V10 Change direction.</summary>
/// <param name="Diatonic">
/// True mirrors in steps of the key's scale, so a 3rd up becomes a 3rd down and the melody stays in
/// the key; false mirrors in semitones, so every interval keeps its exact size.
/// </param>
/// <param name="Intervals">The melody intervals to flip; null flips them all, which mirrors the whole melody around its first note.</param>
public sealed record ChangeDirectionParameters(bool Diatonic = true, IntervalSet? Intervals = null) : OperationParameters;

/// <summary>
/// V10 Change direction: mirrors the melody around its first note, so every step up becomes a step
/// down of the same size and the other way round; or flips only the picked intervals, and the notes
/// after each flipped interval move with it, so every other interval keeps its size and direction.
/// The first note is the fixed axis. Rhythm and the other voices stay. Pure and deterministic.
/// </summary>
public sealed class ChangeDirection : MelodyOperation<ChangeDirectionParameters>
{
    public const string OperationId = "V10";

    private ChangeDirection()
    {
    }

    public static ChangeDirection Instance { get; } = new();

    public override string Id => OperationId;

    public override string Name => "Change direction";

    protected override void Validate(ChangeDirectionParameters parameters)
    {
    }

    protected override bool IsDiatonic(ChangeDirectionParameters parameters) => parameters.Diatonic;

    protected override IntervalSet? Intervals(ChangeDirectionParameters parameters) => parameters.Intervals;

    protected override string? MelodyPrecondition(int[] melody, MusicalKey? key, ChangeDirectionParameters parameters) =>
        Enumerable.Range(0, melody.Length - 1).All(i => !Changes(parameters, i) || melody[i + 1] == melody[i])
            ? $"{Name} has nothing to turn: {(parameters.Intervals is null ? "the melody repeats one note" : "the picked intervals repeat a note")}, so it is not available for this selection."
            : null;

    /// <remarks>Walks the intervals from the first note, turning each picked one over; with every interval picked this mirrors the melody around its first note.</remarks>
    protected override int[] NewMelody(int[] melody, MusicalKey? key, ChangeDirectionParameters parameters)
    {
        var result = new int[melody.Length];
        result[0] = melody[0];
        if (!parameters.Diatonic)
        {
            for (int i = 1; i < melody.Length; i++)
            {
                int interval = melody[i] - melody[i - 1];
                result[i] = result[i - 1] + (Changes(parameters, i - 1) ? -interval : interval);
            }

            return result;
        }

        MusicalKey scale = key!.Value;
        ScalePosition[] positions = [.. melody.Select(pitch => ScaleSteps.Locate(scale, pitch))];
        int step = positions[0].Step;
        for (int i = 1; i < melody.Length; i++)
        {
            int interval = positions[i].Step - positions[i - 1].Step;
            step += Changes(parameters, i - 1) ? -interval : interval;
            result[i] = ScaleSteps.MidiNumber(scale, positions[i] with { Step = step });
        }

        return result;
    }
}