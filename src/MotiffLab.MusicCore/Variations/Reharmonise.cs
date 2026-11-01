using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;

namespace MotiffLab.MusicCore.Variations;

/// <summary>Which chord V15 puts in place of each major or minor chord.</summary>
public enum ReharmoniseKind
{
    /// <summary>The relative chord: a major chord becomes the minor chord a minor 3rd below (C to Am), a minor chord the major chord a minor 3rd above (Am to C).</summary>
    Relative,

    /// <summary>The parallel chord: the same root in the other mode (C to Cm, Am to A).</summary>
    Parallel,
}

/// <summary>The parameters of V15 Reharmonise.</summary>
public sealed record ReharmoniseParameters(ReharmoniseKind Kind) : OperationParameters;

/// <summary>
/// V15 Reharmonise (decision record 0018, section 7): the melody stays note for note, and under it each major or minor
/// chord the harmony finder reads (<see cref="HarmonyAnalyzer.FindChords"/>, S1-A3) is replaced by its relative or
/// its parallel chord. Pure and deterministic.
/// </summary>
/// <remarks>
/// <para>
/// Each accompaniment note, every note but the melody's, that starts under a replaced chord keeps its id, onset and
/// length. A bass note on the old root moves to the nearest new root; any other note whose pitch class is in the new
/// chord stays; any other moves to the nearest tone of the new chord, the lower one on a tie, so the accompaniment moves as
/// little as it can. Other chords stay as they are.
/// </para>
/// <para>
/// Reharmonise is not available on a single line, where there are no chords (S1-A3), when the finder reads no major
/// or minor chord, or when a moved note would sound at or above a melody note sounding with it.
/// </para>
/// </remarks>
public sealed class Reharmonise : VariationOperation<ReharmoniseParameters>
{
    public const string OperationId = "V15";

    private Reharmonise()
    {
    }

    public static Reharmonise Instance { get; } = new();

    public override string Id => OperationId;

    public override string Name => "Reharmonise";

    protected override void Validate(ReharmoniseParameters parameters)
    {
        if (!Enum.IsDefined(parameters.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), parameters, "Unknown reharmonisation.");
        }
    }

    /// <summary>The chord that replaces a major or minor chord, or null for any other.</summary>
    public static ChordLabel? Replacement(ChordLabel chord, ReharmoniseKind kind) => (chord.Quality, kind) switch
    {
        (ChordQuality.Major, ReharmoniseKind.Relative) => new ChordLabel((chord.Root + 9) % 12, ChordQuality.Minor),
        (ChordQuality.Minor, ReharmoniseKind.Relative) => new ChordLabel((chord.Root + 3) % 12, ChordQuality.Major),
        (ChordQuality.Major, ReharmoniseKind.Parallel) => chord with { Quality = ChordQuality.Minor },
        (ChordQuality.Minor, ReharmoniseKind.Parallel) => chord with { Quality = ChordQuality.Major },
        _ => null,
    };

    protected override string? Precondition(IReadOnlyList<Note> notes, VariationContext context, ReharmoniseParameters parameters)
    {
        Words w = context.Words;
        MotifTexture texture = MotifTexture.Read(notes);
        if (texture.IsSingleLine)
        {
            return NotAvailable(w, "needs chords under the melody, and this selection is a single line", "это одна линия, а нужны аккорды под мелодией");
        }

        IReadOnlyList<ChordSpan> chords = HarmonyAnalyzer.FindChords(notes, context.Beats, context.Span, context.Key);
        if (!chords.Any(chord => Replacement(chord.Chord, parameters.Kind) is not null))
        {
            return NotAvailable(w, "found no major or minor chord to replace", "не найдено мажорного или минорного аккорда для замены");
        }

        Note[] after = [.. Moved(notes, chords, parameters.Kind)];
        var melody = texture.Melody.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        if (after.Any(note => !melody.Contains(note.Id) && texture.Melody.Any(top => top.Span.Contains(note.Span.Onset) && top.Pitch.MidiNumber <= note.Pitch.MidiNumber)))
        {
            return NotAvailable(w, "would put an accompaniment note at or above the melody", "нота аккомпанемента оказалась бы на уровне мелодии или выше");
        }

        return after.Zip(notes.OrderBy(static note => note.Id, StringComparer.Ordinal)).All(static pair => pair.First.Pitch == pair.Second.Pitch)
            ? w.T($"The accompaniment already plays only notes of the new chords, so {Name} has nothing to change.", $"Аккомпанемент уже играет только звуки новых аккордов: «{NameIn(w)}» ничего не изменит.")
            : null;
    }

    protected override IEnumerable<Note> Transform(IReadOnlyList<Note> notes, VariationContext context, ReharmoniseParameters parameters) =>
        Moved(notes, HarmonyAnalyzer.FindChords(notes, context.Beats, context.Span, context.Key), parameters.Kind);

    /// <summary>Every note, by id, with the accompaniment moved to the new chords.</summary>
    private static IEnumerable<Note> Moved(IReadOnlyList<Note> notes, IReadOnlyList<ChordSpan> chords, ReharmoniseKind kind)
    {
        MotifTexture texture = MotifTexture.Read(notes);
        var melody = texture.Melody.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        var bass = texture.Bass.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        foreach (Note note in notes.OrderBy(static note => note.Id, StringComparer.Ordinal))
        {
            ChordSpan? chord = melody.Contains(note.Id) ? null : chords.FirstOrDefault(chord => chord.Span.Contains(note.Span.Onset));
            if (chord is null || Replacement(chord.Chord, kind) is not { } replacement)
            {
                yield return note;
                continue;
            }

            int pitch = bass.Contains(note.Id) && note.Pitch.PitchClass == chord.Chord.Root
                ? BassPattern.Near(replacement.Root, note.Pitch.MidiNumber)
                : replacement.Contains(note.Pitch.PitchClass) ? note.Pitch.MidiNumber : Nearest(replacement, note.Pitch.MidiNumber);
            yield return pitch == note.Pitch.MidiNumber ? note : note with { Pitch = new Pitch(pitch), Attribution = Attribution.UserEdited };
        }
    }

    /// <summary>The nearest pitch to a note whose pitch class is in the chord; the lower on a tie.</summary>
    private static int Nearest(ChordLabel chord, int pitch)
    {
        for (int distance = 1; distance <= 6; distance++)
        {
            if (chord.Contains(pitch - distance))
            {
                return pitch - distance;
            }

            if (chord.Contains(pitch + distance))
            {
                return pitch + distance;
            }
        }

        return pitch;
    }
}