using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// How an app finds a collection's pieces among its library's rows, and a piece's collection, from the project files alone
/// (v0.4, «Пьесы в файле», "partOf" within version 15): each part keeps the collection's original under "src_book", named
/// by the collection's title, and its row says so.
/// </summary>
public class PiecePartsLinkTests
{
    [Fact]
    public void The_rows_of_a_scores_parts_name_its_original_by_the_collections_title_and_are_found_from_the_collection()
    {
        Project book = PiecePartsTests.Book();
        PieceSplit split = PieceParts.Split(book, [new("Für Elise", PartUnit.Bars, 0, 105), new("Ode to Joy", PartUnit.Bars, 106, 121)], PiecePartsTests.Options(device: HoldingDevice.Web));

        // Saved and read back as the library keeps them: each row says where it came from, with no place for a score cut by bars.
        Project[] saved = [.. split.Parts.Select(static part => ProjectFile.Read(ProjectFile.Write(part.Project)))];
        PartSummary[] rows = [.. saved.Select(static part => part.Summary.Part!)];
        Assert.All(rows, row => Assert.Equal(book.Details.Title.Value, row.Original.Shown));
        Assert.All(rows, static row => Assert.Null(row.Place));

        // The collection, read back from its own file, finds both; it is no part itself.
        Project collection = ProjectFile.Read(ProjectFile.Write(book));
        PieceOriginal original = PieceParts.CollectionOriginal(collection)!;
        Assert.All(rows, row => Assert.True(PieceParts.IsPartOf(row, original)));
        Assert.Null(collection.Summary.Part);

        // Another score is not their collection, and a part is not its own.
        Project other = Project.Create("p_other", "two-pieces.musicxml", [.. book.Source, .. " "u8.ToArray()], book.CreatedAt);
        Assert.False(PieceParts.IsPartOf(rows[0], PieceParts.CollectionOriginal(other)!));
        Assert.False(PieceParts.IsPartOf(rows[0], PieceParts.CollectionOriginal(saved[0])!));
    }

    [Fact]
    public void The_parts_of_a_recording_described_by_no_fingerprint_are_found_by_its_name_and_time_and_keep_their_stretch()
    {
        Project recording = PiecePartsTests.Recording(2);
        OpenedProject opened = OpenedProject.Open(recording);
        int last = opened.Piece.Meter.BarAt(new Tick(opened.Piece.End.Value - 1)).Number;
        PieceSplit split = PieceParts.Split(recording, [new("Start", PartUnit.Bars, opened.Piece.Meter.FirstBarNumber, 3), new("End", PartUnit.Bars, 4, last)], PiecePartsTests.Options());

        PartSummary row = ProjectFile.Read(ProjectFile.Write(split.Parts[1].Project)).Summary.Part!;
        Assert.Null(row.Original.Sha256);
        Assert.Equal(split.Parts[1].Milliseconds, row.Place!.Milliseconds);
        Assert.Equal(recording.Details.Title.Value, row.Original.Shown);
        Assert.True(PieceParts.IsPartOf(row, PieceParts.CollectionOriginal(ProjectFile.Read(ProjectFile.Write(recording)))!));
        Assert.False(PieceParts.IsPartOf(row, PieceParts.CollectionOriginal(recording with { CreatedAt = recording.CreatedAt.AddSeconds(1) })!));
    }
}