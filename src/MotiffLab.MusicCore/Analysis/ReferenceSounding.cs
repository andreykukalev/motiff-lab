using MotiffLab.MusicCore.Notation;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>How a chord's tones are spread (version 0.5): close, all within an octave above the bass, or open.</summary>
public enum ChordSpacing
{
    /// <summary>The tones above the bass within an octave of it: G7 as B3 D4 F4 G4.</summary>
    Close,

    /// <summary>The first, third, … voices from the bottom an octave lower: G7 as B2 F3 D4 G4, C as C3 G3 E4.</summary>
    Open,
}

/// <summary>How a chord is played in the Reference (version 0.5).</summary>
public enum ChordPattern
{
    /// <summary>Struck at once.</summary>
    Together,

    /// <summary>Broken from the bass up.</summary>
    Up,

    /// <summary>Broken from the top down.</summary>
    Down,

    /// <summary>Up and back down, the top once.</summary>
    UpAndDown,

    /// <summary>Broken by skips, each voice then the one two above it, up to the top: C G E C for C E G C.</summary>
    Broken,
}

/// <summary>Which way a scale is played in the Reference (version 0.5).</summary>
public enum ScalePattern
{
    Up,
    Down,
    UpAndDown,
}

/// <summary>A chord as the Reference sounds it: one position, spread and played one way.</summary>
/// <param name="Position">The position of the chord's card (root position or an inversion), with its title and figure.</param>
/// <param name="Tones">The tones from the bass up as they sound, spelled.</param>
/// <param name="Music">The chord struck and held for a bar, then, unless struck at once, played the chosen way in quavers.</param>
public sealed record ChordSounding(ChordCard Card, ChordPosition Position, ChordSpacing Spacing, ChordPattern Pattern, IReadOnlyList<WrittenTone> Tones, ReferenceMusic Music);

/// <summary>A scale as the Reference sounds it: up, down or both, over one or two octaves, in quavers on a treble staff.</summary>
public sealed record ScaleSounding(ScaleCard Card, ScalePattern Pattern, int Octaves, ReferenceMusic Music);

/// <summary>
/// How the Reference's chords and scales sound (version 0.5, «Аккорды и гаммы»): what sounds is chosen apart from how it
/// sounds, the position, the spacing and the way it is played. Pure theory; the same choices give the same notes.
/// </summary>
public static class ReferenceSounding
{
    /// <summary>The tempo the Reference plays at, as <see cref="KeyReference.MicrosecondsPerQuarter"/>: a quarter at 100.</summary>
    public const int MicrosecondsPerQuarter = KeyReference.MicrosecondsPerQuarter;

    /// <summary>
    /// The chord of <paramref name="card"/> in position <paramref name="inversion"/>, spread and played as chosen. On a piano's
    /// two staves split at middle C; a broken chord rings, its quavers in one layer crossing the staves.
    /// </summary>
    public static ChordSounding Chord(ChordCard card, int inversion, ChordSpacing spacing, ChordPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(card);
        IReadOnlyList<WrittenTone> close = ChordReference.Close(card.Root, card.Type, inversion);
        IReadOnlyList<WrittenTone> tones = spacing == ChordSpacing.Open ? Spread(close) : close;
        var score = new ReferenceScore(grandStaff: true, fifths: 0);
        score.Chord(0, tones);
        if (pattern != ChordPattern.Together)
        {
            score.Run(1, 0, Order(tones, pattern), ringing: true);
        }

        return new ChordSounding(card, card.Positions[inversion], spacing, pattern, tones, score.Build(MicrosecondsPerQuarter, tones));
    }

