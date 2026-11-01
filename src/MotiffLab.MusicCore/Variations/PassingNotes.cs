using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Variations;

/// <summary>Which notes V11 adds between two melody notes.</summary>
public enum PassingNoteKind
{
    /// <summary>The scale notes between them, one to three, walking from the first to the second.</summary>
    Passing,

    /// <summary>The scale step above the first note, leading back to the line.</summary>
    UpperNeighbour,

    /// <summary>The scale step below the first note, leading back to the line.</summary>
    LowerNeighbour,
}

/// <summary>The parameters of V11 Passing and neighbour notes.</summary>
/// <param name="Intervals">The melody intervals to fill, interval 0 running from the first note to the second; null for every one.</param>
public sealed record PassingNotesParameters(PassingNoteKind Kind, IntervalSet? Intervals = null) : OperationParameters;

/// <summary>
/// V11 Passing and neighbour notes (Stage 1 spec, Operations): the main notes of the melody keep their pitches and
/// onsets, and notes are added between them in the second half of each note, so the passage keeps its length.
/// Pure and deterministic.
/// </summary>
/// <remarks>
/// <para>
/// Passing notes are the scale notes strictly between two melody notes a third to a fifth apart; a neighbour is a
/// scale step above or below the first note (a whole tone above or a semitone below without a key). The added notes
/// share the second half of the first note in equal parts on the 32nd grid; a note too short for that is left as it
/// is. An added note copies the note it follows, with "~p" and its place added to the id ("n_0003~p1").
/// </para>
/// <para>
/// The step is not available when the melody has fewer than two notes, when passing notes are asked for and the
/// motif has no key, when no picked interval can take the notes, or when an added note would sound at or below the
/// accompaniment.
/// </para>
/// </remarks>
public sealed class PassingNotes : VariationOperation<PassingNotesParameters>
{
    public const string OperationId = "V11";

    /// <summary>The grid the added notes sit on: 32nds.</summary>
    public const long Grid = CanonicalTime.TicksPerQuarter / 8;

    private PassingNotes()
    {
    }

    public static PassingNotes Instance { get; } = new();

    public override string Id => OperationId;

    public override string Name => "Passing and neighbour notes";

    public override bool Supports(OperationScope scope) => scope is OperationScope.All or OperationScope.Melody;

    protected override void Validate(PassingNotesParameters parameters)
    {
        if (!Enum.IsDefined(parameters.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), parameters, "Unknown passing or neighbour note.");
        }
    }

    protected override string? Precondition(IReadOnlyList<Note> notes, VariationContext context, PassingNotesParameters parameters)
    {
        Words w = context.Words;
        MotifTexture texture = MotifTexture.Read(notes);
        if (texture.Melody.Count < 2)
        {
            return NotAvailable(w, "needs at least two melody notes", "нужны хотя бы две ноты мелодии");
        }

        if (parameters.Kind == PassingNoteKind.Passing && context.Key is null)
        {
            return NoKey(w);
        }

        if (parameters.Intervals is { } picked && picked.Indices[^1] >= texture.Melody.Count - 1)
        {
            return NotAvailable(
                w,
                $"is asked to fill interval {picked.Indices[^1] + 1}, and the melody has {texture.Melody.Count - 1}",
                $"нужно заполнить интервал {picked.Indices[^1] + 1}, а в мелодии {w.Count(texture.Melody.Count - 1, "", "", "интервал", "интервала", "интервалов")}");
        }

        Note[] added = [.. Plan(texture, context.Key, parameters).SelectMany(static figure => figure.Added)];
        if (added.Length == 0)
        {
            return NotAvailable(
                w,
                parameters.Kind == PassingNoteKind.Passing
                    ? "finds no two melody notes a third to a fifth apart with room for passing notes"
                    : "finds no melody note long enough to share with a neighbour note",
                parameters.Kind == PassingNoteKind.Passing
                    ? "нет двух нот мелодии на расстоянии от терции до квинты, между которыми поместились бы проходящие"
                    : "нет ноты мелодии, достаточно долгой, чтобы поделиться временем со вспомогательной");
        }

        var line = texture.Melody.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        Note[] under = [.. notes.Where(note => !line.Contains(note.Id))];
        return added.Any(note => under.Any(other => other.Span.Onset <= note.Span.Onset && other.Span.End > note.Span.Onset && other.Pitch.MidiNumber >= note.Pitch.MidiNumber))
            ? NotAvailable(w, "would put an added note at or below the accompaniment", "добавленная нота оказалась бы на уровне аккомпанемента или ниже")
            : null;
    }

    protected override IEnumerable<Note> Transform(IReadOnlyList<Note> notes, VariationContext context, PassingNotesParameters parameters)
    {
        (Note Main, Note Shortened, Note[] Added)[] figures = Plan(MotifTexture.Read(notes), context.Key, parameters);
        var changed = figures.ToDictionary(static figure => figure.Main.Id, StringComparer.Ordinal);
        foreach (Note note in notes)
        {
            if (!changed.TryGetValue(note.Id, out var figure))
            {
                yield return note;
                continue;
            }

            yield return figure.Shortened;
            foreach (Note added in figure.Added)
            {
                yield return added;
            }
        }
    }

    /// <summary>For each melody note that takes added notes: the note shortened and the notes after it.</summary>
    private static (Note Main, Note Shortened, Note[] Added)[] Plan(MotifTexture texture, MusicalKey? key, PassingNotesParameters parameters)
    {
        var figures = new List<(Note, Note, Note[])>();
        for (int i = 0; i + 1 < texture.Melody.Count; i++)
        {
            if (parameters.Intervals is { } picked && !picked.Contains(i))
            {
                continue;
            }

            Note note = texture.Melody[i];
            int[] pitches = Pitches(note.Pitch.MidiNumber, texture.Melody[i + 1].Pitch.MidiNumber, key, parameters.Kind);
            if (pitches.Length == 0 || note.Span.Onset.Value % Grid != 0)
            {
                continue;
            }

            long part = note.Span.Duration / 2 / pitches.Length / Grid * Grid;
            if (part < Grid)
            {
                continue;
            }

            long start = note.Span.End.Value - (part * pitches.Length);
            Note[] added = [.. pitches.Select((pitch, j) => note with
            {
                Id = $"{note.Id}~p{j + 1}",
                Pitch = new Pitch(pitch),
                Span = new TickSpan(new Tick(start + (j * part)), part),
                Source = null,
                Attribution = Attribution.UserEdited,
            })];
            figures.Add((note, note with { Span = TickSpan.FromBounds(note.Span.Onset, new Tick(start)), Attribution = Attribution.UserEdited }, added));
        }

        return [.. figures];
    }

    /// <summary>The added pitches between two melody notes, in order; empty when none fit.</summary>
    private static int[] Pitches(int from, int to, MusicalKey? key, PassingNoteKind kind)
    {
        if (kind == PassingNoteKind.UpperNeighbour)
        {
            return [key is { } scale ? ScaleSteps.Move(scale, from, 1) : from + 2];
        }

        if (kind == PassingNoteKind.LowerNeighbour)
        {
            return [key is { } scale ? ScaleSteps.Move(scale, from, -1) : from - 1];
        }

        MusicalKey inKey = key!.Value;
        int steps = ScaleSteps.Locate(inKey, to).Step - ScaleSteps.Locate(inKey, from).Step;
        int direction = Math.Sign(steps);
        return Math.Abs(steps) is >= 2 and <= 4
            ? [.. Enumerable.Range(1, Math.Abs(steps) - 1).Select(step => ScaleSteps.Move(inKey, from, step * direction))]
            : [];
    }
}