using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Variations;

/// <summary>Which onsets V8 accents.</summary>
public enum AccentPattern
{
    /// <summary>Notes that start on a beat (a dotted quarter in compound meters).</summary>
    OnBeat,

    /// <summary>Notes that start between the beats.</summary>
    OffBeat,

    /// <summary>Every third onset, from the first.</summary>
    EveryThird,

    /// <summary>Notes that start a bar.</summary>
    Downbeat,

    /// <summary>The onsets the player picks, counted from 0 in time order (<see cref="AccentShiftParameters.Onsets"/>).</summary>
    Custom,
}

/// <summary>The parameters of V8 Accent shift.</summary>
/// <param name="Onsets">For <see cref="AccentPattern.Custom"/> only: the onsets to accent, counted from 0 in time order.</param>
public sealed record AccentShiftParameters(AccentPattern Pattern, IntervalSet? Onsets = null) : OperationParameters
{
    /// <summary>How much louder than the passage's usual velocity an accented note plays.</summary>
    public const int Lift = 20;

    /// <summary>How much softer than the usual velocity every other note plays.</summary>
    public const int Drop = 10;
}

/// <summary>
/// V8 Accent shift (Stage 1 spec, Operations): every pitch, onset and length stays, and only the velocities change,
/// so the accents fall on other notes. Pure and deterministic.
/// </summary>
/// <remarks>
/// The passage's usual velocity is the median of the notes the step changes (the lower one of an even count). Notes
/// that start on an accented onset play <see cref="AccentShiftParameters.Lift"/> louder and every other note
/// <see cref="AccentShiftParameters.Drop"/> softer, within 1 to 127, so notes struck together share an accent. The
/// step is not available when the pattern accents no onset or every onset, since no note would stand out, or when the
/// notes already play these velocities.
/// </remarks>
public sealed class AccentShift : VariationOperation<AccentShiftParameters>
{
    public const string OperationId = "V8";

    private AccentShift()
    {
    }

    public static AccentShift Instance { get; } = new();

    public override string Id => OperationId;

    public override string Name => "Accent shift";

    /// <summary>Any scope: limited to the melody, the accompaniment keeps its own dynamics.</summary>
    public override bool Supports(OperationScope scope) => true;

    protected override void Validate(AccentShiftParameters parameters)
    {
        if (!Enum.IsDefined(parameters.Pattern) || (parameters.Pattern == AccentPattern.Custom) != (parameters.Onsets is not null))
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), parameters, "Pick a pattern, and onsets only for a custom one.");
        }
    }

    protected override string? Precondition(IReadOnlyList<Note> notes, VariationContext context, AccentShiftParameters parameters)
    {
        Words w = context.Words;
        long[] onsets = Onsets(notes);
        if (parameters.Onsets is { } picked && picked.Indices[^1] >= onsets.Length)
        {
            return NotAvailable(
                w,
                $"is asked to accent onset {picked.Indices[^1] + 1}, and the notes start at {onsets.Length} {(onsets.Length == 1 ? "place" : "places")}",
                $"нужно выделить начало {picked.Indices[^1] + 1}, а ноты начинаются в {w.Count(onsets.Length, "", "", "месте", "местах", "местах")}");
        }

        int accented = onsets.Count(onset => Accented(onset, onsets, context.Beats, parameters));
        if (accented == 0 || accented == onsets.Length)
        {
            return NotAvailable(
                w,
                accented == 0 ? "finds no note on the pattern to accent" : "would accent every note, so none would stand out",
                accented == 0 ? "ни одна нота не попадает в рисунок акцентов" : "акцент получила бы каждая нота, и ни одна не выделилась бы");
        }

        return Transform(notes, context, parameters).Zip(notes).All(static pair => pair.First.Velocity == pair.Second.Velocity)
            ? w.T($"The notes already play these accents, so {Name} has nothing to change.", $"Ноты уже звучат с этими акцентами: «{NameIn(w)}» ничего не изменит.")
            : null;
    }

    protected override IEnumerable<Note> Transform(IReadOnlyList<Note> notes, VariationContext context, AccentShiftParameters parameters)
    {
        long[] onsets = Onsets(notes);
        int[] velocities = [.. notes.Select(static note => note.Velocity).Order()];
        int usual = velocities[(velocities.Length - 1) / 2];
        int loud = Math.Min(127, usual + AccentShiftParameters.Lift);
        int soft = Math.Max(1, usual - AccentShiftParameters.Drop);
        foreach (Note note in notes)
        {
            int velocity = Accented(note.Span.Onset.Value, onsets, context.Beats, parameters) ? loud : soft;
            yield return velocity == note.Velocity ? note : note with { Velocity = velocity, Attribution = Attribution.UserEdited };
        }
    }

    private static long[] Onsets(IReadOnlyList<Note> notes) => [.. notes.Select(static note => note.Span.Onset.Value).Distinct().Order()];

    private static bool Accented(long onset, long[] onsets, MeterMap meter, AccentShiftParameters parameters)
    {
        Bar bar = meter.BarAt(new Tick(onset));
        TimeSignature signature = bar.Signature;
        long beat = signature.BeatTicks * (signature.Denominator == 8 && signature.Numerator % 3 == 0 && signature.Numerator > 3 ? 3 : 1);
        long inBar = onset - bar.Span.Onset.Value + bar.LeadIn;
        return parameters.Pattern switch
        {
            AccentPattern.OnBeat => inBar % beat == 0,
            AccentPattern.OffBeat => inBar % beat != 0,
            AccentPattern.EveryThird => Array.IndexOf(onsets, onset) % 3 == 0,
            AccentPattern.Downbeat => onset == bar.Span.Onset.Value && bar.LeadIn == 0,
            _ => parameters.Onsets!.Contains(Array.IndexOf(onsets, onset)),
        };
    }
}