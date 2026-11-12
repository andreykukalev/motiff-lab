using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// The scales of the Reference: the major, the three minors, the church modes and the two pentatonics. A key card shows the
/// major for a major key, the natural and the harmonic minor for a minor one; a scale's own card takes any of them.
/// </summary>
public enum ScaleForm
{
    Major,
    NaturalMinor,
    HarmonicMinor,
    MelodicMinor,
    Dorian,
    Phrygian,
    Lydian,
    Mixolydian,
    Locrian,
    MajorPentatonic,
    MinorPentatonic,
}

/// <summary>A scale the Reference offers: its id ("dorian") and its name in words ("Dorian", «дорийский»).</summary>
public sealed record ScaleFormChoice(ScaleForm Form, string Id, string Name);

/// <summary>A scale's own card (the Reference, part 2): its notes spelled by letter from the tonic, its degrees, and the scale up and down to hear.</summary>
/// <param name="Name">The tonic and the scale: "D Dorian", «ре дорийский».</param>
/// <param name="Title">The scale alone: "Dorian", «дорийский».</param>
/// <param name="Formula">Each note's degree against the major scale of the tonic: "1 2 ♭3 4 5 6 ♭7".</param>
/// <param name="Notes">The notes up from the tonic, spelled for the naming: five for a pentatonic, seven for the rest.</param>
/// <param name="Descending">The way down when it differs from the way up (the melodic minor comes down natural), from the octave; else empty.</param>
/// <param name="Sound">The scale up to the octave and down again, a quaver a note; the keyboard marks the way up.</param>
public sealed record ScaleCard(
    SpelledName Tonic,
    ScaleForm Form,
    string Name,
    string Title,
    IReadOnlyList<string> Formula,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> Descending,
    ReferenceSound Sound);

/// <summary>Spells and sounds the Reference's scales (<see cref="ScaleForm"/>) from any of its roots. Pure theory.</summary>
public static class ScaleReference
{
    /// <summary>The id of a scale card's sound, which the facade's reference.sound takes.</summary>
    public const string SoundId = "scale";

    /// <summary>A quaver: the time from one note of a scale card's sound to the next.</summary>
    public const long Step = CanonicalTime.TicksPerQuarter / 2;

    private static readonly int[] s_majorSemitones = [0, 2, 4, 5, 7, 9, 11];

    private static readonly int[] s_heptatonicSteps = [0, 1, 2, 3, 4, 5, 6];

    /// <summary>The scales in the order the Reference offers them, with their ids and names.</summary>
    public static IReadOnlyList<ScaleFormChoice> Forms(Words words = default) =>
        [.. Enum.GetValues<ScaleForm>().Select(form => new ScaleFormChoice(form, Id(form), Title(form, words)))];

