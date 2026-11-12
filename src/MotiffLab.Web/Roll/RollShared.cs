using System.Globalization;
using System.Runtime.CompilerServices;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Playback;

namespace MotiffLab.Web.Roll;

/// <summary>
/// What the rolls of a page share (#419): the roll in the drawer and the one in focus that Expand opens show the same notes,
/// so music-core's payload and names for them are made once, and sent to the script under a key the script keeps them by.
/// A second roll on the same notes asks the script for the key first, and thousands of notes do not cross over again.
/// </summary>
internal static class RollShared
{
    private static readonly ConditionalWeakTable<object, string> s_ids = new();
    private static int s_next;
    private static (string Key, RollNames Names, IReadOnlyDictionary<string, string> Spelled)? s_names;

    /// <summary>A key for this object while it lives, the same for every roll; "-" for none.</summary>
    public static string Id(object? value) =>
        value is null ? "-" : s_ids.GetValue(value, _ => Interlocked.Increment(ref s_next).ToString(CultureInfo.InvariantCulture));

    /// <summary>The key of the notes as the roll draws them: the notes and the score whose bars they lie in.</summary>
    public static string NotesKey(IReadOnlyList<Note> notes, PlaybackScore score) => $"{Id(notes)}:{Id(score)}";

    /// <summary>The key of the names written for the notes: in the key signatures given, in the naming and language given.</summary>
    public static string NamesKey(string notesKey, IReadOnlyList<KeyChange>? keys, NoteNaming naming, string language) =>
        $"{notesKey}:{Id(keys)}:{naming}:{language}";

    /// <summary>The names under a key, with each note's name by its id, made by <paramref name="make"/> only for a new key.</summary>
    public static (RollNames Names, IReadOnlyDictionary<string, string> Spelled) Names(string key, IReadOnlyList<Note> notes, Func<RollNames> make)
    {
        if (s_names is { } kept && kept.Key == key)
        {
            return (kept.Names, kept.Spelled);
        }

        RollNames names = make();
        Dictionary<string, string> spelled = notes.Select((note, index) => (note.Id, Name: names.Notes[index])).ToDictionary(pair => pair.Id, pair => pair.Name, StringComparer.Ordinal);
        s_names = (key, names, spelled);
        return (names, spelled);
    }
}
