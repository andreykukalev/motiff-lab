using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;

namespace MotiffLab.MusicCore.Variations;

/// <summary>
/// Which of the selected notes a step may change. The voices are read from the selection's texture
/// (<see cref="MotifTexture"/>), the same reading analysis and Keep checks use, so "the melody" means
/// the same line everywhere. A hand is not a voice: in Für Elise the right hand also plays the
/// accompaniment's arpeggios.
/// </summary>
public enum OperationScope
{
    /// <summary>Every selected note.</summary>
    All,

    /// <summary>The melody line: the top line of the texture, or every note of a single line.</summary>
    Melody,

    /// <summary>Every selected note outside the melody line.</summary>
    Accompaniment,

    /// <summary>The bass line: the bottom line of the texture. A single line has none.</summary>
    Bass,
}

/// <summary>Splits notes by <see cref="OperationScope"/>. Pure: the same notes and scope give the same split.</summary>
public static class OperationScopes
{
    /// <summary>The notes the scope covers, and the others, each in the order of <paramref name="notes"/>.</summary>
    public static (Note[] Inside, Note[] Outside) Split(IReadOnlyList<Note> notes, OperationScope scope)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (scope == OperationScope.All)
        {
            return ([.. notes], []);
        }

        MotifTexture texture = MotifTexture.Read(notes);
        HashSet<string> melody = texture.Melody.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        HashSet<string> ids = scope switch
        {
            OperationScope.Melody => melody,
            OperationScope.Accompaniment => texture.Notes.Where(note => !melody.Contains(note.Id)).Select(static note => note.Id).ToHashSet(StringComparer.Ordinal),
            OperationScope.Bass => texture.Bass.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown scope."),
        };
        return ([.. notes.Where(note => ids.Contains(note.Id))], [.. notes.Where(note => !ids.Contains(note.Id))]);
    }

    /// <summary>"the melody", "the accompaniment", "the bass" or "the selection", completing "limited to …".</summary>
    public static string Describe(OperationScope scope) => scope switch
    {
        OperationScope.All => "the selection",
        OperationScope.Melody => "the melody",
        OperationScope.Accompaniment => "the accompaniment",
        OperationScope.Bass => "the bass",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown scope."),
    };
}