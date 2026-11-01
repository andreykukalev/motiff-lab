using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;

namespace MotiffLab.MusicCore.Variations;

/// <summary>What V3 changes in how the chords are voiced or where the notes sit.</summary>
public enum VoicingKind
{
    /// <summary>Each chord with its root in the bass.</summary>
    RootPosition,

    /// <summary>Each chord with its third in the bass.</summary>
    FirstInversion,

    /// <summary>Each chord with its fifth in the bass.</summary>
    SecondInversion,

    /// <summary>The notes over each chord's bass gathered into the octave above it.</summary>
    Close,

    /// <summary>The chord spread wider: its second-highest note dropped an octave, or its lowest upper note raised one.</summary>
    Open,

    /// <summary>Every note of the step's scope an octave higher.</summary>
    OctaveUp,

    /// <summary>Every note of the step's scope an octave lower.</summary>
    OctaveDown,

    /// <summary>The highest left-hand note of each chord moves to the right hand.</summary>
    OtherHand,
}

/// <summary>The parameters of V3 Voicing and register.</summary>
public sealed record VoicingParameters(VoicingKind Kind) : OperationParameters
{
    /// <summary>Whether the kind moves whole notes by an octave rather than revoicing the chords.</summary>
    public bool IsOctave => Kind is VoicingKind.OctaveUp or VoicingKind.OctaveDown;
}

/// <summary>
/// V3 Voicing and register (Stage 1 spec, Operations): the chords the harmony finder reads (S1-A3) keep their notes
/// and change their inversion, their spacing or the hand that plays a note, or the step's notes move an octave. The
/// melody and every onset and length stay. Pure and deterministic.
/// </summary>
/// <remarks>
/// <para>
/// A chord's notes are the notes other than the melody's that start under it. An inversion moves the lowest note of the chord tone it asks for down by octaves until it is under the
/// chord; Close folds every note over the lowest into the octave above it; Open drops the second-highest note an
/// octave when that keeps it over the lowest, and otherwise raises the lowest note over the bass an octave.
/// A chord that lacks the tone or would reach the melody stays as it is. Other hand moves the highest left-hand note
/// of each chord with two or more left-hand notes to the right hand, and needs notes marked with hands.
/// </para>
/// <para>
/// The octave shifts can be limited to the melody, the accompaniment or the bass; the other kinds work on the whole
/// selection. While Keep bass is on, the inversions are off and the bass notes do not move (footnote 4); while Keep
/// melody is on, only an octave shift may move the melody, keeping its pitch classes (footnote 3).
/// </para>
/// </remarks>
public sealed class VoicingAndRegister : VariationOperation<VoicingParameters>
{
    public const string OperationId = "V3";

    private VoicingAndRegister()
    {
    }

    public static VoicingAndRegister Instance { get; } = new();

    public override string Id => OperationId;

    public override string Name => "Voicing and register";

    public override bool Supports(OperationScope scope) => true;

