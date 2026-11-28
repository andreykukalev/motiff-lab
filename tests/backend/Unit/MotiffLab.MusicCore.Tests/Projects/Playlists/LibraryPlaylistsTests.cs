using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Playlists;

namespace MotiffLab.MusicCore.Tests.Projects.Playlists;

/// <summary>The library's playlists and their edits (version 1.7, the score-reading spec 6; docs/playlists-file.md, "The rules").</summary>
public class LibraryPlaylistsTests
{
    /// <summary>"Бах" with p_a and p_b, "Шопен" with p_c, as numbered ids give them.</summary>
    internal static LibraryPlaylists Two()
    {
        PlaylistChange bach = LibraryPlaylists.None.Created("Бах", ["p_a", "p_b"], IdSource.Numbered);
        return bach.Playlists.Created("Шопен", ["p_c"], IdSource.Numbered).Playlists;
    }

    private static IReadOnlyList<string> Pieces(LibraryPlaylists playlists, string name) => playlists.Named(name)!.Pieces;

    [Fact]
    public void A_new_playlist_is_empty_at_the_end_and_stays_while_it_has_no_piece()
    {
        PlaylistChange made = Two().Created("  Для  рояля ", [], IdSource.Numbered);

        Assert.Equal("list_0003", made.Id);
        Assert.Null(made.Taken);
        Assert.Equal(["Бах", "Шопен", "Для рояля"], made.Playlists.All.Select(static playlist => playlist.Name));
        Assert.Empty(made.Playlists.Find("list_0003")!.Pieces);
    }

    [Theory]
    [InlineData("бах")]
    [InlineData(" БАХ  ")]
    public void A_name_taken_whatever_its_letter_case_answers_the_playlist_that_has_it_and_changes_nothing(string name)
    {
        LibraryPlaylists two = Two();

        PlaylistChange made = two.Created(name, ["p_z"], IdSource.Numbered);

        Assert.Null(made.Id);
        Assert.Equal("list_0001", made.Taken);
        Assert.Equal(two, made.Playlists);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_playlist_needs_a_name(string name)
    {
        Assert.Throws<ArgumentException>(() => Two().Created(name, [], IdSource.Numbered));
        Assert.Throws<ArgumentException>(() => Two().Renamed("list_0001", name));
    }

    [Fact]
    public void A_rename_keeps_the_id_and_the_pieces_may_change_only_the_letter_case_and_never_takes_another_name()
    {
        LibraryPlaylists two = Two();

        PlaylistChange cased = two.Renamed("list_0001", "БАХ");
        PlaylistChange taken = two.Renamed("list_0001", "шопен");

        Assert.Equal(new Playlist("list_0001", "БАХ", ["p_a", "p_b"]), cased.Playlists.Find("list_0001"));
        Assert.Equal("list_0002", taken.Taken);
        Assert.Equal(two, taken.Playlists);
    }

    [Fact]
    public void A_deleted_playlist_goes_and_the_others_keep_their_pieces()
    {
        LibraryPlaylists deleted = Two().Added("list_0002", ["p_a"]).Deleted("list_0001");

        Assert.Equal(["Шопен"], deleted.All.Select(static playlist => playlist.Name));
        Assert.Equal(["p_c", "p_a"], Pieces(deleted, "Шопен"));
        Assert.Throws<KeyNotFoundException>(() => deleted.Deleted("list_0001"));
    }

    [Fact]
    public void Pieces_added_go_at_the_end_in_their_order_each_once_and_one_already_there_stays_in_its_place()
    {
        LibraryPlaylists added = Two().Added("list_0001", ["p_d", "p_a", "p_c", "p_d", ""]);

        Assert.Equal(["p_a", "p_b", "p_d", "p_c"], Pieces(added, "Бах"));
        Assert.Equal(["p_c"], Pieces(added, "Шопен"));
    }

    [Fact]
    public void A_piece_removed_leaves_only_that_playlist()
    {
        LibraryPlaylists removed = Two().Added("list_0002", ["p_a"]).Removed("list_0001", ["p_a", "p_x"]);

        Assert.Equal(["p_b"], Pieces(removed, "Бах"));
        Assert.Equal(["p_c", "p_a"], Pieces(removed, "Шопен"));
    }

    [Theory]
    [InlineData(new[] { "p_d" }, "p_a", new[] { "p_d", "p_a", "p_b", "p_c" })]
    [InlineData(new[] { "p_a" }, null, new[] { "p_b", "p_c", "p_d", "p_a" })]
    [InlineData(new[] { "p_d", "p_b" }, "p_a", new[] { "p_d", "p_b", "p_a", "p_c" })]
    [InlineData(new[] { "p_x", "p_c" }, "p_b", new[] { "p_a", "p_c", "p_b", "p_d" })]
    public void Pieces_moved_go_before_the_piece_named_or_to_the_end_in_the_order_given(string[] moved, string? before, string[] order)
    {
        LibraryPlaylists four = Two().Added("list_0001", ["p_c", "p_d"]);

        Assert.Equal(order, Pieces(four.Moved("list_0001", moved, before), "Бах"));
    }

    [Theory]
    [InlineData("p_c")]
    [InlineData("p_a")]
    public void A_move_before_a_piece_not_staying_on_the_playlist_is_refused(string before)
    {
        Assert.Throws<ArgumentException>(() => Two().Moved("list_0001", ["p_a"], before));
    }

    [Fact]
    public void A_piece_the_library_no_longer_has_goes_from_every_playlist()
    {
        LibraryPlaylists forgotten = Two().Added("list_0002", ["p_a"]).Forgotten(["p_a"]);

        Assert.Equal(["p_b"], Pieces(forgotten, "Бах"));
        Assert.Equal(["p_c"], Pieces(forgotten, "Шопен"));
    }

    [Fact]
    public void A_piece_re_keyed_stays_on_its_playlists_under_its_id_now_once()
    {
        LibraryPlaylists rekeyed = Two().Rekeyed(new Dictionary<string, string> { ["p_a"] = "p_new", ["p_b"] = "p_new" });

        Assert.Equal(["p_new"], Pieces(rekeyed, "Бах"));
        Assert.Equal(["p_c"], Pieces(rekeyed, "Шопен"));
    }

    [Fact]
    public void Two_playlists_of_one_name_are_joined_into_the_first()
    {
        var two = new LibraryPlaylists { All = [new Playlist("list_b", "Бах", ["p_a", "p_b"]), new Playlist("list_a", "бах", ["p_c", "p_a"])] };

        Assert.Equal([new Playlist("list_b", "Бах", ["p_a", "p_b", "p_c"])], two.Joined().All);
    }
}