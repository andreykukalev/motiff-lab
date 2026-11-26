using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// A piece moved to another key (version 0.5, #351, decision record 0027 section 10): the notes moved by the interval
/// between the two tonics, their times, ids, hands and provenance unchanged; every key signature moved by as many fifths;
/// and how the moved notes are written. The piece itself is not changed: this is what a view shows and plays, and what a
/// derived edition can later keep.
/// </summary>
/// <param name="From">The key the piece is in.</param>
/// <param name="To">The key it is moved to.</param>
/// <param name="Semitones">How far each note moved: the nearer way, −6 to +5.</param>
internal sealed record TransposedPiece(MusicalKey From, MusicalKey To, int Semitones, IReadOnlyList<Note> Notes, IReadOnlyList<KeyChange> Keys, TransposedSpelling Spelling);

/// <summary>Transposition of a piece to another key of the same mode. Pure: the same piece and keys give the same result.</summary>
internal static class Transposition
{
    /// <summary>The semitones from one tonic to the other, the nearer way (−6 to +5): A minor to C minor is +3, to F♯ minor −3.</summary>
    public static int Semitones(MusicalKey from, MusicalKey to)
    {
        int up = Mod(to.TonicPitchClass - from.TonicPitchClass, 12);
        return up > 5 ? up - 12 : up;
    }

    /// <summary>
    /// The piece's <paramref name="notes"/>, in <paramref name="from"/> with its key signatures <paramref name="keys"/> (none:
    /// no sharps or flats), moved to <paramref name="to"/>. A note that would leave the MIDI range moves an octave less
    /// (<see cref="Moved(Pitch, int)"/>).
    /// </summary>
    /// <exception cref="ArgumentException">The keys are of different modes: changing the mode is not a transposition.</exception>
    public static TransposedPiece Of(IReadOnlyList<Note> notes, IEnumerable<KeyChange> keys, MusicalKey from, MusicalKey to)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(keys);
        if (from.Mode != to.Mode)
        {
            throw new ArgumentException("A piece moves to a key of its own mode.", nameof(to));
        }

        int semitones = Semitones(from, to);
        int fifths = to.Fifths - from.Fifths;
        KeyChange[] original = [.. keys.OrderBy(key => key.At)];
        if (original.Length == 0 || original[0].At > Tick.Zero)
        {
            original = [new KeyChange(Tick.Zero, 0), .. original];
        }

        var moved = new List<KeyChange>(original.Length);
        var sections = new List<(Tick At, int OriginalFifths, int Letters)>(original.Length);
        foreach (KeyChange key in original)
        {
            int written = Written(key.Fifths + fifths);
            moved.Add(new KeyChange(key.At, written));
            sections.Add((key.At, key.Fifths, Letters(semitones, written - key.Fifths)));
        }

        Note[] shiftedNotes = [.. notes.Select(note => note with { Pitch = Moved(note.Pitch, semitones) })];
        return new TransposedPiece(from, to, semitones, shiftedNotes, moved, new TransposedSpelling(semitones, sections));
    }

    /// <summary>
    /// The key signature of <paramref name="fifths"/> once its notes move by <paramref name="semitones"/>: as many fifths on
    /// as the interval is (seven semitones a fifth), a tritone the way the semitones go (six sharps more up, six flats more
    /// down), written as <see cref="Of"/> writes it. C major up two semitones is two sharps, D major up five is one, A minor
    /// up three is three flats, G major down a tritone five flats.
    /// </summary>
    public static int Moved(int fifths, int semitones)
    {
        int on = Mod(7 * semitones, 12);
        return Written(fifths + (on == 6 ? 6 * Math.Sign(semitones) : on > 6 ? on - 12 : on));
    }

    /// <summary>A pitch moved by <paramref name="semitones"/>; one that would leave the MIDI range moves by whole octaves less.</summary>
    public static Pitch Moved(Pitch pitch, int semitones) => new(Within(pitch.MidiNumber + semitones));

    /// <summary>
    /// The letters an interval of <paramref name="semitones"/> moves, for a key moved by <paramref name="fifths"/>: a fifth is
    /// four letters, so the count is 4 × fifths modulo 7, taken the way the semitones go (a minor third up is two letters).
    /// </summary>
    internal static int Letters(int semitones, int fifths)
    {
        int residue = Mod(4 * fifths, 7);
        double target = semitones * 7.0 / 12.0;
        return residue + (7 * (int)Math.Round((target - residue) / 7.0, MidpointRounding.AwayFromZero));
    }

    /// <summary>Beyond seven sharps or flats a key is written as its enharmonic, twelve fifths round: one letter apart.</summary>
    private static int Written(int shifted) => shifted > 7 ? shifted - 12 : shifted < -7 ? shifted + 12 : shifted;

    private static int Within(int midiNumber) =>
        midiNumber > Pitch.MaxMidiNumber ? Within(midiNumber - 12) : midiNumber < 0 ? Within(midiNumber + 12) : midiNumber;

    private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}