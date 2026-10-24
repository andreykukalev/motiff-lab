using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Variations;

/// <summary>The V1 rhythm reshapes this slice offers. Syncopate and dotted follow in #35.</summary>
public enum RhythmReshapeKind
{
    /// <summary>Every onset twice as far from the motif's start, every note twice as long.</summary>
    HalfTime,

    /// <summary>Every onset half as far from the motif's start, every note half as long.</summary>
    DoubleTime,

    /// <summary>Every note starts 1–3 eighths later, with its length unchanged.</summary>
    Displace,
}

/// <summary>The parameters of V1 Rhythm reshape (S1-E3).</summary>
/// <param name="Eighths">How far <see cref="RhythmReshapeKind.Displace"/> moves the notes, 1–3 eighths; ignored otherwise.</param>
public sealed record RhythmReshapeParameters(RhythmReshapeKind Kind, int Eighths = 1) : OperationParameters
{
    public const int MinEighths = 1;

    public const int MaxEighths = 3;
}

/// <summary>
/// V1 Rhythm reshape: keeps the pitches and their order, changes onsets and durations. Pure and
/// deterministic: the same notes, selection, Keep choices and parameters give the same variation.
/// </summary>
public sealed class RhythmReshape : VariationOperation<RhythmReshapeParameters>
{
    public const string OperationId = "V1";

    private const long EighthTicks = CanonicalTime.TicksPerQuarter / 2;

    private RhythmReshape()
    {
    }

    public static RhythmReshape Instance { get; } = new();

    public override string Id => OperationId;

    public override string Name => "Rhythm reshape";

    protected override void Validate(RhythmReshapeParameters parameters)
    {
        if (parameters.Kind == RhythmReshapeKind.Displace
            && parameters.Eighths is < RhythmReshapeParameters.MinEighths or > RhythmReshapeParameters.MaxEighths)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), parameters.Eighths, "Displace moves notes by 1–3 eighths.");
        }
    }

    protected override string? Precondition(IReadOnlyList<Note> notes, VariationContext context, RhythmReshapeParameters parameters) =>
        parameters.Kind == RhythmReshapeKind.DoubleTime
            && notes.Any(note => (note.Span.Onset.Value - context.Span.Onset.Value) % 2 != 0 || note.Span.Duration % 2 != 0)
            ? "Double-time would put a note between ticks, so it is not available for this selection."
            : null;

    protected override IEnumerable<Note> Transform(IReadOnlyList<Note> notes, VariationContext context, RhythmReshapeParameters parameters) =>
        notes.Select(note => note with
        {
            Span = Reshape(note.Span, context.Span.Onset.Value, parameters),
            Attribution = Attribution.UserEdited,
        });

    private static TickSpan Reshape(TickSpan span, long start, RhythmReshapeParameters parameters)
    {
        long offset = span.Onset.Value - start;
        return parameters.Kind switch
        {
            RhythmReshapeKind.HalfTime => new TickSpan(new Tick(start + (offset * 2)), span.Duration * 2),
            RhythmReshapeKind.DoubleTime => new TickSpan(new Tick(start + (offset / 2)), span.Duration / 2),
            RhythmReshapeKind.Displace => new TickSpan(span.Onset.Offset(parameters.Eighths * EighthTicks), span.Duration),
            _ => throw new ArgumentOutOfRangeException(nameof(parameters), parameters.Kind, "Unknown rhythm reshape."),
        };
    }
}