    /// <summary>
    /// The scale of <paramref name="card"/> over <paramref name="octaves"/> (1 or 2) from its tonic around middle C, played
    /// as chosen, in the key signature of its tonic's major key (minor key for the minors and the modes on a minor third),
    /// or none beyond seven sharps or flats. The melodic minor comes down natural. The keyboard marks the way up.
    /// </summary>
    public static ScaleSounding Scale(ScaleCard card, ScalePattern pattern, int octaves)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentOutOfRangeException.ThrowIfLessThan(octaves, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(octaves, 2);
        IReadOnlyList<WrittenTone> up = Ladder(card.Tonic, card.Form, octaves);
        ScaleForm downForm = card.Form == ScaleForm.MelodicMinor ? ScaleForm.NaturalMinor : card.Form;
        IReadOnlyList<WrittenTone> down = [.. Ladder(card.Tonic, downForm, octaves).Reverse()];
        WrittenTone[] played = pattern switch
        {
            ScalePattern.Up => [.. up],
            ScalePattern.Down => [.. down],
            _ => [.. up, .. down.Skip(1)],
        };
        var score = new ReferenceScore(grandStaff: false, fifths: SignatureOf(card.Tonic, card.Form));
        score.Run(0, 0, played, ringing: false, staff: 1);
        return new ScaleSounding(card, pattern, octaves, score.Build(MicrosecondsPerQuarter, up));
    }

    /// <summary>The scale's notes up from the tonic around middle C to the top tonic, over the octaves.</summary>
    private static WrittenTone[] Ladder(SpelledName tonic, ScaleForm form, int octaves)
    {
        IReadOnlyList<SpelledName> notes = ScaleReference.Spell(tonic, form);
        int start = KeyReference.Around(tonic.PitchClass);
        var ladder = new List<WrittenTone>();
        int previous = start - 1;
        for (int octave = 0; octave < octaves; octave++)
        {
            foreach (SpelledName note in notes)
            {
                int pitch = previous + 1 + ((((note.PitchClass - previous - 1) % 12) + 12) % 12);
                ladder.Add(new WrittenTone(pitch, note));
                previous = pitch;
            }
        }

        ladder.Add(new WrittenTone(start + (12 * octaves), tonic));
        return [.. ladder];
    }

    /// <summary>Sharps or flats of the key the scale is written in: its tonic's major or minor key; 0 beyond seven.</summary>
    private static int SignatureOf(SpelledName tonic, ScaleForm form)
    {
        int[] naturalFifths = [0, 2, 4, -1, 1, 3, 5];
        bool minor = form is ScaleForm.NaturalMinor or ScaleForm.HarmonicMinor or ScaleForm.MelodicMinor
            or ScaleForm.Dorian or ScaleForm.Phrygian or ScaleForm.Locrian or ScaleForm.MinorPentatonic;
        int fifths = naturalFifths[tonic.Step] + (7 * tonic.Alter) - (minor ? 3 : 0);
        return Math.Abs(fifths) <= 7 ? fifths : 0;
    }

    /// <summary>Open spacing: the first, third, … voices from the bottom an octave lower, from the bass up again.</summary>
    internal static IReadOnlyList<WrittenTone> Spread(IReadOnlyList<WrittenTone> close) =>
        [.. close.Select((tone, index) => index % 2 == 0 ? tone with { Midi = tone.Midi - 12 } : tone).OrderBy(tone => tone.Midi)];

    /// <summary>The tones, from the bass up, in the order the pattern plays them.</summary>
    internal static IReadOnlyList<WrittenTone> Order(IReadOnlyList<WrittenTone> tones, ChordPattern pattern) => pattern switch
    {
        ChordPattern.Up => tones,
        ChordPattern.Down => [.. tones.Reverse()],
        ChordPattern.UpAndDown => [.. tones, .. tones.Reverse().Skip(1)],
        ChordPattern.Broken => [.. Zigzag(tones.Count).Select(index => tones[index])],
        _ => tones,
    };

    /// <summary>0 2 1 3 2 4 …: each voice and then the one two above it, until every voice has sounded.</summary>
    private static IEnumerable<int> Zigzag(int count)
    {
        var heard = new HashSet<int>();
        for (int step = 0; heard.Count < count; step++)
        {
            int index = Math.Min(count - 1, step % 2 == 0 ? step / 2 : (step + 3) / 2);
            heard.Add(index);
            yield return index;
        }
    }
}