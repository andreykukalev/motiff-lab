using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Variations;

/// <summary>How V2 plays each bar's accompaniment notes.</summary>
public enum AccompanimentPatternKind
{
    /// <summary>All the bar's accompaniment notes struck together once and held to the end of the bar.</summary>
    HeldChord,

    /// <summary>All the bar's accompaniment notes struck together on every unit.</summary>
    RepeatedChord,

    /// <summary>One note per unit, from the lowest to the highest, then again.</summary>
    ArpeggioUp,

    /// <summary>One note per unit, from the highest to the lowest, then again.</summary>
    ArpeggioDown,

    /// <summary>One note per unit, up to the highest and back down without repeating the turning notes.</summary>
    UpDown,

    /// <summary>One note per unit: lowest, highest, middle, highest. Needs three different notes in each bar.</summary>
    Alberti,
}

/// <summary>The note value a V2 pattern moves in.</summary>
public enum PatternUnit
{
    Eighths,
    Triplets,
    Sixteenths,
}

/// <summary>The parameters of V2 Accompaniment pattern.</summary>
/// <param name="Unit">The note value the pattern moves in; <see cref="AccompanimentPatternKind.HeldChord"/> ignores it.</param>
public sealed record AccompanimentPatternParameters(AccompanimentPatternKind Kind, PatternUnit Unit = PatternUnit.Eighths) : OperationParameters;

/// <summary>
/// V2 Accompaniment pattern: the melody stays as it is, and each bar's accompaniment replays the
/// pitches it already has in a new rhythm and order. The pitches come from the accompaniment itself,
/// not from a detected chord, so the harmony each bar sounds is the one it had and no chord
/// detection is needed. Pure and deterministic.
/// </summary>
/// <remarks>
/// <para>
/// The accompaniment is every note outside the melody line of the selection's texture
/// (<see cref="MotifTexture"/>). Bars come from the meter; a bar's pattern starts on its first beat
/// and fills the part of the bar inside the selection, the last unit cut at the bar's end. A bar
/// with no accompaniment stays empty.
/// </para>
/// <para>
/// Each new note copies the track, hand, voice and velocity of the accompaniment note of the same
/// pitch that starts first in its bar, and takes that note's id with "~p" and its place in the
/// pattern ("n_0012~p3"). The old accompaniment notes are removed. The pattern is not available when
/// a new note would sound at or above a melody note sounding with it, since the melody would stop
/// being the top line.
/// </para>
/// </remarks>
public sealed class AccompanimentPattern : VariationOperation<AccompanimentPatternParameters>
{
    public const string OperationId = "V2";

    private AccompanimentPattern()
    {
    }

    public static AccompanimentPattern Instance { get; } = new();

    public override string Id => OperationId;

    public override string Name => "Accompaniment pattern";

