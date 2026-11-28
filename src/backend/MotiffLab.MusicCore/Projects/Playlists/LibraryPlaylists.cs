using MotiffLab.MusicCore.Pieces;

namespace MotiffLab.MusicCore.Projects.Playlists;

/// <summary>
/// One playlist of the library (version 1.7, the score-reading spec 6; docs/playlists-file.md): its id, its name and its
/// pieces by their project ids, in the player's order. It exists until the player deletes it, empty or not.
/// </summary>
/// <param name="Id">"list_" and 32 hex digits, random for one the player makes (<see cref="IdSource"/>), of its name for one moved from the pieces (<see cref="LibraryPlaylists.Migrated"/>).</param>
/// <param name="Name">One line (<see cref="PieceMetadata.Clean"/>), unique in the library whatever the letter case.</param>
/// <param name="Pieces">The pieces' project ids, each once, in the player's order: new ones at the end.</param>
internal sealed record Playlist(string Id, string Name, IReadOnlyList<string> Pieces)
{
    /// <summary>The fields of the playlist this build does not know (a later build's), written back after its own (0026 S6).</summary>
    public IReadOnlyList<UnknownField> Unknown { get; init; } = [];

    public bool Equals(Playlist? other) =>
        other is not null && Id == other.Id && Name == other.Name && Pieces.SequenceEqual(other.Pieces, StringComparer.Ordinal) && Unknown.SequenceEqual(other.Unknown);

    public override int GetHashCode() => HashCode.Combine(Id, Name, Pieces.Count);
}

/// <summary>What an edit that names a playlist made: the playlists after it and the playlist made, or the one whose name was asked for.</summary>
/// <param name="Playlists">The playlists after the edit; as they were when <paramref name="Taken"/> is set.</param>
/// <param name="Id">The playlist made or renamed; null when the name was taken.</param>
/// <param name="Taken">The playlist that already has the name asked for (the spec's «то же имя → предлагаем открыть существующий»); null when none.</param>
internal sealed record PlaylistChange(LibraryPlaylists Playlists, string? Id, string? Taken);

/// <summary>
/// The library's playlists (version 1.7, the 1.7 plan's lead decision 3): the one owner of which pieces each playlist holds
/// and in what order, kept in a file of its own beside the project files (<see cref="PlaylistsFile"/>) and online as one
/// document per player. Each edit is pure and answers the playlists it makes; one read from a later version
/// (<see cref="IsReadOnly"/>) is never saved (<see cref="PlaylistsFile.Save"/>). The rules: a name is one line, unique
/// whatever the letter case; a playlist may be empty; a piece may be on several; pieces added go at the end, each once;
/// a playlist deleted leaves its pieces in the library; a piece the library no longer has goes from every playlist.
/// </summary>
internal sealed record LibraryPlaylists
{
    /// <summary>The prefix of a playlist's id ("pl_" is a draft's place's).</summary>
    public const string IdPrefix = "list_";

    /// <summary>No playlists: the library before the player makes one, or before the pieces' are moved here.</summary>
    public static LibraryPlaylists None { get; } = new();

    /// <summary>The playlists in the order they were made (the library shows them in its own order).</summary>
    public IReadOnlyList<Playlist> All { get; init; } = [];

    /// <summary>The top-level fields this build does not know (a later build's), written back after its own.</summary>
    public IReadOnlyList<UnknownField> Unknown { get; init; } = [];

    /// <summary>The version and "minReader" of a file a later build wrote, read read-only; null for one of this build.</summary>
    public NewerFile? Newer { get; init; }

    /// <summary>Read from a later version: shown, never saved here (0026 S6).</summary>
    public bool IsReadOnly => Newer is not null;

    public bool Equals(LibraryPlaylists? other) =>
        other is not null && All.SequenceEqual(other.All) && Unknown.SequenceEqual(other.Unknown) && Newer == other.Newer;

    public override int GetHashCode() => HashCode.Combine(All.Count, Unknown.Count, Newer);

