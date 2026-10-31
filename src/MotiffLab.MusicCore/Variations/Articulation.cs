using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Variations;

/// <summary>How long each note sounds, as a share of the time to the next onset (V13).</summary>
public enum ArticulationKind
{
    /// <summary>About a quarter of the time to the next onset: short, with a wide gap.</summary>
    Short,

    /// <summary>About half of the time to the next onset: detached, with space between the notes.</summary>
    Detached,

    /// <summary>All of the time to the next onset: each note holds until the next one starts.</summary>
    Connected,
}

/// <summary>The parameters of V13 Articulation.</summary>
public sealed record ArticulationParameters(ArticulationKind Kind) : OperationParameters;

/// <summary>
/// V13 Articulation: every note keeps its pitch and its onset and sounds for a share of the time to
/// the next onset in the step's scope, so the same notes can be played short, detached or connected.
/// Pure and deterministic: the same notes, scope and kind give the same variation.
/// </summary>
/// <remarks>
/// <para>
/// The time to the next onset is measured within the notes the step changes, so limited to the
/// melody, a held accompaniment keeps sounding and the melody's notes are spaced against each other.
/// Notes struck together share their next onset; the last onset is measured to the end of the
/// selection, or against its own length when it starts at that end or later.
/// </para>
/// <para>
/// Short and Detached only shorten: a note already shorter than its share keeps its length.
/// Connected only lengthens: a note already held past the next onset keeps its length. A note on the
/// grid of 32nds (or of triplet 16ths) stays on that grid, at the latest grid point within its share
/// and never shorter than one grid step, so a detached 16th sounds for a 32nd and a short 16th does
/// too. Applying the same kind again changes nothing.
/// </para>
/// </remarks>
public sealed class Articulation : VariationOperation<ArticulationParameters>
{
    public const string OperationId = "V13";

    private static readonly long[] s_grids = [CanonicalTime.TicksPerQuarter / 8, CanonicalTime.TicksPerQuarter / 6];

    private Articulation()
    {
    }

    public static Articulation Instance { get; } = new();

    public override string Id => OperationId;

    public override string Name => "Articulation";

    /// <summary>Any scope: limited to the melody, it spaces the melody and leaves a held accompaniment ringing.</summary>
    public override bool Supports(OperationScope scope) => true;

    protected override void Validate(ArticulationParameters parameters)
    {
        if (!Enum.IsDefined(parameters.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), parameters.Kind, "Unknown articulation.");
        }
    }

    protected override string? Precondition(IReadOnlyList<Note> notes, VariationContext context, ArticulationParameters parameters) =>
        notes.All(note => Length(note, notes, context, parameters.Kind) == note.Span.Duration)
            ? parameters.Kind switch
            {
                ArticulationKind.Connected => context.Words.T(
                    "Every note already holds until the next one starts, so Connected has nothing to change.",
                    "Каждая нота уже держится до начала следующей: «Связно» ничего не изменит."),
                _ => context.Words.T(
                    $"Every note is already this short, so {parameters.Kind} has nothing to change.",
                    $"Каждая нота уже настолько короткая: «{(parameters.Kind == ArticulationKind.Short ? "Коротко" : "Отрывисто")}» ничего не изменит."),
            }
            : null;

    protected override IEnumerable<Note> Transform(IReadOnlyList<Note> notes, VariationContext context, ArticulationParameters parameters) =>
        notes.Select(note => Length(note, notes, context, parameters.Kind) is long length && length != note.Span.Duration
            ? note with { Span = new TickSpan(note.Span.Onset, length), Attribution = Attribution.UserEdited }
            : note);

    /// <summary>The note's new length under <paramref name="kind"/>.</summary>
    private static long Length(Note note, IReadOnlyList<Note> notes, VariationContext context, ArticulationKind kind)
    {
        long onset = note.Span.Onset.Value;
        long next = notes.Where(other => other.Span.Onset.Value > onset).Select(static other => other.Span.Onset.Value).DefaultIfEmpty(context.Span.End.Value).Min();
        long gap = next > onset ? next - onset : note.Span.Duration;
        return kind switch
        {
            ArticulationKind.Connected => Math.Max(note.Span.Duration, gap),
            ArticulationKind.Detached => Math.Min(note.Span.Duration, Share(note, gap, 2)),
            ArticulationKind.Short => Math.Min(note.Span.Duration, Share(note, gap, 4)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown articulation."),
        };
    }

    /// <summary>
    /// <paramref name="gap"/> divided by <paramref name="divisor"/>, at least one tick. A note that
    /// starts and ends on the grid of 32nds, or else of triplet 16ths, ends on that same grid: at
    /// the latest grid point within its share, and never sooner than the first after its onset.
    /// </summary>
    private static long Share(Note note, long gap, long divisor)
    {
        long share = Math.Max(1, gap / divisor);
        long onset = note.Span.Onset.Value;
        long grid = s_grids.FirstOrDefault(grid => onset % grid == 0 && note.Span.End.Value % grid == 0);
        return grid == 0 ? share : Math.Max(grid, share / grid * grid);
    }
}