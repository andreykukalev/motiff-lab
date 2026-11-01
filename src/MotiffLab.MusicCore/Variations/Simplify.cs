using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Variations;

/// <summary>The parameters of V6 Simplify.</summary>
/// <param name="Level">1 to 3, each doing what the one before it does and more (PRD, V6).</param>
public sealed record SimplifyParameters(int Level) : OperationParameters
{
    public const int MinLevel = 1;

    public const int MaxLevel = 3;
}

/// <summary>
/// V6 Simplify (PRD and Stage 1 spec, Operations): fewer, easier notes with the same melody and harmony. Level 1 leaves
/// out the inner voices, every note that is neither the melody nor the bass; level 2 also plays one root per chord in
/// the bass; level 3 also rounds every onset and end to the nearest eighth and folds a melody leap wider than an
/// octave. Pure and deterministic.
/// </summary>
/// <remarks>
/// <para>
/// Level 2 reads the chords from the notes before the step (S1-A3). Under each chord that has a bass note, the bass
/// notes are replaced by the chord's root, placed at or below the first of them within an octave and held to the
/// chord's end, with that note's id and "~b1". Level 3 rounds a tie to the earlier eighth, gives every note at least
/// an eighth, keeps only the first of two notes of one line that would start together, and moves a melody note by
/// octaves until it is no more than an octave from the one before it.
/// </para>
/// <para>
/// Simplify works on the whole selection. It is not available on a single line at levels 1 and 2, which has no inner
/// voice or bass to simplify, or when it would change nothing.
/// </para>
/// </remarks>
public sealed class Simplify : VariationOperation<SimplifyParameters>
{
    public const string OperationId = "V6";

    private const long Eighth = CanonicalTime.TicksPerQuarter / 2;

    private Simplify()
    {
    }

    public static Simplify Instance { get; } = new();

    public override string Id => OperationId;

    public override string Name => "Simplify";

    protected override void Validate(SimplifyParameters parameters)
    {
        if (parameters.Level is < SimplifyParameters.MinLevel or > SimplifyParameters.MaxLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), parameters, "Simplify has levels 1 to 3.");
        }
    }

    protected override string? Precondition(IReadOnlyList<Note> notes, VariationContext context, SimplifyParameters parameters)
    {
        Words w = context.Words;
        if (parameters.Level < 3 && MotifTexture.Read(notes).IsSingleLine)
        {
            return NotAvailable(w, "has no inner voice or bass to leave out on a single line", "в одной линии нет средних голосов и баса, которые можно убрать");
        }

        static (string, long, long, int) Shape(Note note) => (note.Id, note.Span.Onset.Value, note.Span.Duration, note.Pitch.MidiNumber);
        return Transform(notes, context, parameters).Select(Shape).Order().SequenceEqual(notes.Select(Shape).Order())
            ? w.T($"The notes are already this simple, so {Name} level {parameters.Level} has nothing to change.", $"Ноты уже настолько простые: «{NameIn(w)}», уровень {parameters.Level}, ничего не изменит.")
            : null;
    }

    protected override IEnumerable<Note> Transform(IReadOnlyList<Note> notes, VariationContext context, SimplifyParameters parameters)
    {
        MotifTexture texture = MotifTexture.Read(notes);
        var melody = texture.Melody.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        var bass = texture.Bass.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        List<Note> kept = [.. notes.Where(note => melody.Contains(note.Id) || bass.Contains(note.Id))];

        if (parameters.Level >= 2)
        {
            kept = [.. Roots(kept, melody, bass, HarmonyAnalyzer.FindChords(notes, context.Beats, context.Span, context.Key), context.Span)];
        }

        if (parameters.Level >= 3)
        {
            kept = [.. Rounded(kept, melody), .. Rounded(kept.Where(note => !melody.Contains(note.Id)).ToList(), null)];
            kept = Folded(kept, melody);
        }

        return kept;
    }

    /// <summary>The kept notes with each chord's bass notes replaced by its root.</summary>
    private static IEnumerable<Note> Roots(List<Note> kept, HashSet<string> melody, HashSet<string> bass, IReadOnlyList<ChordSpan> chords, TickSpan span)
    {
        Note[] line = [.. kept.Where(note => bass.Contains(note.Id) && !melody.Contains(note.Id))];
        var replaced = new HashSet<string>(StringComparer.Ordinal);
        var roots = new List<Note>();
        foreach (ChordSpan chord in chords)
        {
            Note[] under = [.. line.Where(note => chord.Span.Contains(note.Span.Onset))];
            if (under.Length == 0)
            {
                continue;
            }

            replaced.UnionWith(under.Select(static note => note.Id));
            Note model = under[0];
            roots.Add(model with
            {
                Id = $"{model.Id}~b1",
                Pitch = new Pitch(BassPattern.Place(chord.Chord.Root, model.Pitch.MidiNumber)),
                Span = TickSpan.FromBounds(model.Span.Onset, new Tick(Math.Min(chord.Span.End.Value, span.End.Value))),
                Source = null,
                Attribution = Attribution.UserEdited,
            });
        }

        return [.. kept.Where(note => !replaced.Contains(note.Id)), .. roots];
    }

    /// <summary>One line's notes (the melody, or every other kept note when <paramref name="melody"/> is null) on the eighth grid.</summary>
    private static IEnumerable<Note> Rounded(List<Note> notes, HashSet<string>? melody)
    {
        var taken = new HashSet<(long Onset, int Pitch)>();
        var starts = new HashSet<long>();
        foreach (Note note in notes.Where(note => melody?.Contains(note.Id) ?? true).OrderBy(static note => note.Span.Onset).ThenByDescending(static note => note.Pitch.MidiNumber))
        {
            long onset = Round(note.Span.Onset.Value);
            long end = Math.Max(Round(note.Span.End.Value), onset + Eighth);
            if (melody is not null ? !starts.Add(onset) : !taken.Add((onset, note.Pitch.MidiNumber)))
            {
                continue;
            }

            Note rounded = note with { Span = TickSpan.FromBounds(new Tick(onset), new Tick(end)) };
            yield return rounded == note ? note : rounded with { Attribution = Attribution.UserEdited };
        }
    }

    private static long Round(long tick) => (tick + (Eighth / 2) - 1) / Eighth * Eighth;

    /// <summary>The notes with every melody leap wider than an octave folded by octaves towards the note before.</summary>
    private static List<Note> Folded(List<Note> notes, HashSet<string> melody)
    {
        var result = new List<Note>(notes);
        int? previous = null;
        foreach (Note note in notes.Where(note => melody.Contains(note.Id)).OrderBy(static note => note.Span.Onset).ToArray())
        {
            int pitch = note.Pitch.MidiNumber;
            while (previous is int before && Math.Abs(pitch - before) > 12)
            {
                pitch += pitch > before ? -12 : 12;
            }

            if (pitch != note.Pitch.MidiNumber)
            {
                result[result.IndexOf(note)] = note with { Pitch = new Pitch(pitch), Attribution = Attribution.UserEdited };
            }

            previous = pitch;
        }

        return result;
    }
}