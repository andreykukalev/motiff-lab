using System.Text;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Import.Scan;

/// <summary>The "Check the scan" step for pieces read by sheet music recognition (decision record 0011).</summary>
public class ScanCheckTests
{
    private static ImportedPiece Fixture(string name) =>
        PieceImporter.Import(name, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Scan", "Fixtures", name))).Piece!;

    /// <summary>A one-part grand staff in 3/4; each string is the content of one measure.</summary>
    private static ImportedPiece Score(params string[] measures)
    {
        var xml = new StringBuilder("<score-partwise><part-list><score-part id=\"P1\"><part-name>Piano</part-name></score-part></part-list><part id=\"P1\">");
        for (int i = 0; i < measures.Length; i++)
        {
            string attributes = i == 0 ? "<attributes><divisions>1</divisions><staves>2</staves><time><beats>3</beats><beat-type>4</beat-type></time></attributes>" : "";
            xml.Append($"<measure number=\"{i + 1}\">{attributes}{measures[i]}</measure>");
        }

        return PieceImporter.Import("scan.musicxml", Encoding.UTF8.GetBytes(xml.Append("</part></score-partwise>").ToString())).Piece!;
    }

    private static string Note(string step, int octave, int duration, int staff = 1, string extra = "") =>
        $"<note>{extra}<pitch><step>{step}</step><octave>{octave}</octave></pitch><duration>{duration}</duration><staff>{staff}</staff></note>";

    private static string Rest(int duration, int staff) => $"<note><rest/><duration>{duration}</duration><staff>{staff}</staff></note>";

    private static string Backup(int duration) => $"<backup><duration>{duration}</duration></backup>";

    private static string Full(int staff = 1) => Note("C", 5, 3, staff);

    [Fact]
    public void Homr_reading_of_fur_elise_flags_the_bars_where_its_rhythm_goes_wrong()
    {
        ScanCheck check = ScanCheck.Of(Fixture("homr-fur-elise-page1.musicxml"));

        Assert.Equal(32, check.Bars.Count);
        Assert.Equal([8, 16, 21, 27, 29, 32, 36], check.Flagged.Select(bar => bar.Bar));
        Assert.Equal([ScanFlag.BarTooLong, ScanFlag.StavesDiffer], check.Find(16)!.Flags);
        Assert.Equal([ScanFlag.BarTooLong, ScanFlag.StavesDiffer, ScanFlag.GraceNotes], check.Find(27)!.Flags);
        Assert.Equal([ScanFlag.BarTooShort], check.Find(8)!.Flags);
        // Bars 1-7 of the opening came back exactly right.
        Assert.All(check.Bars.Where(bar => bar.Bar < 8), bar => Assert.False(bar.IsFlagged));
    }

    [Fact]
    public void A_harsh_photo_of_the_same_page_is_flagged_in_the_same_places()
    {
        ScanCheck check = ScanCheck.Of(Fixture("homr-fur-elise-page1-photo.musicxml"));

        Assert.Equal([8, 16, 21, 27, 29, 32, 35], check.Flagged.Select(bar => bar.Bar));
    }

    [Fact]
    public void Every_note_belongs_to_one_bar()
    {
        ImportedPiece piece = Fixture("homr-fur-elise-page1.musicxml");
        ScanCheck check = ScanCheck.Of(piece);

        Assert.Equal(piece.Notes.Select(note => note.Id).Order(), check.Bars.SelectMany(bar => bar.NoteIds).Order());
    }

    [Fact]
    public void A_short_first_bar_is_a_pickup_and_a_short_last_bar_completes_it()
    {
        ScanCheck check = ScanCheck.Of(Score(Note("E", 5, 1), Full(), Note("A", 4, 2)));

        Assert.Empty(check.Flagged);
    }

    [Fact]
    public void A_bar_that_runs_long_or_short_is_flagged()
    {
        ScanCheck check = ScanCheck.Of(Score(Full(), Note("C", 5, 4), Note("D", 5, 2), Full()));

        Assert.Equal([ScanFlag.BarTooLong], check.Bars[1].Flags);
        Assert.Equal([ScanFlag.BarTooShort], check.Bars[2].Flags);
        Assert.False(check.Bars[3].IsFlagged);
    }

    [Fact]
    public void A_hand_that_does_not_reach_the_end_of_the_bar_is_flagged()
    {
        ScanCheck check = ScanCheck.Of(Score(
            Full() + Backup(3) + Rest(3, 2),
            Full() + Backup(3) + Note("C", 3, 2, staff: 2),
            Full()));

        Assert.False(check.Bars[0].IsFlagged);
        Assert.Equal([ScanFlag.StavesDiffer], check.Bars[1].Flags);
    }

    [Fact]
    public void Notes_outside_the_piano_and_grace_notes_are_flagged()
    {
        ScanCheck check = ScanCheck.Of(Score(Full(), Note("C", 9, 3), "<note><grace/><pitch><step>D</step><octave>5</octave></pitch><staff>1</staff></note>" + Full(), Full()));

        Assert.Equal([ScanFlag.OutOfPianoRange], check.Bars[1].Flags);
        Assert.Equal([ScanFlag.GraceNotes], check.Bars[2].Flags);
        Assert.All(check.Flagged.SelectMany(bar => bar.Reasons), reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
    }

    [Fact]
    public void Scanned_notes_are_detected_less_sure_where_flagged_and_confirmed_where_the_player_said_so()
    {
        ImportedPiece piece = Fixture("homr-fur-elise-page1.musicxml");
        ScanCheck check = ScanCheck.Of(piece);

        ImportedPiece marked = check.Mark(piece, confirmedBars: [16, 3]);

        Note Of(int bar) => marked.Notes.First(note => note.Id == check.Find(bar)!.NoteIds[0]);
        Assert.Equal(Attribution.Detected(ScanCheck.CleanConfidence), Of(2).Attribution);
        Assert.Equal(Attribution.Detected(ScanCheck.FlaggedConfidence), Of(21).Attribution);
        Assert.Equal(Attribution.UserConfirmed, Of(16).Attribution);
        Assert.Equal(Attribution.UserConfirmed, Of(3).Attribution);
        // The hands come from the staves the engine read, so they are as sure as the notes.
        Assert.Equal(Of(21).Attribution, Of(21).Hand!.Attribution);
        Assert.DoesNotContain(marked.Notes, note => note.Attribution.Provenance == Provenance.Imported);
        Assert.Equal(piece.Notes.Select(note => (note.Id, note.Pitch, note.Span)), marked.Notes.Select(note => (note.Id, note.Pitch, note.Span)));
    }

    [Fact]
    public void Marking_is_the_same_whatever_the_notes_were_marked_before()
    {
        ImportedPiece piece = Fixture("homr-fur-elise-page1.musicxml");
        ScanCheck check = ScanCheck.Of(piece);

        IReadOnlyList<Note> once = check.Mark(piece.Notes, [16]);

        Assert.Equal(check.Mark(piece.Notes, []), check.Mark(once, []));
    }

    [Fact]
    public void A_scanned_project_reopens_with_its_notes_marked_and_its_checked_bars_kept()
    {
        byte[] source = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Scan", "Fixtures", "homr-fur-elise-page1.musicxml"));
        Project project = Project.Create("p_scan", "fur-elise.musicxml", source, new DateTimeOffset(2026, 10, 25, 20, 0, 0, TimeSpan.Zero)) with
        {
            Scan = ScanOrigin.Read("homr 0.7.0").Confirm([16, 8]),
        };

        Project reread = ProjectFile.Read(ProjectFile.Write(project));
        OpenedProject opened = OpenedProject.Open(reread);

        Assert.Equal(new ScanOrigin("homr 0.7.0", [8, 16]), reread.Scan);
        ScanCheck check = ScanCheck.Of(opened.Piece);
        Assert.Equal(Attribution.UserConfirmed, opened.Notes.First(note => note.Id == check.Find(16)!.NoteIds[0]).Attribution);
        Assert.Equal(Provenance.Detected, opened.Notes.First(note => note.Id == check.Find(21)!.NoteIds[0]).Attribution.Provenance);
        Assert.Equal(opened.Notes, opened.Piece.Notes);
    }

    [Fact]
    public void A_project_imported_from_a_file_has_no_scan()
    {
        byte[] source = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Scan", "Fixtures", "homr-fur-elise-page1.musicxml"));
        Project project = Project.Create("p_file", "fur-elise.musicxml", source, new DateTimeOffset(2026, 10, 25, 20, 0, 0, TimeSpan.Zero));

        Project reread = ProjectFile.Read(ProjectFile.Write(project));

        Assert.Null(reread.Scan);
        Assert.All(OpenedProject.Open(reread).Notes, note => Assert.Equal(Attribution.Imported, note.Attribution));
    }

    [Fact]
    public void Checked_bars_are_kept_in_order_once_and_can_be_checked_again()
    {
        ScanOrigin scan = ScanOrigin.Read("homr 0.7.0").Confirm([21, 8, 21]);

        Assert.Equal([8, 21], scan.ConfirmedBars);
        Assert.True(scan.IsConfirmed(21));
        Assert.Equal([8], scan.Unconfirm(21).ConfirmedBars);
    }
}