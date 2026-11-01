using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Variations;

/// <summary>What V12 plays in the bass under each chord.</summary>
public enum BassPatternKind
{
    /// <summary>The chord's root, held for the chord.</summary>
    RootsOnly,

    /// <summary>The root for the first half of the chord, its fifth above for the rest; the root alone under a chord of one beat.</summary>
    RootAndFifth,

    /// <summary>The root and the root an octave away, alternating on each beat.</summary>
    Octaves,

    /// <summary>A walk on each beat: the root, up the chord's tones, and a step below the next chord's root to lead into it.</summary>
    Walk,

    /// <summary>A pedal point: the key's tonic (or the first chord's root) at one pitch under every chord, struck again in each bar.</summary>
    Pedal,
}

/// <summary>The parameters of V12 Bass pattern.</summary>
public sealed record BassPatternParameters(BassPatternKind Kind) : OperationParameters;

/// <summary>
/// V12 Bass pattern (Stage 1 spec, Operations): the melody and the upper notes stay as they are, and the bass is
/// played anew from the chords the harmony finder reads (<see cref="HarmonyAnalyzer.FindChords"/>, S1-A3). Pure and
/// deterministic.
/// </summary>
/// <remarks>
/// <para>
/// The bass is the motif's bass line (<see cref="MotifTexture.Bass"/>) without notes that are also its melody. Under
/// each chord that has a bass note, the old bass notes are removed and the pattern starts where the first of them
/// started, to the chord's end; each new note is placed at or below the old bass note it replaces, within an
/// octave (an octave lower again when it would meet or pass a note sounding over it), and copies that note's track, hand, voice and velocity, with its id and "~b" and its place
/// ("n_0003~b2"). A pedal keeps one pitch, at or below the first bass note, and is struck again in each bar, from the first bass note to the end of the last.
/// </para>
/// <para>
/// The pattern is not available on a single line, where there is no bass and no chord (S1-A3), when the finder
/// reads no chord, or when a new bass note would sound at or above another note sounding with it, since the bass
/// would stop being the lowest line.
/// </para>
/// </remarks>
public sealed class BassPattern : VariationOperation<BassPatternParameters>
{
    public const string OperationId = "V12";

    private BassPattern()
    {
    }

    public static BassPattern Instance { get; } = new();

    public override string Id => OperationId;

    public override string Name => "Bass pattern";

