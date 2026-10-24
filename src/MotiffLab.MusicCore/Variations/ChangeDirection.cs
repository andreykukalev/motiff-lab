using MotiffLab.MusicCore.Analysis;

namespace MotiffLab.MusicCore.Variations;

/// <summary>The parameters of V10 Change direction.</summary>
/// <param name="Diatonic">
/// True mirrors in steps of the key's scale, so a 3rd up becomes a 3rd down and the melody stays in
/// the key; false mirrors in semitones, so every interval keeps its exact size.
/// </param>
public sealed record ChangeDirectionParameters(bool Diatonic = true) : OperationParameters;

/// <summary>
/// V10 Change direction: mirrors the melody around its first note, so every step up becomes a step
/// down of the same size and the other way round. Rhythm and the other voices stay. Pure and
/// deterministic. Flipping only selected intervals follows when the lab can select intervals.
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

    protected override string? MelodyPrecondition(int[] melody, MusicalKey? key, ChangeDirectionParameters parameters) =>
        melody.All(pitch => pitch == melody[0])
            ? $"{Name} has nothing to turn: the melody repeats one note, so it is not available for this selection."
            : null;

    protected override int[] NewMelody(int[] melody, MusicalKey? key, ChangeDirectionParameters parameters)
    {
        int axis = melody[0];
        if (!parameters.Diatonic)
        {
            return [.. melody.Select(pitch => (2 * axis) - pitch)];
        }

        MusicalKey scale = key!.Value;
        int axisStep = ScaleSteps.Locate(scale, axis).Step;
        return [.. melody.Select(pitch =>
        {
            ScalePosition position = ScaleSteps.Locate(scale, pitch);
            return ScaleSteps.MidiNumber(scale, position with { Step = (2 * axisStep) - position.Step });
        })];
    }
}