    /// <summary>The playlist with this id, or null.</summary>
    public Playlist? Find(string id) => All.FirstOrDefault(playlist => playlist.Id == id);

    /// <summary>The playlist with this name whatever the letter case and the white space, or null.</summary>
    public Playlist? Named(string name) =>
        PieceMetadata.Clean(name) is { } cleaned ? All.FirstOrDefault(playlist => SameName(playlist.Name, cleaned)) : null;

    /// <summary>
    /// The playlists the library had while a playlist was only a name in each piece's "metadata"."playlists" (before 1.7),
    /// so nothing is lost: <paramref name="pieces"/> in the order the library lists them (a playlist's pieces show in it),
    /// each with its playlists' names. A playlist comes in the order its name is first met, spelled as first met, its pieces
    /// in the order given. Its id is of its name (<see cref="IdSource.Hashed"/> of "playlist|" and the name in lower case), so
    /// two devices that move the same pieces make the same playlists, which then merge as one (<see cref="PlaylistsMerge"/>).
    /// </summary>
    public static LibraryPlaylists Migrated(IEnumerable<(string Piece, IReadOnlyList<string> Playlists)> pieces)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        LibraryPlaylists made = None;
        foreach ((string piece, IReadOnlyList<string> names) in pieces)
        {
            foreach (string name in names.Select(PieceMetadata.Clean).OfType<string>())
            {
                Playlist playlist = made.Named(name) ?? new Playlist(IdSource.Hashed(IdPrefix, "playlist|" + name.ToLowerInvariant(), made.All.Select(static each => each.Id)), name, []);
                made = made.Put(playlist with { Pieces = Distinct([.. playlist.Pieces, piece]) });
            }
        }

