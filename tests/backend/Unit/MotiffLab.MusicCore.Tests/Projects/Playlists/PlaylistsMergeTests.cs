using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Playlists;

namespace MotiffLab.MusicCore.Tests.Projects.Playlists;

/// <summary>
/// The three-way merge of two copies of the playlists against the copy both last agreed on (docs/playlists-file.md, "Sync"),
/// by the principles of the project's (c-17a, <see cref="ProjectMerge"/>): by id, the side that changed wins, deleted and
/// changed keeps the change, both changed keeps the copy saved last's with a conflict, no base a union.
/// </summary>
public class PlaylistsMergeTests
{
    private static readonly DateTimeOffset s_earlier = new(2026, 11, 28, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset s_later = s_earlier.AddMinutes(5);

    private static LibraryPlaylists Base() => LibraryPlaylistsTests.Two();

    private static Playlist Bach(MergedPlaylists merged) => merged.Playlists.Find("list_0001")!;

    [Fact]
    public void What_one_copy_changed_comes_in_and_nothing_is_a_conflict()
    {
        LibraryPlaylists mine = Base().Renamed("list_0001", "Бах, инвенции").Playlists;
        LibraryPlaylists theirs = Base().Added("list_0002", ["p_d"]).Created("Экзамен", ["p_a"], IdSource.Numbered).Playlists;

        MergedPlaylists merged = PlaylistsMerge.Merged(mine, theirs, Base());

        Assert.Equal(["Бах, инвенции", "Шопен", "Экзамен"], merged.Playlists.All.Select(static playlist => playlist.Name));
        Assert.Equal(["p_c", "p_d"], merged.Playlists.Find("list_0002")!.Pieces);
        Assert.Empty(merged.Conflicts);
    }

    [Fact]
    public void Renamed_on_both_differently_the_copy_saved_last_keeps_its_name_and_the_merge_records_the_conflict()
    {
        LibraryPlaylists mine = Base().Renamed("list_0001", "Бах I").Playlists;
        LibraryPlaylists theirs = Base().Renamed("list_0001", "Бах II").Playlists;

        MergedPlaylists merged = PlaylistsMerge.Merged(theirs, mine, Base());

        Assert.Equal("Бах II", Bach(merged).Name);
        Assert.Equal([new MergeConflict("playlists.name", "list_0001")], merged.Conflicts);
    }

    [Fact]
    public void Pieces_added_and_taken_off_on_either_copy_are_so_in_the_merge_the_other_copys_new_ones_at_the_end()
    {
        LibraryPlaylists mine = Base().Added("list_0001", ["p_m"]).Removed("list_0001", ["p_a"]);
        LibraryPlaylists theirs = Base().Added("list_0001", ["p_t1", "p_t2"]).Removed("list_0001", ["p_b"]);

        Assert.Equal(["p_m", "p_t1", "p_t2"], Bach(PlaylistsMerge.Merged(mine, theirs, Base())).Pieces);
        Assert.Equal(["p_t1", "p_t2", "p_m"], Bach(PlaylistsMerge.Merged(theirs, mine, Base())).Pieces);
    }

    [Fact]
    public void An_order_only_the_copy_that_lost_changed_is_kept_and_a_piece_the_other_added_follows_it()
    {
        LibraryPlaylists four = Base().Added("list_0001", ["p_c", "p_d"]);
        LibraryPlaylists reordered = four.Moved("list_0001", ["p_d"], "p_a");
        LibraryPlaylists added = four.Added("list_0001", ["p_e"]);

        MergedPlaylists merged = PlaylistsMerge.Merged(added, reordered, four);

        Assert.Equal(["p_d", "p_a", "p_b", "p_c", "p_e"], Bach(merged).Pieces);
        Assert.Empty(merged.Conflicts);
    }

    [Fact]
    public void Reordered_on_both_differently_the_copy_saved_last_keeps_its_order_with_a_conflict()
    {
        LibraryPlaylists four = Base().Added("list_0001", ["p_c", "p_d"]);
        LibraryPlaylists mine = four.Moved("list_0001", ["p_d"], "p_a");
        LibraryPlaylists theirs = four.Moved("list_0001", ["p_a"], null);

        MergedPlaylists merged = PlaylistsMerge.Merged(mine, theirs, four);

        Assert.Equal(["p_d", "p_a", "p_b", "p_c"], Bach(merged).Pieces);
        Assert.Equal([new MergeConflict("playlists.pieces", "list_0001")], merged.Conflicts);
    }

    [Fact]
    public void A_playlist_deleted_on_one_copy_and_unchanged_on_the_other_goes()
    {
        LibraryPlaylists deleted = Base().Deleted("list_0002");

        Assert.Null(PlaylistsMerge.Merged(Base(), deleted, Base()).Playlists.Find("list_0002"));
        Assert.Null(PlaylistsMerge.Merged(deleted, Base(), Base()).Playlists.Find("list_0002"));
    }

    [Fact]
    public void A_playlist_deleted_on_one_copy_and_changed_on_the_other_stays_with_the_change()
    {
        LibraryPlaylists deleted = Base().Deleted("list_0001");
        LibraryPlaylists changed = Base().Added("list_0001", ["p_new"]);

        Assert.Equal(["p_a", "p_b", "p_new"], Bach(PlaylistsMerge.Merged(deleted, changed, Base())).Pieces);
        Assert.Equal(["p_a", "p_b", "p_new"], Bach(PlaylistsMerge.Merged(changed, deleted, Base())).Pieces);
    }

    [Fact]
    public void Without_a_base_the_merge_is_a_union_with_no_deletions()
    {
        LibraryPlaylists mine = Base().Deleted("list_0002").Removed("list_0001", ["p_a"]);
        LibraryPlaylists theirs = Base().Added("list_0001", ["p_t"]);

        MergedPlaylists merged = PlaylistsMerge.Merged(mine, theirs, null);

        Assert.Equal(["Бах", "Шопен"], merged.Playlists.All.Select(static playlist => playlist.Name));
        Assert.Equal(["p_b", "p_a", "p_t"], Bach(merged).Pieces);
    }

    [Fact]
    public void Two_playlists_made_apart_under_one_name_are_one_after_the_merge_with_the_pieces_of_both()
    {
        LibraryPlaylists mine = Base().Created("Экзамен", ["p_a"], IdSource.Random).Playlists;
        LibraryPlaylists theirs = Base().Created("экзамен", ["p_b", "p_a"], IdSource.Random).Playlists;

        Playlist exam = Assert.Single(PlaylistsMerge.Merged(mine, theirs, Base()).Playlists.All, static playlist => LibraryPlaylists.SameName(playlist.Name, "Экзамен"));

        Assert.Equal(mine.Named("Экзамен"), exam with { Pieces = ["p_a"] });
        Assert.Equal(["p_a", "p_b"], exam.Pieces);
    }

    [Fact]
    public void A_field_this_build_does_not_know_merges_as_a_whole_value()
    {
        LibraryPlaylists theirs = Base() with { Unknown = [new UnknownField("future", "1")] };

        MergedPlaylists merged = PlaylistsMerge.Merged(Base(), theirs, Base());
        MergedPlaylists both = PlaylistsMerge.Merged(theirs with { Unknown = [new UnknownField("future", "2")] }, theirs, Base());

        Assert.Equal(theirs.Unknown, merged.Playlists.Unknown);
        Assert.Equal([new UnknownField("future", "2")], both.Playlists.Unknown);
        Assert.Equal([new MergeConflict("future", null)], both.Conflicts);
    }

    [Fact]
    public void The_copy_saved_last_wins_and_the_kept_one_goes_online_only_when_it_is_not_the_online_copy()
    {
        LibraryPlaylists mine = Base().Renamed("list_0001", "Бах I").Playlists;
        LibraryPlaylists theirs = Base().Renamed("list_0001", "Бах II").Playlists;

        PlaylistsResolution later = PlaylistsMerge.Resolve(mine, s_later, theirs, s_earlier, Base());
        PlaylistsResolution earlier = PlaylistsMerge.Resolve(mine, s_earlier, theirs, s_later, Base());
        PlaylistsResolution same = PlaylistsMerge.Resolve(Base(), s_later, theirs, s_earlier, Base());

        Assert.Equal(("Бах I", true), (later.Kept.Find("list_0001")!.Name, later.Upload));
        Assert.Equal(("Бах II", false), (earlier.Kept.Find("list_0001")!.Name, earlier.Upload));
        Assert.Equal((theirs, false, true), (same.Kept, same.Upload, same.Merged));
    }

    [Fact]
    public void A_later_builds_copy_is_never_merged_nor_sent_and_a_change_here_not_in_it_is_a_conflict()
    {
        LibraryPlaylists newer = Base() with { Newer = new NewerFile(2, 1) };
        LibraryPlaylists changed = Base().Deleted("list_0002");

        PlaylistsResolution resolved = PlaylistsMerge.Resolve(changed, s_later, newer, s_earlier, Base());
        PlaylistsResolution unchanged = PlaylistsMerge.Resolve(Base(), s_later, newer, s_earlier, Base());

        Assert.Equal((newer, false, false), (resolved.Kept, resolved.Upload, resolved.Merged));
        Assert.Equal([new MergeConflict("playlists", null)], resolved.Conflicts);
        Assert.Empty(unchanged.Conflicts);
    }
}