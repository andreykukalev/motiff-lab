using System.Text;

using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// The pieces layer (version 0.5): the pieces of a long score or recording kept as ordinary bookmarks in the reserved
/// category "pieces", so the project file does not change and older builds keep it as a category of the player's own.
/// </summary>
public class PieceLayerTests
{
    private const long Bar = 4 * CanonicalTime.TicksPerQuarter;

    private static readonly DateTimeOffset s_now = new(2026, 11, 12, 15, 0, 0, TimeSpan.Zero);

    private static readonly byte[] s_source = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "importer-grand-staff.musicxml"));

    private static readonly Tick s_end = new(60 * Bar);

    private static Tick At(int bar) => new((bar - 1) * Bar);

    private static Project Fresh() => Project.Create("p_concert", "concert.mid", s_source, s_now)
        .AddBookmark(new Bookmark("b_0001", "Hard bars", BookmarkCategory.PractiseId, TickSpan.FromBounds(At(3), At(5)), FromPhrase: false, s_now));

    private static Project Saved(params PieceStart[] pieces) => PieceLayer.Save(Fresh(), pieces, MeterMap.Default, s_end, Words.Russian, s_now);

    [Fact]
    public void Saving_writes_one_bookmark_a_piece_from_its_first_bar_to_the_next_in_a_category_named_in_the_players_language()
    {
        Project project = Saved(new PieceStart("Prelude", 1), new PieceStart(null, 21), new PieceStart(" Fugue ", 41));

        BookmarkCategory category = project.FindCategory(BookmarkCategory.PiecesId)!;
        Assert.Equal("Пьесы", category.Name);
        Assert.False(category.IsBuiltIn);
        Assert.Equal("Pieces", category.NameIn(Words.English));
        Assert.Equal("Hard bars", project.FindBookmark("b_0001")!.Title);

        IReadOnlyList<LayerPiece> layer = PieceLayer.Read(project, MeterMap.Default);
        Assert.Equal(["Prelude", "Пьеса 2", "Fugue"], layer.Select(piece => piece.Title));
        Assert.Equal([(1, 20), (21, 40), (41, 60)], layer.Select(piece => (piece.FirstBar, piece.LastBar)));
        Assert.Equal(TickSpan.FromBounds(Tick.Zero, At(21)), layer[0].Span);
        Assert.Equal(TickSpan.FromBounds(At(41), s_end), layer[2].Span);
        Assert.True(PieceLayer.Has(project));
        Assert.False(PieceLayer.Has(Fresh()));
    }

    [Fact]
    public void Saving_again_replaces_the_layer_keeping_its_ids_in_score_order_and_none_clears_it()
    {
        Project first = Saved(new PieceStart("A", 1), new PieceStart("B", 21), new PieceStart("C", 41));
        string[] ids = [.. PieceLayer.Read(first, MeterMap.Default).Select(piece => piece.BookmarkId)];

        Project second = PieceLayer.Save(first, [new PieceStart("A", 1), new PieceStart("B and C", 25)], MeterMap.Default, s_end, Words.English, s_now.AddHours(1));

        IReadOnlyList<LayerPiece> layer = PieceLayer.Read(second, MeterMap.Default);
        Assert.Equal(ids[..2], layer.Select(piece => piece.BookmarkId));
        Assert.Equal([1, 25], layer.Select(piece => piece.FirstBar));
        Assert.Equal(s_now, second.FindBookmark(ids[0])!.CreatedAt);
        Assert.Null(second.FindBookmark(ids[2]));
        Assert.Equal("Пьесы", second.FindCategory(BookmarkCategory.PiecesId)!.Name);

        Project cleared = PieceLayer.Save(second, [], MeterMap.Default, s_end, Words.English, s_now);
        Assert.Empty(PieceLayer.Read(cleared, MeterMap.Default));
        Assert.NotNull(cleared.FindBookmark("b_0001"));
    }

    [Fact]
    public void Pieces_out_of_order_or_outside_the_piece_are_refused()
    {
        Assert.Throws<ArgumentException>(() => Saved(new PieceStart("A", 21), new PieceStart("B", 21)));
        Assert.Throws<ArgumentException>(() => Saved(new PieceStart("A", 21), new PieceStart("B", 5)));
        Assert.Throws<ArgumentException>(() => Saved(new PieceStart("A", 0)));
        Assert.Throws<ArgumentException>(() => Saved(new PieceStart("A", 61)));
    }

    [Fact]
    public void Where_two_copies_merged_overlap_the_later_piece_cuts_the_earlier_short_and_an_empty_one_is_left_out()
    {
        Project project = Saved(new PieceStart("A", 1)).AddBookmark(new Bookmark("b_0009", "B", BookmarkCategory.PiecesId, TickSpan.FromBounds(At(30), s_end), FromPhrase: false, s_now))
            .AddBookmark(new Bookmark("b_0010", "C", BookmarkCategory.PiecesId, TickSpan.FromBounds(At(30), At(31)), FromPhrase: false, s_now));

        IReadOnlyList<LayerPiece> layer = PieceLayer.Read(project, MeterMap.Default);

        Assert.Equal(["A", "C"], layer.Select(piece => piece.Title));
        Assert.Equal((1, 29), (layer[0].FirstBar, layer[0].LastBar));
        Assert.Equal((30, 30), (layer[1].FirstBar, layer[1].LastBar));
    }

    [Fact]
    public void The_layer_goes_through_the_project_file_unchanged_as_bookmarks_of_a_named_category()
    {
        Project project = Saved(new PieceStart("Prelude", 1), new PieceStart("Fugue", 31));

        byte[] file = ProjectFile.Write(project);
        Project read = ProjectFile.Read(file);

        Assert.Contains("\"pieces\"", Encoding.UTF8.GetString(file), StringComparison.Ordinal);
        Assert.Equal(PieceLayer.Read(project, MeterMap.Default), PieceLayer.Read(read, MeterMap.Default));
        Assert.Equal("Пьесы", read.FindCategory(BookmarkCategory.PiecesId)!.Name);
    }
}