        return made;
    }

    /// <summary>A new playlist at the end, empty or with <paramref name="pieces"/> in their order, unless the name is taken.</summary>
    /// <exception cref="ArgumentException">The name is empty.</exception>
    public PlaylistChange Created(string name, IEnumerable<string> pieces, IdSource ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        string cleaned = Cleaned(name);
        if (Named(cleaned) is { } taken)
        {
            return new PlaylistChange(this, null, taken.Id);
        }

        var playlist = new Playlist(ids.Next(IdPrefix, All.Select(static each => each.Id)), cleaned, Distinct(pieces));
        return new PlaylistChange(Put(playlist), playlist.Id, null);
    }

    /// <summary>The playlist renamed, unless another one has the name; the same one may change only its letter case.</summary>
    /// <exception cref="ArgumentException">The name is empty.</exception>
    public PlaylistChange Renamed(string id, string name)
    {
        string cleaned = Cleaned(name);
        Playlist playlist = Get(id);
        return Named(cleaned) is { } taken && taken.Id != id
            ? new PlaylistChange(this, null, taken.Id)
            : new PlaylistChange(Put(playlist with { Name = cleaned }), id, null);
    }

    /// <summary>The playlist deleted; its pieces stay in the library.</summary>
    public LibraryPlaylists Deleted(string id)
    {
        Get(id);
        return this with { All = [.. All.Where(playlist => playlist.Id != id)] };
    }

    /// <summary><paramref name="pieces"/> at the end of the playlist in their order, but those already on it, which stay where they are.</summary>
    public LibraryPlaylists Added(string id, IEnumerable<string> pieces)
    {
        Playlist playlist = Get(id);
        return Put(playlist with { Pieces = Distinct([.. playlist.Pieces, .. pieces]) });
    }

    /// <summary><paramref name="pieces"/> taken off the playlist; they stay in the library and on other playlists.</summary>
    public LibraryPlaylists Removed(string id, IEnumerable<string> pieces)
    {
        Playlist playlist = Get(id);
        HashSet<string> gone = [.. pieces];
        return Put(playlist with { Pieces = [.. playlist.Pieces.Where(piece => !gone.Contains(piece))] });
    }

    /// <summary>
    /// <paramref name="pieces"/> of the playlist moved, in the order given, before <paramref name="before"/>, or to the end
    /// when it is null: a drag of one or several rows. A piece not on the playlist is passed by.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="before"/> is not on the playlist, or is one of the pieces moved.</exception>
    public LibraryPlaylists Moved(string id, IEnumerable<string> pieces, string? before)
    {
        Playlist playlist = Get(id);
        List<string> moved = [.. Distinct(pieces).Where(playlist.Pieces.Contains)];
        if (before is not null && (!playlist.Pieces.Contains(before) || moved.Contains(before)))
        {
            throw new ArgumentException($"\"{before}\" is not a piece of the playlist that stays where it is.", nameof(before));
        }

        List<string> rest = [.. playlist.Pieces.Where(piece => !moved.Contains(piece))];
        rest.InsertRange(before is null ? rest.Count : rest.IndexOf(before), moved);
        return Put(playlist with { Pieces = rest });
    }

    /// <summary>The pieces the library no longer has taken off every playlist (a piece deleted, here or on another device).</summary>
    public LibraryPlaylists Forgotten(IEnumerable<string> pieces)
    {
        HashSet<string> gone = [.. pieces];
        return this with { All = [.. All.Select(playlist => playlist with { Pieces = [.. playlist.Pieces.Where(piece => !gone.Contains(piece))] })] };
    }

    /// <summary>
    /// Each piece under the id it has now where a playlist holds it by one it had before (<see cref="Project.FormerIds"/>, a
    /// numbered id re-keyed or a copy merged under the online id, #317), so a re-keyed piece stays on its playlists.
    /// </summary>
    public LibraryPlaylists Rekeyed(IReadOnlyDictionary<string, string> current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return this with { All = [.. All.Select(playlist => playlist with { Pieces = Distinct(playlist.Pieces.Select(piece => current.GetValueOrDefault(piece, piece))) })] };
    }

    /// <summary>
    /// The playlists with one per name (whatever the letter case): a later one with the name of an earlier one joins it, its
    /// pieces after the earlier one's, its id gone. Two playlists made apart under one name (on two devices) are one after a
    /// merge (<see cref="PlaylistsMerge"/>), and one written so by another writer reads as one.
    /// </summary>
    public LibraryPlaylists Joined()
    {
        List<Playlist> kept = [];
        foreach (Playlist playlist in All)
        {
            int same = kept.FindIndex(each => SameName(each.Name, playlist.Name));
            if (same < 0)
            {
                kept.Add(playlist);
            }
            else
            {
                kept[same] = kept[same] with { Pieces = Distinct([.. kept[same].Pieces, .. playlist.Pieces]) };
            }
        }

        return kept.Count == All.Count ? this : this with { All = kept };
    }

    public static bool SameName(string one, string other) => string.Equals(one, other, StringComparison.OrdinalIgnoreCase);

    /// <summary>The ids in their order, each once, the empty ones dropped.</summary>
    public static IReadOnlyList<string> Distinct(IEnumerable<string> pieces)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        HashSet<string> seen = new(StringComparer.Ordinal);
        return [.. pieces.Where(piece => !string.IsNullOrWhiteSpace(piece) && seen.Add(piece))];
    }

    private static string Cleaned(string name) => PieceMetadata.Clean(name) ?? throw new ArgumentException("A playlist needs a name.", nameof(name));

    /// <summary>The playlist with this id.</summary>
    /// <exception cref="KeyNotFoundException">There is none.</exception>
    private Playlist Get(string id) => Find(id) ?? throw new KeyNotFoundException($"There is no playlist \"{id}\".");

    /// <summary>The playlist in the place of the one with its id, or at the end.</summary>
    private LibraryPlaylists Put(Playlist playlist) =>
        Find(playlist.Id) is null
            ? this with { All = [.. All, playlist] }
            : this with { All = [.. All.Select(each => each.Id == playlist.Id ? playlist : each)] };
}