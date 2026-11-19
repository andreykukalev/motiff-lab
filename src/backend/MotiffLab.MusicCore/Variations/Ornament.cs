using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Variations;

/// <summary>The ornament V14 adds to a long melody note.</summary>
public enum OrnamentKind
{
    /// <summary>The note, the step above, the note again, then held.</summary>
    Mordent,

    /// <summary>The note, the step below, the note again, then held.</summary>
    LowerMordent,

    /// <summary>The step above, the note, the step below, then the note held.</summary>
    Turn,

    /// <summary>The note and the step above, alternating for half the note, then the note held.</summary>
    Trill,

    /// <summary>A grace note a step above, struck on the note's onset, then the note held.</summary>
    GraceAbove,

    /// <summary>A grace note a step below, struck on the note's onset, then the note held.</summary>
    GraceBelow,
}

/// <summary>The parameters of V14 Ornament.</summary>
public sealed record OrnamentParameters(OrnamentKind Kind) : OperationParameters;

/// <summary>
/// V14 Ornament (decision record 0018, section 7): adds an ornament in 32nds to each melody note a quarter or longer,
/// with the neighbour notes a step of the key's scale away (a whole tone above and a semitone below without a key).
/// The ornament fits inside the note's own time, so every onset of the motif stays and the bass and the chords
/// under it are not touched. Pure and deterministic.
/// </summary>
/// <remarks>
/// The ornamented note keeps its id for the first part at its own pitch (the second note of a turn, the held note
/// after a grace note) and
/// the other parts add "~o" and their place ("n_0004~o2"). The ornament is not available when the line has no
/// note a quarter or longer that starts on the 32nd grid, or when a neighbour note would sound at or below a note
/// of the accompaniment sounding with it.
/// </remarks>
public sealed class Ornament : VariationOperation<OrnamentParameters>
{
    public const string OperationId = "V14";

    /// <summary>The shortest note that gets an ornament: a quarter.</summary>
    public const long MinTicks = CanonicalTime.TicksPerQuarter;

    /// <summary>Each note of the ornament: a 32nd.</summary>
    public const long Step = CanonicalTime.TicksPerQuarter / 8;

    private Ornament()
    {
    }

    public static Ornament Instance { get; } = new();

    public override string Id => OperationId;

    public override string Name => "Ornament";

    public override bool Supports(OperationScope scope) => scope is OperationScope.All or OperationScope.Melody;

    protected override void Validate(OrnamentParameters parameters)
    {
        if (!Enum.IsDefined(parameters.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), parameters, "Unknown ornament.");
        }
    }

    protected override string? Precondition(IReadOnlyList<Note> notes, VariationContext context, OrnamentParameters parameters)
    {
        Words w = context.Words;
        MotifTexture texture = MotifTexture.Read(notes);
        Note[] ornamented = [.. texture.Melody.Where(Fits)];
        if (ornamented.Length == 0)
        {
            return NotAvailable(w, "needs a melody note a quarter or longer to ornament", "нужна нота мелодии длиной не меньше четверти");
        }

        var line = texture.Melody.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        Note[] under = [.. notes.Where(note => !line.Contains(note.Id))];
        Note[] added = [.. ornamented.SelectMany(note => Figure(note, context.Key, parameters.Kind))];
        return added.Any(note => under.Any(other => other.Span.Contains(note.Span.Onset) && other.Pitch.MidiNumber >= note.Pitch.MidiNumber))
            ? NotAvailable(w, "would put an ornament note at or below the accompaniment", "нота орнамента оказалась бы на уровне аккомпанемента или ниже")
            : null;
    }

    protected override IEnumerable<Note> Transform(IReadOnlyList<Note> notes, VariationContext context, OrnamentParameters parameters)
    {
        MotifTexture texture = MotifTexture.Read(notes);
        var ornamented = texture.Melody.Where(Fits).Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        return [.. notes.Where(note => !ornamented.Contains(note.Id)), .. texture.Melody.Where(Fits).SelectMany(note => Figure(note, context.Key, parameters.Kind))];
    }

    private static bool Fits(Note note) => note.Span.Duration >= MinTicks && note.Span.Onset.Value % Step == 0;

    /// <summary>The notes that replace a long note: its ornament, then the note held to its end.</summary>
    private static IEnumerable<Note> Figure(Note note, MusicalKey? key, OrnamentKind kind)
    {
        int pitch = note.Pitch.MidiNumber;
        int above = key is { } scale ? ScaleSteps.Move(scale, pitch, 1) : pitch + 2;
        int below = key is { } lower ? ScaleSteps.Move(lower, pitch, -1) : pitch - 1;
        int[] figure = kind switch
        {
            OrnamentKind.Mordent => [pitch, above],
            OrnamentKind.LowerMordent => [pitch, below],
            OrnamentKind.Turn => [above, pitch, below],
            OrnamentKind.Trill => [.. Enumerable.Range(0, TrillNotes(note.Span.Duration)).Select(i => i % 2 == 0 ? pitch : above)],
            OrnamentKind.GraceAbove => [above],
            OrnamentKind.GraceBelow => [below],
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown ornament."),
        };

        // The note keeps its id on its first part at its own pitch; a turn or a grace note starts on a neighbour.
        int keeper = Array.IndexOf(figure, pitch) is int index and >= 0 ? index : figure.Length;
        long onset = note.Span.Onset.Value;
        int part = 0;
        for (int i = 0; i <= figure.Length; i++)
        {
            bool held = i == figure.Length;
            long from = onset + (i * Step);
            long to = held ? note.Span.End.Value : from + Step;
            Note piece = note with
            {
                Pitch = new Pitch(held ? pitch : figure[i]),
                Span = TickSpan.FromBounds(new Tick(from), new Tick(to)),
                Attribution = Attribution.UserEdited,
            };
            if (i == keeper)
            {
                yield return piece;
            }
            else
            {
                part++;
                yield return piece with { Id = $"{note.Id}~o{part}", Source = null };
            }
        }
    }

    /// <summary>The trill's notes before the held note: half the note in 32nds, an even number, at least four.</summary>
    private static int TrillNotes(long duration) => (int)Math.Max(4, duration / 2 / Step / 2 * 2);
}