    protected override void Validate(AccompanimentPatternParameters parameters)
    {
        if (!Enum.IsDefined(parameters.Kind) || !Enum.IsDefined(parameters.Unit))
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), parameters, "Unknown accompaniment pattern.");
        }
    }

    protected override string? Precondition(IReadOnlyList<Note> notes, VariationContext context, AccompanimentPatternParameters parameters)
    {
        MotifTexture texture = MotifTexture.Read(notes);
        HashSet<string> melody = texture.Melody.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        Note[] accompaniment = [.. notes.Where(note => !melody.Contains(note.Id))];
        if (accompaniment.Length == 0)
        {
            return $"{Name} needs an accompaniment under the melody, and this selection has none, so it is not available.";
        }

        if (parameters.Kind == AccompanimentPatternKind.Alberti && Bars(accompaniment, context).Any(static bar => bar.Pitches.Length < 3))
        {
            return "Alberti needs three different accompaniment notes in each bar, so it is not available for this selection.";
        }

        Note[] pattern = Pattern(accompaniment, context, parameters);
        if (pattern.Any(added => texture.Melody.Any(note => note.Span.Contains(added.Span.Onset) && note.Pitch.MidiNumber <= added.Pitch.MidiNumber)))
        {
            return $"{Name} would put an accompaniment note at or above the melody, so it is not available for this selection.";
        }

        static (long, long, int) Shape(Note note) => (note.Span.Onset.Value, note.Span.Duration, note.Pitch.MidiNumber);
        return accompaniment.Select(Shape).Order().SequenceEqual(pattern.Select(Shape).Order())
            ? $"The accompaniment already plays this pattern, so {Name} has nothing to change."
            : null;
    }

    protected override IEnumerable<Note> Transform(IReadOnlyList<Note> notes, VariationContext context, AccompanimentPatternParameters parameters)
    {
        HashSet<string> melody = MotifTexture.Read(notes).Melody.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        Note[] accompaniment = [.. notes.Where(note => !melody.Contains(note.Id))];
        return [.. notes.Where(note => melody.Contains(note.Id)), .. Pattern(accompaniment, context, parameters)];
    }

    /// <summary>The accompaniment's bars that hold notes, in order: the part inside the selection and the pitches, lowest first, each with the note it copies.</summary>
    private static (TickSpan Window, int[] Pitches, Note[] Models)[] Bars(Note[] accompaniment, VariationContext context) =>
        [.. accompaniment
            .GroupBy(note => context.Beats.BarAt(note.Span.Onset).Number)
            .OrderBy(static bar => bar.Key)
            .Select(bar =>
            {
                TickSpan span = context.Beats.GetBar(bar.Key).Span;
                long from = Math.Max(span.Onset.Value, context.Span.Onset.Value);
                long to = Math.Min(span.End.Value, context.Span.End.Value);
                Note[] models = [.. bar
                    .OrderBy(static note => note.Span.Onset)
                    .ThenBy(static note => note.Id, StringComparer.Ordinal)
                    .DistinctBy(static note => note.Pitch.MidiNumber)
                    .OrderBy(static note => note.Pitch.MidiNumber)];
                return (TickSpan.FromBounds(new Tick(from), new Tick(Math.Max(from + 1, to))), models.Select(static note => note.Pitch.MidiNumber).ToArray(), models);
            })];

    private static Note[] Pattern(Note[] accompaniment, VariationContext context, AccompanimentPatternParameters parameters)
    {
        long unit = parameters.Unit switch
        {
            PatternUnit.Eighths => CanonicalTime.TicksPerQuarter / 2,
            PatternUnit.Triplets => CanonicalTime.TicksPerQuarter / 3,
            PatternUnit.Sixteenths => CanonicalTime.TicksPerQuarter / 4,
            _ => throw new ArgumentOutOfRangeException(nameof(parameters), parameters.Unit, "Unknown unit."),
        };
        var pattern = new List<Note>();
        foreach ((TickSpan window, int[] pitches, Note[] models) in Bars(accompaniment, context))
        {
            long barStart = context.Beats.BarAt(window.Onset).Span.Onset.Value;
            long[] onsets = parameters.Kind == AccompanimentPatternKind.HeldChord
                ? [window.Onset.Value]
                : [.. Enumerable.Range(0, (int)((window.End.Value - barStart + unit - 1) / unit))
                    .Select(i => barStart + (i * unit))
                    .Where(onset => onset >= window.Onset.Value)];
            for (int i = 0; i < onsets.Length; i++)
            {
                long end = parameters.Kind == AccompanimentPatternKind.HeldChord ? window.End.Value : Math.Min(onsets[i] + unit, window.End.Value);
                foreach (int index in Indices(parameters.Kind, i, pitches.Length))
                {
                    Note model = models[index];
                    pattern.Add(model with
                    {
                        Id = $"{model.Id}~p{i + 1}",
                        Span = TickSpan.FromBounds(new Tick(onsets[i]), new Tick(end)),
                        Source = null,
                        Attribution = Attribution.UserEdited,
                    });
                }
            }
        }

        return [.. pattern];
    }

    /// <summary>Which of a bar's pitches, lowest first, sound at the pattern's <paramref name="i"/>th onset.</summary>
    private static IEnumerable<int> Indices(AccompanimentPatternKind kind, int i, int count) => kind switch
    {
        AccompanimentPatternKind.HeldChord or AccompanimentPatternKind.RepeatedChord => Enumerable.Range(0, count),
        AccompanimentPatternKind.ArpeggioUp => [i % count],
        AccompanimentPatternKind.ArpeggioDown => [count - 1 - (i % count)],
        AccompanimentPatternKind.UpDown when count == 1 => [0],
        AccompanimentPatternKind.UpDown => [Math.Min(i % ((2 * count) - 2), (2 * count) - 2 - (i % ((2 * count) - 2)))],
        AccompanimentPatternKind.Alberti => [(i % 4) switch { 0 => 0, 1 or 3 => count - 1, _ => 1 }],
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown accompaniment pattern."),
    };
}