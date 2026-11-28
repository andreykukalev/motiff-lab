using MotiffLab.MusicCore.Pieces;

namespace MotiffLab.MusicCore.Projects.Playlists;

/// <summary>Two copies of the playlists merged: the playlists and what of the copy that lost they do not hold.</summary>
internal sealed record MergedPlaylists(LibraryPlaylists Playlists, IReadOnlyList<MergeConflict> Conflicts);

/// <summary>What resolving this device's playlists against the online copy keeps (the 1.7 plan, item p-3 sends it).</summary>
/// <param name="Kept">The playlists to keep on this device.</param>
/// <param name="Upload">True when <paramref name="Kept"/> goes online over the online copy: it is not the online copy and not read-only.</param>
/// <param name="Merged">False when the copies were not merged, as one is a later build's: <paramref name="Kept"/> is the online copy as it came.</param>
/// <param name="Conflicts">What of the copy that lost <paramref name="Kept"/> does not hold.</param>
internal sealed record PlaylistsResolution(LibraryPlaylists Kept, bool Upload, bool Merged, IReadOnlyList<MergeConflict> Conflicts);

/// <summary>
/// The three-way merge of two copies of the library's playlists against the copy both last agreed on, by the principles of
/// the project's (<see cref="ProjectMerge"/>, 0026 S4, S5, C2): by playlist id, a playlist changed on one side only takes
/// that side; added on either side, it comes in; deleted on one side and unchanged on the other, or only rid of pieces there
/// (taken off, or forgotten as deleted), it goes; deleted on one side and renamed, given pieces or reordered on the other, it
/// stays with the change, so nothing the player added is lost (the lead's decision, 2026-11-28). Its name is a value as a whole: changed on both
/// differently, the copy saved last keeps its own and the merge records a conflict ("playlists.name"). Its pieces are a set:
/// each added or taken off on either side is so in the merge. Its order is the copy saved last's, unless only the other
/// copy reordered the pieces both still have since the base, then the other's; the pieces only the other copy added go at
/// the end in its order, as a piece added does; both reordered differently, a conflict ("playlists.pieces"). Without a base
/// the merge is a union with no deletions, the order the copy saved last's. A field this build does not know merges as a
/// whole value. Two playlists that come to share a name are one (<see cref="LibraryPlaylists.Joined"/>). Pure and
/// deterministic: the same three copies give the same playlists and conflicts.
/// </summary>
internal static class PlaylistsMerge
{
    /// <summary>
    /// This device's playlists against the online copy (<see cref="Merged"/>), the copy saved last winning; a copy a later
    /// build saved is never merged nor sent (as a project's, the 1.6 plan's decision 14): the online copy is kept as it came,
    /// and when this device's differs from it (from <paramref name="base"/>, when there is one), a conflict ("playlists")
    /// says this device's changes are not in it.
    /// </summary>
    public static PlaylistsResolution Resolve(LibraryPlaylists here, DateTimeOffset savedHere, LibraryPlaylists online, DateTimeOffset savedOnline, LibraryPlaylists? @base)
    {
        ArgumentNullException.ThrowIfNull(here);
        ArgumentNullException.ThrowIfNull(online);
        if (here.IsReadOnly || online.IsReadOnly)
        {
            bool lost = !here.Equals(@base ?? online) && !here.Equals(online);
            return new PlaylistsResolution(online, Upload: false, Merged: false, lost ? [new MergeConflict("playlists", null)] : []);
        }

        bool hereWins = savedHere >= savedOnline;
        MergedPlaylists merged = hereWins ? Merged(here, online, @base) : Merged(online, here, @base);
        bool upload = !PlaylistsFile.Write(merged.Playlists).AsSpan().SequenceEqual(PlaylistsFile.Write(online));
        return new PlaylistsResolution(merged.Playlists, upload, Merged: true, merged.Conflicts);
    }

    /// <summary><paramref name="winner"/> (the copy saved last) and <paramref name="loser"/> merged against <paramref name="base"/> (null: none kept, a union).</summary>
    public static MergedPlaylists Merged(LibraryPlaylists winner, LibraryPlaylists loser, LibraryPlaylists? @base)
    {
        ArgumentNullException.ThrowIfNull(winner);
        ArgumentNullException.ThrowIfNull(loser);
        List<MergeConflict> conflicts = [];
        List<string> ids = [.. winner.All.Select(static each => each.Id).Concat(loser.All.Select(static each => each.Id)).Distinct(StringComparer.Ordinal)];
        List<Playlist> kept = [];
        foreach (string id in ids)
        {
            Playlist? won = winner.Find(id), lost = loser.Find(id), was = @base?.Find(id);
            if (won is not null && lost is not null)
            {
                kept.Add(Both(won, lost, was, conflicts));
            }
            else if ((won ?? lost) is { } one && (was is null || !OnlyLess(one, was)))
            {
                // Added on one side, or deleted on the other and renamed, given pieces or reordered on this one: it stays.
                kept.Add(one);
            }
        }

        var playlists = new LibraryPlaylists
        {
            All = kept,
            Unknown = Fields(winner.Unknown, loser.Unknown, @base?.Unknown, @base is not null, null, conflicts),
        };
        return new MergedPlaylists(playlists.Joined(), conflicts);
    }

