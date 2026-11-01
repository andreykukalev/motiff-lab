using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Variations;

/// <summary>What V7 does to the line's notes and rests.</summary>
public enum DensityKind
{
    /// <summary>Each note two units or longer is played as repeated notes of one unit at its pitch.</summary>
    Repeat,

    /// <summary>Notes repeated at one pitch, one straight after the other, become one longer note.</summary>
    MergeRepeats,

    /// <summary>More air: a rest of one unit before each note that follows straight on, taken from the end of the note before it.</summary>
    MoreAir,

    /// <summary>Each note is held to the next one, so the rests between them go.</summary>
    FillRests,
}

/// <summary>The parameters of V7 Density and rests.</summary>
/// <param name="Unit">The repeated notes' value, the rest's length, or the gap that still counts as straight after; eighths or sixteenths.</param>
public sealed record DensityParameters(DensityKind Kind, PatternUnit Unit = PatternUnit.Eighths) : OperationParameters;

/// <summary>
/// V7 Density and rests (Stage 1 spec, Operations): repeats notes, merges repeats, or adds or removes rests in the
/// top line of the notes the step changes (<see cref="MotifTexture.Melody"/>), keeping the order of the pitches.
/// Every other note stays as it was. Pure and deterministic.
/// </summary>
/// <remarks>
/// A repeated note keeps the id of the note it splits for its first part and adds "~r" and its place for the
/// others ("n_0007~r2"); a merged note keeps the id of the first of its repeats, and the others are removed. Every
/// onset that stays stays where it was. The step is not available when it would change nothing.
/// </remarks>
public sealed class DensityAndRests : VariationOperation<DensityParameters>
{
    public const string OperationId = "V7";

    private DensityAndRests()
    {
    }

    public static DensityAndRests Instance { get; } = new();

    public override string Id => OperationId;

    public override string Name => "Density and rests";

    public override bool Supports(OperationScope scope) => true;

    protected override void Validate(DensityParameters parameters)
    {
        if (!Enum.IsDefined(parameters.Kind) || parameters.Unit is not (PatternUnit.Eighths or PatternUnit.Sixteenths))
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), parameters, "Unknown density step.");
        }
    }

    protected override string? Precondition(IReadOnlyList<Note> notes, VariationContext context, DensityParameters parameters)
    {
        Words w = context.Words;
        Note[] line = [.. MotifTexture.Read(notes).Melody];
        Note[] after = [.. NewLine(line, parameters)];
        static (string, long, long, int) Shape(Note note) => (note.Id, note.Span.Onset.Value, note.Span.Duration, note.Pitch.MidiNumber);
        if (!after.Select(Shape).SequenceEqual(line.Select(Shape)))
        {
            return null;
        }

        return parameters.Kind switch
        {
            DensityKind.Repeat => NotAvailable(w, $"needs a note at least two {Unit(parameters, w)} long to repeat", $"нужна нота длиной не меньше двух {Unit(parameters, w)}"),
            DensityKind.MergeRepeats => NotAvailable(w, "needs notes repeated at one pitch, one straight after the other", "нужны ноты, повторённые на одной высоте одна за другой"),
            DensityKind.MoreAir => NotAvailable(w, $"needs a note followed straight on by another and at least two {Unit(parameters, w)} long, to make room for a rest", $"нужна нота длиной не меньше двух {Unit(parameters, w)}, за которой сразу идёт следующая, чтобы освободить место для паузы"),
            _ => NotAvailable(w, "needs a rest between two notes of the line to fill", "нужна пауза между двумя нотами линии"),
        };
    }

    protected override IEnumerable<Note> Transform(IReadOnlyList<Note> notes, VariationContext context, DensityParameters parameters)
    {
        Note[] line = [.. MotifTexture.Read(notes).Melody];
        var lineIds = line.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        return [.. notes.Where(note => !lineIds.Contains(note.Id)), .. NewLine(line, parameters)];
    }

    private static long Ticks(DensityParameters parameters) => parameters.Unit == PatternUnit.Sixteenths ? CanonicalTime.TicksPerQuarter / 4 : CanonicalTime.TicksPerQuarter / 2;

    private static string Unit(DensityParameters parameters, Words w) => parameters.Unit == PatternUnit.Sixteenths
        ? w.T("sixteenths", "шестнадцатых")
        : w.T("eighths", "восьмых");

    private static IEnumerable<Note> NewLine(Note[] line, DensityParameters parameters)
    {
        long unit = Ticks(parameters);
        switch (parameters.Kind)
        {
            case DensityKind.Repeat:
                foreach (Note note in line)
                {
                    long count = note.Span.Duration / unit;
                    if (count < 2)
                    {
                        yield return note;
                        continue;
                    }

                    for (long i = 0; i < count; i++)
                    {
                        long onset = note.Span.Onset.Value + (i * unit);
                        long end = i == count - 1 ? note.Span.End.Value : onset + unit;
                        yield return i == 0
                            ? Edited(note, note with { Span = TickSpan.FromBounds(note.Span.Onset, new Tick(end)) })
                            : note with { Id = $"{note.Id}~r{i + 1}", Span = TickSpan.FromBounds(new Tick(onset), new Tick(end)), Source = null, Attribution = Attribution.UserEdited };
                    }
                }

                break;
            case DensityKind.MergeRepeats:
                for (int i = 0; i < line.Length;)
                {
                    int last = i;
                    while (last + 1 < line.Length && line[last + 1].Pitch == line[i].Pitch && line[last + 1].Span.Onset.Value - line[last].Span.End.Value < unit)
                    {
                        last++;
                    }

                    yield return last == i ? line[i] : Edited(line[i], line[i] with { Span = TickSpan.FromBounds(line[i].Span.Onset, line[last].Span.End) });
                    i = last + 1;
                }

                break;
            case DensityKind.MoreAir:
                for (int i = 0; i < line.Length; i++)
                {
                    Note note = line[i];
                    long? next = i + 1 < line.Length ? line[i + 1].Span.Onset.Value : null;
                    yield return next is long start && note.Span.End.Value >= start && start - unit - note.Span.Onset.Value >= unit
                        ? Edited(note, note with { Span = TickSpan.FromBounds(note.Span.Onset, new Tick(start - unit)) })
                        : note;
                }

                break;
            case DensityKind.FillRests:
                for (int i = 0; i < line.Length; i++)
                {
                    Note note = line[i];
                    long? next = i + 1 < line.Length ? line[i + 1].Span.Onset.Value : null;
                    yield return next is long start && note.Span.End.Value < start
                        ? Edited(note, note with { Span = TickSpan.FromBounds(note.Span.Onset, new Tick(start)) })
                        : note;
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(parameters), parameters.Kind, "Unknown density step.");
        }
    }

    private static Note Edited(Note before, Note after) => after == before ? before : after with { Attribution = Attribution.UserEdited };
}