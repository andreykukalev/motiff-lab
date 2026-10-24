using MotiffLab.MusicCore.Analysis;

namespace MotiffLab.MusicCore.Variations;

/// <summary>The parameters of V9 Interval reshape.</summary>
/// <param name="Steps">Scale steps added to each changed melody interval: 1 or 2 widens, −1 or −2 narrows.</param>
/// <param name="Intervals">The melody intervals to reshape; null reshapes them all.</param>
public sealed record IntervalReshapeParameters(int Steps, IntervalSet? Intervals = null) : OperationParameters
{
    public const int MaxSteps = 2;
}

/// <summary>
/// V9 Interval reshape: widens or narrows every melody interval, or the picked ones, by 1–2 steps of
/// the key's scale and keeps its direction, so the contour stays. A repeated note stays repeated, and
/// narrowing stops at a 2nd so no step turns into a repeat. Intervals left out keep their size in
/// scale steps. Rhythm and the other voices stay. Pure and deterministic.
/// </summary>
public sealed class IntervalReshape : MelodyOperation<IntervalReshapeParameters>
{
    public const string OperationId = "V9";

    private IntervalReshape()
    {
    }

    public static IntervalReshape Instance { get; } = new();

    public override string Id => OperationId;

    public override string Name => "Interval reshape";

    protected override void Validate(IntervalReshapeParameters parameters)
    {
        if (parameters.Steps == 0 || Math.Abs(parameters.Steps) > IntervalReshapeParameters.MaxSteps)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), parameters.Steps, "Interval reshape widens or narrows by 1–2 scale steps.");
        }
    }

    protected override bool IsDiatonic(IntervalReshapeParameters parameters) => true;

    protected override IntervalSet? Intervals(IntervalReshapeParameters parameters) => parameters.Intervals;

    protected override string? MelodyPrecondition(int[] melody, MusicalKey? key, IntervalReshapeParameters parameters)
    {
        int[] steps = [.. Steps(melody, key!.Value).Where((_, i) => Changes(parameters, i))];
        if (steps.All(step => step == 0))
        {
            return $"{Name} has no interval to reshape: {(parameters.Intervals is null ? "the melody repeats one note" : "the picked intervals repeat a note")}, so it is not available for this selection.";
        }

        return parameters.Steps < 0 && steps.All(step => Math.Abs(step) <= 1)
            ? $"{Name} cannot narrow {(parameters.Intervals is null ? "a melody that moves" : "intervals that move")} only by 2nds without turning steps into repeats, so it is not available for this selection."
            : null;
    }

    protected override int[] NewMelody(int[] melody, MusicalKey? key, IntervalReshapeParameters parameters)
    {
        MusicalKey scale = key!.Value;
        ScalePosition[] positions = [.. melody.Select(pitch => ScaleSteps.Locate(scale, pitch))];
        var result = new int[melody.Length];
        result[0] = melody[0];
        int step = positions[0].Step;
        for (int i = 1; i < melody.Length; i++)
        {
            int interval = positions[i].Step - positions[i - 1].Step;
            step += interval == 0 || !Changes(parameters, i - 1) ? interval : Math.Sign(interval) * Math.Max(1, Math.Abs(interval) + parameters.Steps);
            result[i] = interval == 0 && positions[i].Alteration == positions[i - 1].Alteration
                ? result[i - 1]
                : ScaleSteps.MidiNumber(scale, positions[i] with { Step = step });
        }

        return result;
    }

    private static int[] Steps(int[] melody, MusicalKey key) =>
        [.. melody.Zip(melody.Skip(1), (from, to) => ScaleSteps.Locate(key, to).Step - ScaleSteps.Locate(key, from).Step)];
}