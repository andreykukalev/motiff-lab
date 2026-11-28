using MotiffLab.MusicCore.Projects.Playlists;

namespace MotiffLab.MusicCore.Tests.Projects.Playlists;

/// <summary>
/// The playlists moved from the pieces' "metadata"."playlists" into the playlists file (version 1.7, the score-reading
/// spec 6.1: «существующие плейлисты из свойств пьес переносятся в новые записи при первом запуске, ничего не теряется»).
/// </summary>
public class PlaylistsMigrationTests
{
    /// <summary>The library as it lists the pieces, from A by title, each with the playlists its properties name.</summary>
    private static readonly (string, IReadOnlyList<string>)[] s_library =
    [
        ("p_air", ["Бах", "Концерт"]),
        ("p_ballade", ["Концерт"]),
        ("p_invention", ["бах ", "  "]),
        ("p_nocturne", []),
        ("p_prelude", ["БАХ", "Экзамен"]),
    ];

    [Fact]
    public void Every_piece_stays_on_every_playlist_it_named_in_the_order_the_library_lists_them()
    {
        LibraryPlaylists moved = LibraryPlaylists.Migrated(s_library);

        Assert.Equal(["Бах", "Концерт", "Экзамен"], moved.All.Select(static playlist => playlist.Name));
        Assert.Equal(["p_air", "p_invention", "p_prelude"], moved.Named("Бах")!.Pieces);
        Assert.Equal(["p_air", "p_ballade"], moved.Named("Концерт")!.Pieces);
        Assert.Equal(["p_prelude"], moved.Named("Экзамен")!.Pieces);
    }

    [Fact]
    public void A_playlists_id_is_of_its_name_so_two_devices_moving_the_same_pieces_make_the_same_playlists()
    {
        LibraryPlaylists here = LibraryPlaylists.Migrated(s_library);
        LibraryPlaylists there = LibraryPlaylists.Migrated(s_library.Reverse());

        Assert.Equal(here.Named("Бах")!.Id, there.Named("бах")!.Id);
        Assert.Equal(here.All.Select(static playlist => playlist.Id).Order(), there.All.Select(static playlist => playlist.Id).Order());
        Assert.All(here.All, static playlist => Assert.Matches("^list_[0-9a-f]{32}$", playlist.Id));
        Assert.Single(PlaylistsMerge.Merged(here, there, null).Playlists.All, static playlist => playlist.Name == "Бах");
    }

    [Fact]
    public void A_library_whose_pieces_name_no_playlist_has_none()
    {
        Assert.Empty(LibraryPlaylists.Migrated([("p_nocturne", [])]).All);
    }
}