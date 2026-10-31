using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Variations;

/// <summary>The parameters of V4 Melodic sequence.</summary>
/// <param name="Steps">How far each copy moves from the one before, in steps of the key's scale: −4 to +4, never 0.</param>
/// <param name="Repeats">How many copies follow the motif, 1–3.</param>
/// <param name="Diatonic">
/// True moves every note by <paramref name="Steps"/> scale steps, so the copies stay in the key and
/// keep their interval sizes in scale steps (the Keep table's footnote 2); false moves every note by
/// the semitones that take the first melody note <paramref name="Steps"/> scale steps, so each copy
/// keeps every interval exactly.
/// </param>
public sealed record MelodicSequenceParameters(int Steps = 1, int Repeats = 1, bool Diatonic = true) : OperationParameters
{
    public const int MaxSteps = 4;

    public const int MinRepeats = 1;

    public const int MaxRepeats = 3;
}

/// <summary>
/// V4 Melodic sequence: the motif plays as it was, then repeats 1–3 times, each copy moved the same
/// distance from the one before and keeping the motif's rhythm. Copies follow each other a whole
/// number of the selection's spans apart (<see cref="VariationFacts.Period"/>), so a one-bar motif
/// repeats in the next bar. Every note moves, accompaniment included, so the texture keeps its shape
/// in each copy. Pure and deterministic.
/// </summary>
/// <remarks>
/// Added notes take the id of the note they copy, a "~" and the copy's number ("n_0001~2"), with
/// more of the suffix when that id is already taken, so ids stay unique when sequences are chained.
/// </remarks>
public sealed class MelodicSequence : VariationOperation<MelodicSequenceParameters>
{
    public const string OperationId = "V4";

    private MelodicSequence()
    {
    }

    public static MelodicSequence Instance { get; } = new();

    public override string Id => OperationId;

    public override string Name => "Melodic sequence";

    protected override void Validate(MelodicSequenceParameters parameters)
    {
        if (parameters.Steps == 0 || Math.Abs(parameters.Steps) > MelodicSequenceParameters.MaxSteps)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), parameters.Steps, "Melodic sequence moves by −4 to +4 scale steps, never 0.");
        }

        if (parameters.Repeats is < MelodicSequenceParameters.MinRepeats or > MelodicSequenceParameters.MaxRepeats)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), parameters.Repeats, "Melodic sequence repeats the motif 1–3 times.");
        }
    }

    /// <remarks>Exact sequences need the key too: it says how many semitones the scale steps are from the first melody note.</remarks>
    protected override string? Precondition(IReadOnlyList<Note> notes, VariationContext context, MelodicSequenceParameters parameters) =>
        context.Key is null
            ? NoKey(context.Words)
            : null;

    protected override IEnumerable<Note> Transform(IReadOnlyList<Note> notes, VariationContext context, MelodicSequenceParameters parameters)
    {
        MusicalKey key = context.Key!.Value;
        long period = VariationFacts.Period(notes, context.Span);
        int first = MotifTexture.Read(notes).Melody[0].Pitch.MidiNumber;
        int semitones = ScaleSteps.Move(key, first, parameters.Steps) - first;
        var used = notes.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        var result = new List<Note>(notes);
        for (int copy = 1; copy <= parameters.Repeats; copy++)
        {
            foreach (Note note in notes)
            {
                int pitch = parameters.Diatonic
                    ? ScaleSteps.Move(key, note.Pitch.MidiNumber, copy * parameters.Steps)
                    : note.Pitch.MidiNumber + (copy * semitones);
                result.Add(note with
                {
                    Id = NewId(note.Id, copy, used),
                    Pitch = new Pitch(Math.Clamp(pitch, 0, Pitch.MaxMidiNumber)),
                    Span = new TickSpan(note.Span.Onset.Offset(copy * period), note.Span.Duration),
                    Attribution = Attribution.UserEdited,
                });
            }
        }

        return result;
    }

    private static string NewId(string id, int copy, HashSet<string> used)
    {
        string candidate = $"{id}~{copy}";
        while (!used.Add(candidate))
        {
            candidate += $"~{copy}";
        }

        return candidate;
    }
}