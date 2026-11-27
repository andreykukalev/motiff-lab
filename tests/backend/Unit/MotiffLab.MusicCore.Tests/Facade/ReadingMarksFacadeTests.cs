using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;

using static MotiffLab.MusicCore.Tests.Facade.FacadeCalls;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>
/// The marks «что не так» through the facade («Как напечатано», step 3, c-14b1; within contract 16, project file 18):
/// check.mark and check.unmark on a scan's working notes, check's "marks", the file keeping them, and the next reading of
/// the PDF (sources.notes.recognise) putting them in, carrying them to the new Notes and flagging those it cannot take.
/// </summary>
public class ReadingMarksFacadeTests
{
    private static byte[] Scanned() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Scan", "Fixtures", "homr-fur-elise-page1.musicxml"));

    /// <summary>A PDF read by the scan server, open as piece 1 (as the drafts' tests open it).</summary>
    private static MusicCoreFacade Opened(string language = "en") => DraftsFacadeTests.Opened(language);

    private static string Recognise() => JsonSerializer.Serialize(new { original = "src_pdf", musicXml = Convert.ToBase64String(Scanned()), engine = "homr 0.8.0", pages = 1, now = "2026-11-27T10:00:00Z" });

    private static Project Saved(MusicCoreFacade facade) => ProjectFile.Read(Encoding.UTF8.GetBytes(facade.Call(1, "project.save")));

    [Fact]
    public void A_mark_is_kept_with_the_scans_notes_listed_by_check_in_words_and_taken_away()
    {
        MusicCoreFacade facade = Opened(language: "ru");

        Call(facade, "check.mark", """{"kind": "meter", "bar": 3, "value": "3/8"}""");
        JsonElement check = Call(facade, "check.mark", """{"kind": "clef", "bar": 2, "last": 4, "staff": 2, "value": "treble"}""");

        JsonElement[] marks = [.. check.GetProperty("marks").EnumerateArray()];
        Assert.Equal(["clef:2-4", "meter:3-3"], marks.Select(static mark => $"{mark.GetProperty("kind").GetString()}:{mark.GetProperty("bar").GetInt32()}-{mark.GetProperty("last").GetInt32()}"));
        Assert.Equal("Здесь скрипичный ключ, нижний стан, такты 2–4", marks[0].GetProperty("text").GetString());
        Assert.False(marks[1].TryGetProperty("staff", out _));
        Assert.Equal(2, Saved(facade).Scan!.Marks.Count);

        // The same kind at the same bar takes the earlier mark's place; check.unmark takes it away.
        Call(facade, "check.mark", """{"kind": "meter", "bar": 3, "value": "6/8"}""");
        Assert.Equal("6/8", Assert.Single(Saved(facade).Scan!.Marks, static mark => mark.Kind == ReadingMarkKind.Meter).Value);
        Assert.Single(Call(facade, "check.unmark", """{"kind": "meter", "bar": 3}""").GetProperty("marks").EnumerateArray());
        Assert.Equal("badRequest", Error(facade, "check.unmark", """{"kind": "meter", "bar": 3}"""));
    }

    [Fact]
    public void A_mark_outside_the_piece_or_saying_what_its_kind_cannot_is_refused_and_other_notes_take_none()
    {
        MusicCoreFacade facade = Opened();

        Assert.Equal("badRequest", Error(facade, "check.mark", """{"kind": "hands", "bar": 999}"""));
        Assert.Equal("badRequest", Error(facade, "check.mark", """{"kind": "octave", "bar": 2, "value": "8va"}"""));
        Assert.Equal("badRequest", Error(facade, "check.mark", """{"kind": "meter", "bar": 2, "last": 3, "value": "3/8"}"""));
        Assert.Equal("badRequest", Error(facade, "check.mark", """{"kind": "tempo", "bar": 2}"""));
        Assert.Empty(Call(facade, "check").GetProperty("marks").EnumerateArray());

        var imported = new MusicCoreFacade();
        Json(imported.Open("fur-elise.musicxml", Fixture("fur-elise-original.musicxml"), """{"language": "en"}"""));
        Assert.Equal("unavailable", Error(imported, "check.mark", """{"kind": "hands", "bar": 2}"""));
    }