    /// <summary>Whether <paramref name="one"/> is <paramref name="was"/> with at most some of its pieces gone: the same name, fields and order.</summary>
    private static bool OnlyLess(Playlist one, Playlist was) =>
        one.Name == was.Name && one.Unknown.SequenceEqual(was.Unknown) && one.Pieces.SequenceEqual(was.Pieces.Where(one.Pieces.Contains), StringComparer.Ordinal);

    /// <summary>A playlist both copies have, merged against the base's (null: added on both under one id, a union).</summary>
    private static Playlist Both(Playlist won, Playlist lost, Playlist? was, List<MergeConflict> conflicts)
    {
        string name = Whole(won.Name, lost.Name, was?.Name, was is not null, "playlists.name", won.Id, conflicts)!;
        HashSet<string> members = [.. Members(won.Pieces, lost.Pieces, was?.Pieces)];
        bool wonMoved = was is not null && Reordered(won.Pieces, was.Pieces);
        bool lostMoved = was is not null && Reordered(lost.Pieces, was.Pieces);
        if (wonMoved && lostMoved && Reordered(won.Pieces, lost.Pieces))
        {
            conflicts.Add(new MergeConflict("playlists.pieces", won.Id));
        }

        (Playlist first, Playlist then) = lostMoved && !wonMoved ? (lost, won) : (won, lost);
        IReadOnlyList<string> pieces = LibraryPlaylists.Distinct([.. first.Pieces.Where(members.Contains), .. then.Pieces.Where(members.Contains)]);
        return new Playlist(won.Id, name, pieces) { Unknown = Fields(won.Unknown, lost.Unknown, was?.Unknown, was is not null, won.Id, conflicts) };
    }

    /// <summary>The pieces kept: those on both; one on one side only when that side added it since the base, not when the other took it off; without a base, those on either.</summary>
    private static IEnumerable<string> Members(IReadOnlyList<string> won, IReadOnlyList<string> lost, IReadOnlyList<string>? was) =>
        won.Concat(lost).Distinct(StringComparer.Ordinal).Where(piece =>
        {
            bool inWon = won.Contains(piece), inLost = lost.Contains(piece);
            return inWon == inLost ? inWon : was is null || (inWon == was.Contains(piece) ? inLost : inWon);
        });

    /// <summary>Whether the pieces <paramref name="pieces"/> and <paramref name="other"/> both hold stand in another order in each.</summary>
    private static bool Reordered(IReadOnlyList<string> pieces, IReadOnlyList<string> other) =>
        !pieces.Where(other.Contains).SequenceEqual(other.Where(pieces.Contains), StringComparer.Ordinal);

    /// <summary>The fields this build does not know, each merged as a whole value by its name.</summary>
    private static IReadOnlyList<UnknownField> Fields(IReadOnlyList<UnknownField> won, IReadOnlyList<UnknownField> lost, IReadOnlyList<UnknownField>? was, bool hasBase, string? id, List<MergeConflict> conflicts)
    {
        IEnumerable<string> names = won.Concat(lost).Select(static field => field.Name).Distinct(StringComparer.Ordinal);
        return [.. names.Select(name => (name, Json: Whole(Json(won, name), Json(lost, name), was is null ? null : Json(was, name), hasBase, id is null ? name : $"playlists.{name}", id, conflicts)))
            .Where(static field => field.Json is not null)
            .Select(static field => new UnknownField(field.name, field.Json!))];
    }

    private static string? Json(IReadOnlyList<UnknownField> fields, string name) => fields.FirstOrDefault(field => field.Name == name)?.Json;

    /// <summary>
    /// A value as a whole: the side that changed it since the base; with no base, the one there is; changed on both, the
    /// winner's, with a conflict (as <see cref="ProjectMerge"/>'s whole values).
    /// </summary>
    private static string? Whole(string? won, string? lost, string? was, bool hasBase, string path, string? id, List<MergeConflict> conflicts)
    {
        if (won == lost || (hasBase && lost == was))
        {
            return won;
        }

        if ((hasBase && won == was) || won is null)
        {
            return lost;
        }

        if (lost is not null)
        {
            conflicts.Add(new MergeConflict(path, id));
        }

        return won;
    }
}