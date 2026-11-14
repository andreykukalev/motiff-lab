using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Notation;

/// <summary>
/// How notes moved to another key are written (version 0.5, #351): each as the original note is spelled in its own key,
/// moved by the interval's letters. Für Elise's D♯ in A minor is F♯ in C minor and E♯ in B minor, where the key signature
/// alone would give G♭ and F. A section whose key had to be written enharmonically (nine sharps as three flats) moves by
/// one letter more or less.
/// </summary>
public sealed class TransposedSpelling
{
    private static readonly int[] s_natural = [0, 2, 4, 5, 7, 9, 11];

    private readonly Section[] _sections;

    /// <param name="semitones">How far every note moved.</param>
    /// <param name="sections">From each tick on: the key the notes were written in before, and how many letters they move.</param>
    public TransposedSpelling(int semitones, IEnumerable<(Tick At, int OriginalFifths, int Letters)> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        Semitones = semitones;
        _sections = [.. sections.Select(section => new Section(section.At, section.OriginalFifths, section.Letters)).OrderBy(section => section.At)];
        if (_sections.Length == 0 || _sections.Any(section => Math.Abs(section.OriginalFifths) > 7))
        {
            throw new ArgumentException("At least one section, each in a key of seven sharps or flats at most.", nameof(sections));
        }
    }

    public int Semitones { get; }

    /// <summary>The letters a moved note is written with; the key of <paramref name="fifths"/> spells it where moving cannot.</summary>
    internal SpelledPitch Spell(Note note, int fifths)
    {
        int moved = note.Pitch.MidiNumber;
        Section section = _sections.LastOrDefault(candidate => candidate.At <= note.Span.Onset) ?? _sections[0];
        SpelledPitch original = PitchSpeller.Spell(moved - Semitones, section.OriginalFifths);
        int letter = (original.Octave * 7) + original.Step + section.Letters;
        int step = ((letter % 7) + 7) % 7;
        int octave = (letter - step) / 7;
        int alter = moved - ((12 * (octave + 1)) + s_natural[step]);
        return Math.Abs(alter) <= 2 ? new SpelledPitch(step, alter, octave) : PitchSpeller.Spell(moved, fifths);
    }

    private sealed record Section(Tick At, int OriginalFifths, int Letters);
}