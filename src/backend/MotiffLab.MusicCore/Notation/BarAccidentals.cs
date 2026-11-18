using MotiffLab.MusicCore.Events;

namespace MotiffLab.MusicCore.Notation;

/// <summary>The spelling of a staff's notes in a bar, from the key signature, and the accidentals shown.</summary>
internal static class BarAccidentals
{
    /// <summary>Spells each chord piece of the layers and marks the accidentals it shows: per bar and staff, in time order across voices; tied continuations carry theirs silently.</summary>
    public static void Write(List<List<Element>> layers, int fifths, TransposedSpelling? spelling)
    {
        int[] key = PitchSpeller.KeyAlterations(fifths);
        var current = new Dictionary<(int Step, int Octave), int>();
        IEnumerable<ChordPiece> pieces = layers
            .SelectMany((layer, index) => Flatten(layer).OfType<ChordPiece>().Select(piece => (piece, index)))
            .OrderBy(entry => entry.piece.Start)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.piece);

        foreach (ChordPiece piece in pieces)
        {
            piece.Spellings = [.. piece.Chord.Notes.Select(note => spelling?.Spell(note.Note, fifths) ?? PitchSpeller.Spell(note.Note.Pitch.MidiNumber, fifths))];
            piece.ShowAccidental = new bool[piece.Spellings.Length];
            for (int i = 0; i < piece.Spellings.Length; i++)
            {
                SpelledPitch spelled = piece.Spellings[i];
                if (piece.TieIn)
                {
                    continue;
                }

                int inEffect = current.TryGetValue((spelled.Step, spelled.Octave), out int alter) ? alter : key[spelled.Step];
                if (spelled.Alter != inEffect)
                {
                    piece.ShowAccidental[i] = true;
                    current[(spelled.Step, spelled.Octave)] = spelled.Alter;
                }
            }
        }
    }

    private static IEnumerable<Element> Flatten(IEnumerable<Element> elements) =>
        elements.SelectMany(element => element switch
        {
            Beamed beam => Flatten(beam.Elements),
            TupletGroup tuplet => Flatten(tuplet.Elements),
            _ => [element],
        });
}