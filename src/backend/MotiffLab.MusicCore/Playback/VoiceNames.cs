using MotiffLab.MusicCore.Events;

namespace MotiffLab.MusicCore.Playback;

/// <summary>
/// The names a player understands for the voices a file writes (#330, criterion 4), only where the file says which voice
/// is above: in a track of two voices, each written on a staff of its own, the one on the upper staff is the upper voice
/// and the other the lower voice. Any other voice gets no name here and is shown by its number as the file writes it.
/// A note with no place in a MusicXML file (one the player added) says nothing of its staff. Pure: the same notes give
/// the same names.
/// </summary>
public static class VoiceNames
{
    public static IReadOnlyList<VoiceName> Of(IReadOnlyList<Note> notes, Words words)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var named = new List<VoiceName>();
        foreach (IGrouping<int, Note> track in notes.Where(note => note.Voice is not null).GroupBy(note => note.Track).OrderBy(track => track.Key))
        {
            (int Voice, int[] Staves)[] voices =
            [
                .. track.GroupBy(note => note.Voice!.Value).OrderBy(voice => voice.Key).Select(voice => (voice.Key, Staves(voice))),
            ];
            if (voices.Length != 2 || voices.Any(voice => voice.Staves.Length != 1) || voices[0].Staves[0] == voices[1].Staves[0])
            {
                continue;
            }

            int upper = voices[0].Staves[0] < voices[1].Staves[0] ? 0 : 1;
            named.Add(new(track.Key, voices[upper].Voice, words.T("upper voice", "верхний голос")));
            named.Add(new(track.Key, voices[1 - upper].Voice, words.T("lower voice", "нижний голос")));
        }

        return named;
    }

    /// <summary>The staves, numbered from 1 as the file numbers them, that a voice's notes are written on.</summary>
    private static int[] Staves(IEnumerable<Note> voice) =>
        [.. voice.Select(note => note.Source).OfType<MusicXmlSourceLocation>().Select(place => place.Staff).Distinct()];
}

/// <summary>A voice of a track (<see cref="Note.Track"/>, <see cref="Note.Voice"/>) and the name a player understands.</summary>
public sealed record VoiceName(int Track, int Voice, string Name);