    /// <summary>"major", "naturalMinor", "dorian", "minorPentatonic": the id the facade takes.</summary>
    public static string Id(ScaleForm form)
    {
        string name = form.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>The scale with this id, or null.</summary>
    public static ScaleForm? Parse(string? id) => Enum.GetValues<ScaleForm>().Cast<ScaleForm?>().FirstOrDefault(form => Id(form!.Value) == id);

    /// <summary>The scale's name alone: "harmonic minor", "Dorian"; «гармонический минор», «дорийский».</summary>
    public static string Title(ScaleForm form, Words words = default) => form switch
    {
        ScaleForm.Major => words.T("major", "мажор"),
        ScaleForm.NaturalMinor => words.T("natural minor", "натуральный минор"),
        ScaleForm.HarmonicMinor => words.T("harmonic minor", "гармонический минор"),
        ScaleForm.MelodicMinor => words.T("melodic minor", "мелодический минор"),
        ScaleForm.Dorian => words.T("Dorian", "дорийский"),
        ScaleForm.Phrygian => words.T("Phrygian", "фригийский"),
        ScaleForm.Lydian => words.T("Lydian", "лидийский"),
        ScaleForm.Mixolydian => words.T("Mixolydian", "миксолидийский"),
        ScaleForm.Locrian => words.T("Locrian", "локрийский"),
        ScaleForm.MajorPentatonic => words.T("major pentatonic", "мажорная пентатоника"),
        _ => words.T("minor pentatonic", "минорная пентатоника"),
    };

    /// <summary>The scale's notes up from <paramref name="tonic"/>, each on its own letter: D♯ Lydian has F𝄪 and G𝄪.</summary>
    public static IReadOnlyList<SpelledName> Spell(SpelledName tonic, ScaleForm form)
    {
        (int[] steps, int[] semitones) = Degrees(form);
        return [.. steps.Select((step, index) => SpelledName.On(tonic.Step + step, tonic.PitchClass + semitones[index]))];
    }

    /// <summary>The card of a scale from a root (<see cref="KeyReference.Roots"/>).</summary>
    public static ScaleCard For(SpelledName tonic, ScaleForm form, NoteNaming naming = NoteNaming.Letters, Words words = default)
    {
        (int[] steps, int[] semitones) = Degrees(form);
        IReadOnlyList<SpelledName> up = Spell(tonic, form);
        int start = KeyReference.Around(tonic.PitchClass);
        int[] upward = [.. semitones.Select(semitone => start + semitone), start + 12];
        ScaleForm downForm = form == ScaleForm.MelodicMinor ? ScaleForm.NaturalMinor : form;
        int[] downward = [.. Enumerable.Reverse(Degrees(downForm).Semitones).Select(semitone => start + semitone)];
        string[] descending = form == ScaleForm.MelodicMinor
            ? [tonic.Name(naming), .. Enumerable.Reverse(Spell(tonic, downForm)).Select(note => note.Name(naming))]
            : [];
        ReferenceSound sound = KeyReference.Sound(SoundId, [.. upward, .. downward], together: false) with { Step = Step, Keys = upward };
        string title = Title(form, words);
        return new ScaleCard(
            tonic,
            form,
            $"{tonic.Name(naming)} {title}",
            title,
            [.. steps.Select((step, index) => DegreeLabel(step, semitones[index]))],
            [.. up.Select(note => note.Name(naming))],
            descending,
            sound);
    }

    /// <summary>
    /// A degree as a formula writes it, against the major scale from the same root: "3" for a major third, "♭3" for a minor
    /// one, "♯4", "𝄫7", "9". <paramref name="steps"/> counts letters above the root, <paramref name="semitones"/> the distance.
    /// </summary>
    internal static string DegreeLabel(int steps, int semitones)
    {
        int natural = (12 * (steps / 7)) + s_majorSemitones[steps % 7];
        string accidental = (semitones - natural) switch
        {
            -2 => "𝄫",
            -1 => "♭",
            1 => "♯",
            2 => "𝄪",
            _ => "",
        };
        return $"{accidental}{steps + 1}";
    }

    /// <summary>Each degree of a scale: the letters above the tonic and the semitones.</summary>
    private static (int[] Steps, int[] Semitones) Degrees(ScaleForm form) => form switch
    {
        ScaleForm.Major => (s_heptatonicSteps, s_majorSemitones),
        ScaleForm.NaturalMinor => (s_heptatonicSteps, [0, 2, 3, 5, 7, 8, 10]),
        ScaleForm.HarmonicMinor => (s_heptatonicSteps, [0, 2, 3, 5, 7, 8, 11]),
        ScaleForm.MelodicMinor => (s_heptatonicSteps, [0, 2, 3, 5, 7, 9, 11]),
        ScaleForm.Dorian => (s_heptatonicSteps, [0, 2, 3, 5, 7, 9, 10]),
        ScaleForm.Phrygian => (s_heptatonicSteps, [0, 1, 3, 5, 7, 8, 10]),
        ScaleForm.Lydian => (s_heptatonicSteps, [0, 2, 4, 6, 7, 9, 11]),
        ScaleForm.Mixolydian => (s_heptatonicSteps, [0, 2, 4, 5, 7, 9, 10]),
        ScaleForm.Locrian => (s_heptatonicSteps, [0, 1, 3, 5, 6, 8, 10]),
        ScaleForm.MajorPentatonic => ([0, 1, 2, 4, 5], [0, 2, 4, 7, 9]),
        _ => ([0, 2, 3, 4, 6], [0, 3, 5, 7, 10]),
    };
}