    protected override void Validate(BassPatternParameters parameters)
    {
        if (!Enum.IsDefined(parameters.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), parameters, "Unknown bass pattern.");
        }
    }

    protected override string? Precondition(IReadOnlyList<Note> notes, VariationContext context, BassPatternParameters parameters)
    {
        Words w = context.Words;
        if (MotifTexture.Read(notes).IsSingleLine)
        {
            return NotAvailable(w, "needs a bass under the melody, and this selection is a single line", "это одна линия, а нужен бас под мелодией");
        }

        if (HarmonyAnalyzer.FindChords(notes, context.Beats, context.Span, context.Key).Count == 0)
        {
            return NotAvailable(w, "found no chords to build a bass on", "не найдено аккордов, на которых можно построить бас");
        }

        (Note[] old, Note[] bass) = Plan(notes, context, parameters);
        if (bass.Length == 0)
        {
            return NotAvailable(w, "found no bass note under the chords", "под аккордами нет ни одной ноты баса");
        }

        var oldIds = old.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        Note[] upper = [.. notes.Where(note => !oldIds.Contains(note.Id))];
        if (bass.Any(added => upper.Any(note => note.Span.Contains(added.Span.Onset) || added.Span.Contains(note.Span.Onset) ? note.Pitch.MidiNumber <= added.Pitch.MidiNumber : false)))
        {
            return NotAvailable(w, "would put a bass note at or above a note sounding over it", "нота баса оказалась бы на уровне звучащей над ней ноты или выше");
        }

        static (long, long, int) Shape(Note note) => (note.Span.Onset.Value, note.Span.Duration, note.Pitch.MidiNumber);
        return old.Select(Shape).Order().SequenceEqual(bass.Select(Shape).Order())
            ? w.T($"The bass already plays this pattern, so {Name} has nothing to change.", $"Бас уже играет этот рисунок: «{NameIn(w)}» ничего не изменит.")
            : null;
    }

    protected override IEnumerable<Note> Transform(IReadOnlyList<Note> notes, VariationContext context, BassPatternParameters parameters)
    {
        (Note[] old, Note[] bass) = Plan(notes, context, parameters);
        var oldIds = old.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        return [.. notes.Where(note => !oldIds.Contains(note.Id)), .. bass];
    }

    /// <summary>The bass notes the pattern replaces and the new ones.</summary>
    private static (Note[] Old, Note[] New) Plan(IReadOnlyList<Note> notes, VariationContext context, BassPatternParameters parameters)
    {
        MotifTexture texture = MotifTexture.Read(notes);
        var melody = texture.Melody.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        Note[] line = [.. texture.Bass.Where(note => !melody.Contains(note.Id))];
        IReadOnlyList<ChordSpan> chords = HarmonyAnalyzer.FindChords(notes, context.Beats, context.Span, context.Key);
        if (line.Length == 0 || chords.Count == 0)
        {
            return ([], []);
        }

        var lineIds = line.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        Note[] upper = [.. notes.Where(note => !lineIds.Contains(note.Id))];
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        Note Bass(Note model, long from, long to, int pitch)
        {
            // A note that would meet or pass a note sounding over it goes an octave lower, while the piano reaches.
            if (pitch - 12 >= OperationRules.LowestPitch && upper.Any(note => note.Span.Onset.Value < to && note.Span.End.Value > from && note.Pitch.MidiNumber <= pitch))
            {
                pitch -= 12;
            }

            int n = counts.GetValueOrDefault(model.Id) + 1;
            counts[model.Id] = n;
            return model with
            {
                Id = $"{model.Id}~b{n}",
                Pitch = new Pitch(pitch),
                Span = TickSpan.FromBounds(new Tick(from), new Tick(to)),
                Source = null,
                Attribution = Attribution.UserEdited,
            };
        }

        if (parameters.Kind == BassPatternKind.Pedal)
        {
            int tonic = context.Key?.TonicPitchClass ?? chords[0].Chord.Root;
            long start = line[0].Span.Onset.Value;
            long end = Math.Min(line.Max(static note => note.Span.End.Value), context.Span.End.Value);
            int held = Place(tonic, line[0].Pitch.MidiNumber);
            var pedal = new List<Note>();
            for (long at = start; at < end;)
            {
                Bar bar = context.Beats.BarAt(new Tick(at));
                long to = Math.Min(bar.Span.End.Value, end);
                Note model = line.LastOrDefault(note => note.Span.Onset.Value <= at) ?? line[0];
                pedal.Add(Bass(model, at, to, held));
                at = to;
            }

            return (line, [.. pedal]);
        }

        var old = new List<Note>();
        var bass = new List<Note>();
        for (int c = 0; c < chords.Count; c++)
        {
            ChordSpan chord = chords[c];
            Note[] under = [.. line.Where(note => chord.Span.Contains(note.Span.Onset))];
            if (under.Length == 0)
            {
                continue;
            }

            old.AddRange(under);
            Note model = under[0];
            long from = model.Span.Onset.Value;
            long to = Math.Min(chord.Span.End.Value, context.Span.End.Value);
            int root = Place(chord.Chord.Root, model.Pitch.MidiNumber);
            long[] beats = Beats(context.Beats, from, to);
            switch (parameters.Kind)
            {
                case BassPatternKind.RootsOnly:
                    bass.Add(Bass(model, from, to, root));
                    break;
                case BassPatternKind.RootAndFifth when beats.Length >= 2:
                    long half = beats[beats.Length / 2];
                    bass.Add(Bass(model, from, half, root));
                    bass.Add(Bass(model, half, to, root + 7));
                    break;
                case BassPatternKind.RootAndFifth:
                    bass.Add(Bass(model, from, to, root));
                    break;
                case BassPatternKind.Octaves:
                    int low = root - 12 >= OperationRules.LowestPitch && UpperNotesNear(notes, line, from, to, root + 12) ? root - 12 : root;
                    for (int b = 0; b < beats.Length; b++)
                    {
                        bass.Add(Bass(model, beats[b], b + 1 < beats.Length ? beats[b + 1] : to, b % 2 == 0 ? low : low + 12));
                    }

                    break;
                case BassPatternKind.Walk:
                    int? next = c + 1 < chords.Count ? Near(chords[c + 1].Chord.Root, root) : null;
                    for (int b = 0; b < beats.Length; b++)
                    {
                        bass.Add(Bass(model, beats[b], b + 1 < beats.Length ? beats[b + 1] : to, Walked(root, b, beats.Length, chord.Chord, next, context.Key)));
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(parameters), parameters.Kind, "Unknown bass pattern.");
            }
        }

        return ([.. old], [.. bass]);
    }

    /// <summary>
    /// The walk's note on a beat: the root on the first, then up the chord's tones, and on the last beat of a chord
    /// of two beats or more a step below the next chord's root, leading into it (a scale step in the key, a semitone
    /// without one; the root again when that step is the root itself).
    /// </summary>
    private static int Walked(int root, int beat, int beats, ChordLabel chord, int? next, MusicalKey? key)
    {
        if (beat > 0 && beat == beats - 1 && next is int goal)
        {
            int approach = key is { } scale ? ScaleSteps.Move(scale, goal, -1) : goal - 1;
            return approach == root ? root : approach;
        }

        IReadOnlyList<int> tones = chord.Intervals;
        return root + tones[beat % tones.Count];
    }

    /// <summary>Whether a note other than the bass sounds in the stretch at or below a pitch.</summary>
    private static bool UpperNotesNear(IReadOnlyList<Note> notes, Note[] line, long from, long to, int pitch) =>
        notes.Any(note => !line.Contains(note) && note.Span.Onset.Value < to && note.Span.End.Value > from && note.Pitch.MidiNumber <= pitch);

    /// <summary>The beats from <paramref name="from"/> to <paramref name="to"/>, the first at <paramref name="from"/>: a dotted quarter in compound meters.</summary>
    private static long[] Beats(MeterMap meter, long from, long to)
    {
        var beats = new List<long> { from };
        long at = from;
        while (true)
        {
            Bar bar = meter.BarAt(new Tick(at));
            TimeSignature signature = bar.Signature;
            long unit = signature.BeatTicks * (signature.Denominator == 8 && signature.Numerator % 3 == 0 && signature.Numerator > 3 ? 3 : 1);
            long next = bar.Span.Onset.Value + ((((at - bar.Span.Onset.Value) / unit) + 1) * unit);
            if (next >= to)
            {
                return [.. beats];
            }

            beats.Add(next);
            at = next;
        }
    }

    /// <summary>The pitch of a pitch class at or below a note, within an octave; an octave higher when that is below the piano.</summary>
    internal static int Place(int pitchClass, int near)
    {
        int below = near - ((((near % 12) - pitchClass) % 12) + 12) % 12;
        return below < OperationRules.LowestPitch ? below + 12 : below;
    }

    /// <summary>The pitch of a pitch class nearest a note, the lower on a tie.</summary>
    internal static int Near(int pitchClass, int near)
    {
        int below = near - ((((near % 12) - pitchClass) % 12) + 12) % 12;
        return near - below <= 6 ? below : below + 12;
    }
}