using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Variations;

/// <summary>The V1 rhythm reshapes (Stage 1 spec, Operations in Stage 1 · V1).</summary>
public enum RhythmReshapeKind
{
    /// <summary>
    /// Every onset on a beat after the motif's first moves half a beat earlier and is held to where it
    /// ended, so it is heard ahead of the beat and tied into it; a note that ran up to that beat ends
    /// half a beat earlier. A beat is skipped when something already starts in the half beat before it.
    /// </summary>
    Syncopate,

    /// <summary>
    /// Every pair of notes a quarter, an eighth or a 16th apart that starts on a multiple of twice
    /// that distance from the bar line becomes long–short: the first note sounds half the distance
    /// longer, and the second starts that much later. Notes held longer than the distance stay.
    /// </summary>
    Dotted,

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

    private const long SixteenthTicks = CanonicalTime.TicksPerQuarter / 4;

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

    protected override string? Precondition(IReadOnlyList<Note> notes, VariationContext context, RhythmReshapeParameters parameters) => parameters.Kind switch
    {
        RhythmReshapeKind.DoubleTime when notes.Any(note => (note.Span.Onset.Value - context.Span.Onset.Value) % 2 != 0 || note.Span.Duration % 2 != 0) =>
            "Double-time would put a note between ticks, so it is not available for this selection.",
        RhythmReshapeKind.Syncopate when Anticipations(notes, context).Count == 0 =>
            "Syncopate needs a note on a beat after the first with nothing starting in the half beat before it, so it is not available for this selection.",
        RhythmReshapeKind.Dotted when Pairs(notes, context.Beats).Count == 0 =>
            "Dotted needs two notes in a row a quarter, an eighth or a 16th apart, the first on a multiple of twice that distance from the bar line, so it is not available for this selection.",
        _ => null,
    };

    protected override IEnumerable<Note> Transform(IReadOnlyList<Note> notes, VariationContext context, RhythmReshapeParameters parameters)
    {
        Dictionary<string, TickSpan> spans = parameters.Kind switch
        {
            RhythmReshapeKind.Syncopate => Syncopate(notes, context),
            RhythmReshapeKind.Dotted => Dot(notes, context.Beats),
            _ => notes.ToDictionary(static note => note.Id, note => Reshape(note.Span, context.Span.Onset.Value, parameters), StringComparer.Ordinal),
        };
        return notes.Select(note => spans.TryGetValue(note.Id, out TickSpan span) && span != note.Span
            ? note with { Span = span, Attribution = Attribution.UserEdited }
            : note);
    }

    /// <summary>The beats Syncopate anticipates, each with how far: half a beat of the bar it is in.</summary>
    private static List<(Tick Beat, long Early)> Anticipations(IReadOnlyList<Note> notes, VariationContext context)
    {
        MeterMap meter = context.Beats;
        Tick[] onsets = [.. notes.Select(static note => note.Span.Onset).Distinct().Order()];
        var found = new List<(Tick, long)>();
        foreach (Tick onset in onsets)
        {
            long beat = meter.SignatureAt(onset).BeatTicks;
            if (meter.ToPosition(onset).BeatOffset != 0 || beat % 2 != 0)
            {
                continue;
            }

            long early = beat / 2;
            long from = onset.Value - early;
            if (from >= context.Span.Onset.Value && !onsets.Any(other => other.Value >= from && other < onset))
            {
                found.Add((onset, early));
            }
        }

        return found;
    }

    private static Dictionary<string, TickSpan> Syncopate(IReadOnlyList<Note> notes, VariationContext context)
    {
        List<(Tick Beat, long Early)> beats = Anticipations(notes, context);
        Dictionary<string, (long Onset, long End)> spans = notes.ToDictionary(
            static note => note.Id,
            note => beats.FirstOrDefault(beat => beat.Beat == note.Span.Onset) is { Early: > 0 } moved
                ? (note.Span.Onset.Value - moved.Early, note.Span.End.Value)
                : (note.Span.Onset.Value, note.Span.End.Value),
            StringComparer.Ordinal);
        foreach ((Tick beat, long early) in beats)
        {
            long from = beat.Value - early;
            foreach (string id in spans.Keys.ToArray())
            {
                (long onset, long end) = spans[id];
                if (onset < from && end > from && end <= beat.Value)
                {
                    spans[id] = (onset, from);
                }
            }
        }

        return spans.ToDictionary(static pair => pair.Key, static pair => TickSpan.FromBounds(new Tick(pair.Value.Onset), new Tick(pair.Value.End)), StringComparer.Ordinal);
    }

    /// <summary>
    /// The onset groups Dotted pairs, in order: two onsets in a row a quarter, an eighth or a 16th
    /// apart, the first on a multiple of twice that length from its bar's first beat, with nothing
    /// starting sooner than that length after the second. Only notes no longer than the length move;
    /// a longer note, such as a bass held under the pair, stays as it is. A group already in a pair
    /// is not paired again.
    /// </summary>
    private static List<(Note[] First, Note[] Second, long Length)> Pairs(IReadOnlyList<Note> notes, MeterMap meter)
    {
        Note[][] groups = [.. notes.GroupBy(static note => note.Span.Onset).OrderBy(static group => group.Key).Select(static group => group.ToArray())];
        var pairs = new List<(Note[], Note[], long)>();
        for (int i = 0; i + 1 < groups.Length; i++)
        {
            Tick onset = groups[i][0].Span.Onset;
            long length = groups[i + 1][0].Span.Onset.Value - onset.Value;
            MetricPosition position = meter.ToPosition(onset);
            long fromBar = ((position.Beat - 1L) * meter.SignatureAt(onset).BeatTicks) + position.BeatOffset;
            Note[] first = [.. groups[i].Where(note => note.Span.Duration <= length)];
            Note[] second = [.. groups[i + 1].Where(note => note.Span.Duration <= length)];
            if (length is CanonicalTime.TicksPerQuarter or EighthTicks or SixteenthTicks
                && fromBar % (2 * length) == 0
                && (i + 2 == groups.Length || groups[i + 2][0].Span.Onset.Value - groups[i + 1][0].Span.Onset.Value >= length)
                && first.Length > 0
                && second.Length > 0)
            {
                pairs.Add((first, second, length));
                i++;
            }
        }

        return pairs;
    }

    /// <summary>
    /// Each pair becomes long–short: the first note's notes sound half the length longer, and the
    /// second's start half the length later and last at most that half, so a note played detached
    /// keeps its gap and every result stays on the grid when its source was.
    /// </summary>
    private static Dictionary<string, TickSpan> Dot(IReadOnlyList<Note> notes, MeterMap meter)
    {
        var spans = new Dictionary<string, TickSpan>(StringComparer.Ordinal);
        foreach ((Note[] first, Note[] second, long length) in Pairs(notes, meter))
        {
            foreach (Note note in first)
            {
                spans[note.Id] = new TickSpan(note.Span.Onset, note.Span.Duration + (length / 2));
            }

            foreach (Note note in second)
            {
                spans[note.Id] = new TickSpan(note.Span.Onset.Offset(length / 2), Math.Min(note.Span.Duration, length / 2));
            }
        }

        return spans;
    }

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