    protected override void Validate(VoicingParameters parameters)
    {
        if (!Enum.IsDefined(parameters.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), parameters, "Unknown voicing.");
        }
    }

    protected override string? Precondition(IReadOnlyList<Note> notes, VariationContext context, VoicingParameters parameters)
    {
        Words w = context.Words;
        if (!parameters.IsOctave && parameters.Scope != OperationScope.All)
        {
            return w.T(
                $"{Name} revoices the chords of the whole selection, so this kind can't be limited to {OperationScopes.Describe(parameters.Scope)}.",
                $"«{NameIn(w)}» меняет расположение аккордов всего выделения: ограничить шаг слоем «{OperationScopes.Describe(parameters.Scope, w)}» нельзя.");
        }

        if (parameters.Kind is VoicingKind.RootPosition or VoicingKind.FirstInversion or VoicingKind.SecondInversion && !KeepTable.AllowsInversions(context.Keep))
        {
            return NotAvailable(w, "changes the chord in the bass, and Keep bass is on", "меняется звук в басу, а бас нужно сохранить");
        }

        if (!parameters.IsOctave && MotifTexture.Read(notes).IsSingleLine)
        {
            return NotAvailable(w, "needs chords under the melody, and this selection is a single line", "это одна линия, а нужны аккорды под мелодией");
        }

        if (parameters.Kind == VoicingKind.OtherHand && !notes.Any(static note => note.Hand?.Value == Hand.Left))
        {
            return NotAvailable(w, "needs notes marked for the left hand", "нужны ноты, отмеченные для левой руки");
        }

        if (!parameters.IsOctave && HarmonyAnalyzer.FindChords(notes, context.Beats, context.Span, context.Key).Count == 0)
        {
            return NotAvailable(w, "found no chords to revoice", "не найдено аккордов, которые можно расположить иначе");
        }

        Note[] after = [.. Transform(notes, context, parameters)];
        if (after.Any(note => note.Pitch.MidiNumber is < OperationRules.LowestPitch or > OperationRules.HighestPitch))
        {
            return NotAvailable(w, "would move a note off the piano", "нота вышла бы за пределы клавиатуры");
        }

        if (context.Keep.Contains(KeepProperty.Bass) && !SameBass(notes, after))
        {
            return NotAvailable(w, "would move the bass, and Keep bass is on", "сдвинулся бы бас, а его нужно сохранить");
        }

        return after.Zip(notes).All(static pair => pair.First == pair.Second)
            ? w.T($"The chords already sit this way, so {Name} has nothing to change.", $"Аккорды уже так расположены: «{NameIn(w)}» ничего не изменит.")
            : null;
    }

    protected override IEnumerable<Note> Transform(IReadOnlyList<Note> notes, VariationContext context, VoicingParameters parameters)
    {
        if (parameters.IsOctave)
        {
            int shift = parameters.Kind == VoicingKind.OctaveUp ? 12 : -12;
            return notes.Select(note => note with { Pitch = new Pitch(Math.Clamp(note.Pitch.MidiNumber + shift, 0, Pitch.MaxMidiNumber)), Attribution = Attribution.UserEdited });
        }

        MotifTexture texture = MotifTexture.Read(notes);
        var melody = texture.Melody.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        var changed = new Dictionary<string, Note>(StringComparer.Ordinal);
        foreach (ChordSpan chord in HarmonyAnalyzer.FindChords(notes, context.Beats, context.Span, context.Key))
        {
            Note[] group = [.. notes.Where(note => !melody.Contains(note.Id) && chord.Span.Contains(note.Span.Onset))];
            Note[] voiced = Voiced(group, chord.Chord, parameters.Kind);
            Note[] top = [.. texture.Melody.Where(note => note.Span.Onset < chord.Span.End && note.Span.End > chord.Span.Onset)];
            bool reaches = voiced.Any(note => top.Any(line => line.Span.Onset < note.Span.End && line.Span.End > note.Span.Onset && line.Pitch.MidiNumber <= note.Pitch.MidiNumber));
            if (reaches)
            {
                continue;
            }

            foreach ((Note before, Note after) in group.Zip(voiced))
            {
                if (before != after)
                {
                    changed[before.Id] = after with { Attribution = Attribution.UserEdited };
                }
            }
        }

        return notes.Select(note => changed.TryGetValue(note.Id, out Note? after) ? after : note);
    }

    /// <summary>One chord's notes, in the group's order, voiced anew.</summary>
    private static Note[] Voiced(Note[] group, ChordLabel chord, VoicingKind kind)
    {
        int[] pitches = [.. group.Select(static note => note.Pitch.MidiNumber)];
        if (group.Length < 2)
        {
            return group;
        }

        switch (kind)
        {
            case VoicingKind.RootPosition or VoicingKind.FirstInversion or VoicingKind.SecondInversion:
                int tone = (chord.Root + chord.Intervals[(int)kind]) % 12;
                if (!pitches.Any(pitch => pitch % 12 == tone))
                {
                    return group;
                }

                int low = pitches.Min();
                if (low % 12 != tone)
                {
                    // The lowest note of that tone moves down by octaves until it is under the chord.
                    int index = Array.IndexOf(pitches, pitches.Where(pitch => pitch % 12 == tone).Min());
                    while (pitches[index] > low)
                    {
                        pitches[index] -= 12;
                    }
                }

                break;
            case VoicingKind.Close:
                int bass = pitches.Min();
                pitches = [.. pitches.Select(pitch => pitch == bass ? pitch : bass + ((((pitch - bass - 1) % 12) + 12) % 12) + 1)];
                break;
            case VoicingKind.Open:
                int lowest = pitches.Min();
                int[] upper = [.. pitches.Where(pitch => pitch != lowest).Distinct().OrderDescending()];
                if (upper.Length >= 2)
                {
                    // Drop the second-highest note an octave when it stays over the bass, or else raise the lowest upper note an octave.
                    int moved = upper[1] - 12 > lowest ? upper[1] : upper[^1];
                    int shift = upper[1] - 12 > lowest ? -12 : 12;
                    pitches = [.. pitches.Select(pitch => pitch == moved ? pitch + shift : pitch)];
                }

                break;
            case VoicingKind.OtherHand:
                Note[] left = [.. group.Where(static note => note.Hand?.Value == Hand.Left)];
                if (left.Length < 2)
                {
                    return group;
                }

                int highest = left.Max(static note => note.Pitch.MidiNumber);
                return [.. group.Select(note => note.Hand?.Value == Hand.Left && note.Pitch.MidiNumber == highest
                    ? note with { Hand = new Attributed<Hand>(Hand.Right, Attribution.UserEdited) }
                    : note)];
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown voicing.");
        }

        return [.. group.Select((note, i) => pitches[i] == note.Pitch.MidiNumber ? note : note with { Pitch = new Pitch(pitches[i]) })];
    }

    /// <summary>Whether the lowest note at every onset is the same before and after.</summary>
    private static bool SameBass(IReadOnlyList<Note> before, IReadOnlyList<Note> after)
    {
        static (long, int)[] Lowest(IEnumerable<Note> notes) => [.. notes.GroupBy(static note => note.Span.Onset.Value).OrderBy(static onset => onset.Key).Select(static onset => (onset.Key, onset.Min(static note => note.Pitch.MidiNumber)))];
        return Lowest(before).SequenceEqual(Lowest(after));
    }
}