    [Fact]
    public void The_file_keeps_the_marks_a_later_builds_too_and_a_file_without_them_reads_as_before()
    {
        MusicCoreFacade facade = Opened();
        Assert.False(Json(facade.Call(1, "project.save")).GetProperty("notes")[0].GetProperty("scan").TryGetProperty("marks", out _));
        Call(facade, "check.mark", """{"kind": "octave", "bar": 2, "last": 3, "staff": 1, "value": "8vb"}""");

        byte[] file = ProjectFile.Write(Saved(facade));
        Project again = ProjectFile.Read(file);

        Assert.Equal(new ReadingMark(ReadingMarkKind.Octave, 2, 3, 1, "8vb"), Assert.Single(again.Scan!.Marks));
        Assert.Equal(file, ProjectFile.Write(again));
        string later = Encoding.UTF8.GetString(file).Replace("\"octave\"", "\"fingering\"", StringComparison.Ordinal);
        Project kept = ProjectFile.Read(Encoding.UTF8.GetBytes(later));
        Assert.Empty(kept.Scan!.Marks);
        Assert.Single(kept.Scan.UnknownMarks);
        Assert.Equal(Json(later).GetProperty("notes")[0].GetProperty("scan").GetProperty("marks").GetRawText(),
            Json(Encoding.UTF8.GetString(ProjectFile.Write(kept))).GetProperty("notes")[0].GetProperty("scan").GetProperty("marks").GetRawText());
        string noName = Encoding.UTF8.GetString(file).Replace("\"octave\"", "\"8va mark\"", StringComparison.Ordinal);
        Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(noName)));
        string damaged = Encoding.UTF8.GetString(file).Replace("\"8vb\"", "\"15ma\"", StringComparison.Ordinal);
        Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(damaged)));
    }

    [Fact]
    public void The_next_reading_takes_the_marks_carries_them_and_flags_what_it_cannot_take()
    {
        MusicCoreFacade facade = Opened();
        Call(facade, "check.mark", """{"kind": "hands", "bar": 2}""");
        Call(facade, "check.mark", """{"kind": "clef", "bar": 3, "staff": 3, "value": "bass"}""");
        Call(facade, "check.mark", """{"kind": "badlyRead", "bar": 4}""");

        JsonElement marked = Call(facade, "sources.notes.recognise", Recognise()).GetProperty("marked");

        Assert.Equal((1, 1), (marked.GetProperty("used").GetInt32(), marked.GetProperty("unfit").GetInt32()));
        Assert.Equal([3], marked.GetProperty("bars").EnumerateArray().Select(static bar => bar.GetInt32()));
        Assert.Equal("1 mark used · not used, in Check: 1 (bar 3)", marked.GetProperty("text").GetString());
        Call(facade, "sources.makeWorking", """{"id": "n_2"}""");
        Project working = Saved(facade);
        Assert.Equal([ReadingMarkKind.Hands, ReadingMarkKind.Clef, ReadingMarkKind.BadlyRead], working.Scan!.Marks.Select(static mark => mark.Kind));
        Assert.Contains(working.Check!, static flag => flag.Bar == 3 && flag.Reason == CheckReason.MarkUnfit && !flag.Resolved);
        Assert.Equal("Отметка не учтена", CheckFlag.NameOf(CheckReason.MarkUnfit, Words.Russian));

        ImportedPiece before = PieceImporter.Import("a.musicxml", ScanRepair.Repair(Scanned())).Piece!;
        ImportedPiece after = PieceImporter.Import("a.musicxml", working.Source).Piece!;
        Assert.Equal(Hands(before, 2).Select(Swapped), Hands(after, 2));
        Assert.Equal(Hands(before, 5), Hands(after, 5));
    }

    [Fact]
    public void A_mark_whose_bars_music_starts_no_bar_in_the_new_reading_is_flagged_where_its_music_lies()
    {
        MusicCoreFacade facade = Opened();
        ImportedPiece before = PieceImporter.Import("a.musicxml", ScanRepair.Repair(Scanned())).Piece!;
        int bar = before.Meter.ToPosition(before.MusicXml!.Measures[2].Span.Onset).Bar;
        Call(facade, "check.mark", $$"""{"kind": "hands", "bar": {{bar}}}""");
        // The marked bar's music starts a sixteenth late in the new reading (8 divisions a quarter): it lies off the bar's start.
        string xml = Encoding.UTF8.GetString(Scanned());
        int second = xml.IndexOf("<measure number=\"3\">", StringComparison.Ordinal) + "<measure number=\"3\">".Length;
        byte[] late = Encoding.UTF8.GetBytes(xml.Insert(second, "<forward><duration>2</duration></forward>"));
        ImportedPiece after = PieceImporter.Import("a.musicxml", ScanRepair.Repair(late)).Piece!;
        int expected = after.Meter.BarAt(new ReadingMatch(before, after).Place(before.Meter.GetBar(bar).Span.Onset, end: false).At).Number;
        string request = JsonSerializer.Serialize(new { original = "src_pdf", musicXml = Convert.ToBase64String(late), engine = "homr 0.8.0", pages = 1, now = "2026-11-27T10:00:00Z" });

        JsonElement marked = Call(facade, "sources.notes.recognise", request).GetProperty("marked");

        Assert.Equal((0, 1), (marked.GetProperty("used").GetInt32(), marked.GetProperty("unfit").GetInt32()));
        Assert.Equal([expected], marked.GetProperty("bars").EnumerateArray().Select(static each => each.GetInt32()));
        Call(facade, "sources.makeWorking", """{"id": "n_2"}""");
        Project working = Saved(facade);
        Assert.Equal(expected, Assert.Single(working.Scan!.Marks).Bar);
        Assert.Contains(working.Check!, flag => flag.Bar == expected && flag.Reason == CheckReason.MarkUnfit);
    }

    [Fact]
    public void Notes_read_again_without_marks_answer_as_before()
    {
        MusicCoreFacade facade = Opened();

        Assert.False(Call(facade, "sources.notes.recognise", Recognise()).TryGetProperty("marked", out _));
    }

    private static string[] Hands(ImportedPiece piece, int bar) =>
        [.. piece.Notes.Where(note => piece.Meter.BarAt(note.Span.Onset).Number == bar)
            .OrderBy(static note => note.Span.Onset).ThenBy(static note => note.Pitch.MidiNumber).Select(static note => $"{note.Pitch.MidiNumber}:{note.Hand?.Value}")];

    private static string Swapped(string note) => note.EndsWith(":Left", StringComparison.Ordinal) ? note[..^4] + "Right" : note[..^5] + "Left";
}