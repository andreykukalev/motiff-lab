using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Analysis.Phrasing;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Import.Scan.ScanRepairTests;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// A page no engine read, read again by itself and put into the same Notes (v1.3 i-1b, Andrei 2026-11-24 «Вставить в те же
/// ноты»): its bars at its place, the bars after it numbered on, the player's corrections still on the same music. Pages of
/// two bars each, the right hand's pitches telling the pages apart, a whole C3 a bar in the left hand.
/// </summary>
public class PageInsertTests
{
    private const string Now = "2026-11-24T18:00:00Z";

    private static string Bar(string step, int octave, int quarters = 4) =>
        string.Concat(Enumerable.Repeat(Note(step, octave, 2, 1), quarters)) + Backup(2 * quarters) + Note("C", 3, 2 * quarters, 2);

    private static string Page(string step, string beats = "4") => Encoding.UTF8.GetString(Score(2, beats, Bar(step, 5, int.Parse(beats, System.Globalization.CultureInfo.InvariantCulture)), Bar(step, 4, int.Parse(beats, System.Globalization.CultureInfo.InvariantCulture))));

    /// <summary>The project file of a PDF piece of <paramref name="pdfPages"/> pages whose notes were read from <paramref name="pages"/>.</summary>
    private static string Read(int pdfPages, params string[] pages) => Read(pdfPages, null, pages);

    /// <summary>As <see cref="Read(int, string[])"/>, with each page's report kept by the scan when <paramref name="reports"/> are given.</summary>
    private static string Read(int pdfPages, object[]? reports, params string[] pages)
    {
        var facade = new MusicCoreFacade();
        using JsonDocument joined = JsonDocument.Parse(facade.Call(0, "scan.join", JsonSerializer.Serialize(new { pages })));
        string score = joined.RootElement.GetProperty("score").GetString()!;
        Assert.DoesNotContain("\"error\"", facade.Open("Photos.pdf", "%PDF-1.4\n%%EOF\n"u8.ToArray(), $$"""{"id": "p_scan", "photos": {{pdfPages}}, "now": "{{Now}}"}"""), StringComparison.Ordinal);
        string request = reports is null
            ? JsonSerializer.Serialize(new { musicXml = Convert.ToBase64String(Encoding.UTF8.GetBytes(score)), engine = "homr", pages = pdfPages, now = Now })
            : JsonSerializer.Serialize(new { musicXml = Convert.ToBase64String(Encoding.UTF8.GetBytes(score)), engine = "homr", pages = pdfPages, now = Now, reports });
        using JsonDocument read = JsonDocument.Parse(facade.Call(1, "document.read", request));
        return read.RootElement.GetProperty("file").GetString()!;
    }

    private static JsonDocument Insert(string file, int page, object reading, int pdfPages = 3, object[]? reports = null) =>
        JsonDocument.Parse(new MusicCoreFacade().Call(0, "scan.insert", reports is null
            ? JsonSerializer.Serialize(new { file, page, reading, pages = pdfPages, language = "en" })
            : JsonSerializer.Serialize(new { file, page, reading, pages = pdfPages, language = "en", reports })));

    private static Dictionary<string, object> Report(double sharpness) =>
        new() { ["page"] = 1, ["sharpness"] = sharpness, ["contrast"] = 110, ["barLines"] = 4, ["measures"] = 2 };

    /// <summary>The file with the corrections of <paramref name="changes"/>, each a note id, its bar and how many semitones it goes up.</summary>
    private static string Corrected(string file, params (string Id, int Bar, int Pitch)[] changes)
    {
        var facade = new MusicCoreFacade();
        Assert.DoesNotContain("\"error\"", facade.Open("scan.motiff.json", Encoding.UTF8.GetBytes(file), null), StringComparison.Ordinal);
        foreach ((string id, int bar, int pitch) in changes)
        {
            Assert.DoesNotContain("\"error\"", facade.Call(1, "corrections.change", JsonSerializer.Serialize(new { bar, id, pitch })), StringComparison.Ordinal);
        }

        return facade.Call(1, "project.save", "{}");
    }

    /// <summary>The right hand's pitches bar by bar, as the piece of the file plays them with its corrections.</summary>
    private static int[][] RightHand(string file)
    {
        Project project = ProjectFile.Read(Encoding.UTF8.GetBytes(file));
        ImportedPiece piece = OpenedProject.Open(project).Piece;
        IReadOnlyList<Note> notes = OpenedProject.Open(project).Notes;
        return [.. piece.MusicXml!.Measures.Select(measure => notes.Where(note => note.Hand?.Value == Hand.Right && measure.Span.Contains(note.Span.Onset)).OrderBy(note => note.Span.Onset).Select(note => note.Pitch.MidiNumber).ToArray())];
    }

    private static int[] Four(int pitch) => [pitch, pitch, pitch, pitch];

    /// <summary>The file of two pages read, bar 2's C4 tied into page 2's first bar: one note across the place of a page put in after page 1.</summary>
    private static string TiedAcross()
    {
        string tied = Encoding.UTF8.GetString(Score(2, "4", Bar("C", 5), string.Concat(Enumerable.Repeat(Note("C", 4, 2, 1), 3))
            + Note("C", 4, 2, 1).Replace("</note>", "<tie type=\"start\"/></note>", StringComparison.Ordinal) + Backup(8) + Note("C", 3, 8, 2)));
        string next = Encoding.UTF8.GetString(Score(2, "4", Note("C", 4, 2, 1).Replace("</note>", "<tie type=\"stop\"/></note>", StringComparison.Ordinal)
            + string.Concat(Enumerable.Repeat(Note("E", 4, 2, 1), 3)) + Backup(8) + Note("C", 3, 8, 2), Bar("E", 4)));
        return Read(3, tied, next);
    }

    /// <summary>
    /// Version 18 (decision 3 of 1.6, deferred from v1.5 i-7): which pages of the PDF no engine read is kept with the notes,
    /// not only in one iPad's recognition queue; check answers them, and a page put in with "pdfPage" is read from then on.
    /// </summary>
    [Fact]
    public void The_pages_no_engine_read_are_kept_with_the_notes_until_they_are_put_in()
    {
        var facade = new MusicCoreFacade();
        using JsonDocument joined = JsonDocument.Parse(facade.Call(0, "scan.join", JsonSerializer.Serialize(new { pages = new[] { Page("C"), Page("E") } })));
        string score = joined.RootElement.GetProperty("score").GetString()!;
        Assert.DoesNotContain("\"error\"", facade.Open("Photos.pdf", "%PDF-1.4\n%%EOF\n"u8.ToArray(), $$"""{"id": "p_scan", "photos": 3, "now": "{{Now}}"}"""), StringComparison.Ordinal);
        string request = JsonSerializer.Serialize(new { musicXml = Convert.ToBase64String(Encoding.UTF8.GetBytes(score)), engine = "homr", pages = 3, now = Now, unread = new[] { 2 }, read = new[] { 3, 1 } });
        using JsonDocument read = JsonDocument.Parse(facade.Call(1, "document.read", request));
        string file = read.RootElement.GetProperty("file").GetString()!;

        ScanOrigin scan = ProjectFile.Read(Encoding.UTF8.GetBytes(file)).Scan!;
        Assert.Equal([2], scan.Unread);
        Assert.Equal([1, 3], scan.ReadPages);
        var again = new MusicCoreFacade();
        Assert.DoesNotContain("\"error\"", again.Open("scan.motiff.json", Encoding.UTF8.GetBytes(file)), StringComparison.Ordinal);
        using JsonDocument check = JsonDocument.Parse(again.Call(1, "check"));
        Assert.Equal([2], check.RootElement.GetProperty("unreadPages").EnumerateArray().Select(static page => page.GetInt32()));
        Assert.Equal([1, 3], check.RootElement.GetProperty("readPages").EnumerateArray().Select(static page => page.GetInt32()));

        string put = JsonSerializer.Serialize(new { file, page = 2, reading = Page("G"), pages = 3, language = "en", pdfPage = 2 });
        using JsonDocument inserted = JsonDocument.Parse(new MusicCoreFacade().Call(0, "scan.insert", put));
        ScanOrigin after = ProjectFile.Read(Encoding.UTF8.GetBytes(inserted.RootElement.GetProperty("file").GetString()!)).Scan!;
        Assert.Empty(after.Unread);
        Assert.Empty(after.ReadPages);
        Assert.DoesNotContain("\"unread\"", inserted.RootElement.GetProperty("file").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_both_read_and_unread_or_below_one_is_refused()
    {
        ScanOrigin scan = ScanOrigin.Read("homr");

        Assert.Throws<ArgumentException>(() => scan.WithUnread([2], [2]));
        Assert.Throws<ArgumentException>(() => scan.WithUnread([0], [1]));
        Assert.Equal([3], scan.WithUnread([2, 3], [1]).WithPageRead(2).Unread);
        Assert.Equal([1, 2], scan.WithUnread([2, 3], [1]).WithPageRead(2).ReadPages);
        Assert.Same(scan, scan.WithPageRead(4));
    }

    [Theory]
    [InlineData(1, new[] { 79, 67, 72, 60, 76, 64 })]
    [InlineData(2, new[] { 72, 60, 79, 67, 76, 64 })]
    [InlineData(3, new[] { 72, 60, 76, 64, 79, 67 })]
    public void The_page_goes_in_at_its_place_and_the_bars_after_it_are_numbered_on(int page, int[] firsts)
    {
        string file = Read(3, Page("C"), Page("E"));

        using JsonDocument inserted = Insert(file, page, Page("G"));

        JsonElement answer = inserted.RootElement;
        Assert.False(answer.TryGetProperty("error", out _), answer.ToString());
        Assert.Equal((2 * page - 1, 2), (answer.GetProperty("firstBar").GetInt32(), answer.GetProperty("bars").GetInt32()));
        string with = answer.GetProperty("file").GetString()!;
        Assert.Equal(firsts, RightHand(with).Select(bar => bar[0]));
        Project project = ProjectFile.Read(Encoding.UTF8.GetBytes(with));
        Assert.Equal(3, ScanJoin.Split(project.Source).Count);
        Assert.Equal(PieceOriginal.Fingerprint(project.Source), answer.GetProperty("fingerprint").GetString());
        Assert.Equal(PieceOriginal.Fingerprint(ProjectFile.Read(Encoding.UTF8.GetBytes(file)).Source), answer.GetProperty("was").GetString());
        // The three pages are each where the PDF has them now: page 2 starts at bar 3.
        Assert.Equal(new Tick(2 * 4 * 480), project.DocumentPages!.StartOf(2));
        Assert.Equal(page, answer.GetProperty("pages")[0].GetProperty("page").GetInt32());
        Assert.Equal(2, answer.GetProperty("pages")[0].GetProperty("bars").GetInt32());
    }

    [Fact]
    public void A_correction_before_the_page_stays_and_one_after_it_moves_on_with_its_note()
    {
        // n_0002 is bar 1's first C5, after its C3; n_0012 is bar 3's first E5 (each bar: the left hand's whole note and four quarters).
        string file = Corrected(Read(3, Page("C"), Page("E")), ("n_0002", 1, 1), ("n_0012", 3, 2));
        Assert.Equal([73, 72, 72, 72], RightHand(file)[0]);
        Assert.Equal([78, 76, 76, 76], RightHand(file)[2]);

        using JsonDocument inserted = Insert(file, 2, Page("G"));

        string with = inserted.RootElement.GetProperty("file").GetString()!;
        int[][] bars = RightHand(with);
        Assert.Equal([73, 72, 72, 72], bars[0]);
        Assert.Equal(Four(79), bars[2]);
        Assert.Equal([78, 76, 76, 76], bars[4]);
        Project project = ProjectFile.Read(Encoding.UTF8.GetBytes(with));
        // The page's ten notes come before bar 3's: its E5 is n_0022 now. The strict open lays every correction on its note.
        Assert.Equal(["n_0002", "n_0022"], project.Edits.Edits.Select(edit => edit.NoteId));
        Assert.NotNull(OpenedProject.Open(project));
    }

    [Fact]
    public void The_page_s_own_meter_holds_on_it_and_the_pages_after_keep_theirs()
    {
        string file = Read(3, Page("C"), Page("E"));

        using JsonDocument inserted = Insert(file, 2, Page("G", "3"));

        Project project = ProjectFile.Read(Encoding.UTF8.GetBytes(inserted.RootElement.GetProperty("file").GetString()!));
        ImportedPiece piece = OpenedProject.Open(project).Piece;
        Assert.Equal([1920L, 1920, 1440, 1440, 1920, 1920], piece.MusicXml!.Measures.Select(measure => measure.Span.Duration));
        Assert.Equal(Four(76), RightHand(inserted.RootElement.GetProperty("file").GetString()!)[4]);
    }

    [Fact]
    public void A_page_read_by_several_engines_is_merged_as_scan_join_merges_one_and_answers_its_merge()
    {
        string file = Read(3, Page("C"), Page("E"));
        object[] readings = [new { engine = "homr", musicxml = Page("G") }, new { engine = "copisteria", musicxml = Page("G") }, new { engine = "audiveris", musicxml = Page("A") }];

        using JsonDocument inserted = Insert(file, 2, readings);

        JsonElement merge = inserted.RootElement.GetProperty("pages")[0].GetProperty("merge");
        Assert.Equal(["homr", "copisteria", "audiveris"], merge.GetProperty("engines").EnumerateArray().Select(engine => engine.GetString()));
        Assert.Equal([3, 4], merge.GetProperty("bars").EnumerateArray().Select(bar => bar.GetProperty("bar").GetInt32()));
        Assert.Equal(Four(79), RightHand(inserted.RootElement.GetProperty("file").GetString()!)[2]);
    }

    [Fact]
    public void The_same_page_put_into_the_same_notes_gives_the_same_file()
    {
        string file = Corrected(Read(3, Page("C"), Page("E")), ("n_0012", 3, 2));

        using JsonDocument first = Insert(file, 2, Page("G")), second = Insert(file, 2, Page("G"));

        Assert.Equal(first.RootElement.GetProperty("file").GetString(), second.RootElement.GetProperty("file").GetString());
    }

    [Fact]
    public void A_correction_that_cannot_stay_on_the_same_music_refuses_the_page_and_nothing_is_dropped()
    {
        // Bar 2's C4 tied into page 2's first bar: one note across the page's place, which the page put in cuts.
        string read = TiedAcross();
        Note across = OpenedProject.Open(ProjectFile.Read(Encoding.UTF8.GetBytes(read))).Notes.Single(note => note.Hand?.Value == Hand.Right && note.Span.Duration == 960);
        string file = Corrected(read, (across.Id, 2, 1));

        using JsonDocument refused = Insert(file, 2, Page("G"));

        Assert.Equal("unavailable", refused.RootElement.GetProperty("error").GetString());
        Assert.Contains("sounds across", refused.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
        // Without the correction the same page goes in: the tie now ends where the page put in starts.
        using JsonDocument inserted = Insert(read, 2, Page("G"));
        Assert.False(inserted.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public void Checked_bars_flags_and_marks_move_on_with_the_bars_after_the_page()
    {
        Project read = ProjectFile.Read(Encoding.UTF8.GetBytes(Read(3, Page("C"), Page("E"))));
        Project confirmed = read with
        {
            Scan = read.Scan!.Confirm([1, 3]).WithMark(new ReadingMark(ReadingMarkKind.Hands, 3, 4, null, null)).WithMark(new ReadingMark(ReadingMarkKind.Octave, 1, 3, 1, "8va")),
            Check = [new CheckFlag(1, CheckSource.Scan, CheckReason.BarTooLong, true), new CheckFlag(4, CheckSource.Scan, CheckReason.BarTooLong, false)],
        };
        PageInserted inserted = ScanPageInsert.Into(confirmed, 2, Encoding.UTF8.GetBytes(Page("G")), null, 3);
        Assert.Equal([1, 5], inserted.Project!.Scan!.ConfirmedBars);
        Assert.Equal([(1, 5), (5, 6)], inserted.Project.Scan.Marks.Select(static mark => (mark.Bar, mark.Last)));
        Assert.Equal([(1, true), (6, false)], inserted.Project.Check!.Select(flag => (flag.Bar, flag.Resolved)));
    }

    /// <summary>
    /// The file with work on page 2's bars (bars 3 and 4 before the page goes in): the passage selected with a variation tried
    /// and kept as an idea (the motif and its variation saved), a margin note, a highlight of its first E5, a finger on it and a
    /// stroke in bar 3; with <paramref name="first"/> 2, the passage starts in page 1's last bar, across the page's place.
    /// </summary>
    private static string Worked(string file, int first = 3)
    {
        var facade = new MusicCoreFacade();
        Assert.DoesNotContain("\"error\"", facade.Open("scan.motiff.json", Encoding.UTF8.GetBytes(file), null), StringComparison.Ordinal);
        foreach ((string call, string request) in (ReadOnlySpan<(string, string)>)[
            ("select", $$$"""{"bars": {"first": {{{first}}}, "last": 4}}"""),
            ("try", """{"chip": "v1.halfTime"}"""),
            ("idea.keep", $$"""{"name": "Half time", "now": "{{Now}}"}"""),
            ("note.add", $$"""{"bars": {"first": 3, "last": 3}, "text": "Тема", "now": "{{Now}}"}"""),
            ("highlight.add", """{"bars": {"first": 3, "last": 3}, "notes": ["n_0012"]}"""),
            ("fingering.set", """{"noteId": "n_0012", "finger": 3}"""),
            ("ink.add", """{"strokes": [{"id": "s1", "tool": "pen", "colour": "carmine", "size": 2, "bar": 3840, "u": 0.1, "v": 0.2, "points": [0, 0, 0.5], "pressure": 0.5}]}"""),
        ])
        {
            Assert.DoesNotContain("\"error\"", facade.Call(1, call, request), StringComparison.Ordinal);
        }

        return facade.Call(1, "project.save", "{}");
    }

    [Fact]
    public void Phrases_ideas_motifs_and_marks_after_the_page_move_on_by_it_and_stay_on_the_same_notes()
    {
        // The lead, 2026-11-27 (v1.5 r-2b): they move with an inserted page like bookmarks and resume points, by one rule.
        string file = Worked(Read(3, Page("C"), Page("E")));
        Project worked = ProjectFile.Read(Encoding.UTF8.GetBytes(file)) with { Phrases = PhraseEdits.None.Adding([new Tick(1920), new Tick(5760)]) };
        Assert.True(worked.CanInsertPage);
        Assert.Single(worked.Motifs);
        Assert.NotEmpty(worked.Variations);
        Assert.Single(worked.Observations);

        Project inserted = ScanPageInsert.Into(worked, 2, Encoding.UTF8.GetBytes(Page("G")), null, 3).Project!;

        // Page 2 is bars 3 and 4 now, 3840 ticks: what was in bar 3 is in bar 5, its first E5 n_0022 (the page's ten notes before it).
        Assert.Equal([new Tick(1920), new Tick(5760 + 3840)], inserted.Phrases.Added);
        SavedMotif motif = Assert.Single(inserted.Motifs);
        Assert.Equal((7680L, 3840L), (motif.Span.Onset.Value, motif.Span.Duration));
        Assert.Contains("n_0022", motif.NoteIds);
        Observation idea = Assert.Single(inserted.Observations);
        Assert.Equal(7680, idea.Phrase.Span.Onset.Value);
        Assert.Equal(motif.NoteIds, idea.Variation!.NoteIds);
        Assert.Equal(7680, Assert.Single(inserted.Marks.Notes).Span.Onset.Value);
        Highlight highlight = Assert.Single(inserted.Marks.Highlights);
        Assert.Equal(7680, highlight.Span.Onset.Value);
        Assert.Equal(["n_0022"], highlight.NoteIds);
        Assert.Equal("n_0022", Assert.Single(inserted.Marks.Fingering).NoteId);
        Assert.Equal(new Tick(7680), Assert.Single(inserted.Marks.Ink).Bar);
        // The strict open makes the motif, its variation and the idea again on the same music: E5 E5 E5 E5 then E4.
        OpenedProject opened = OpenedProject.Open(inserted);
        Note e5 = opened.Notes.Single(note => note.Id == "n_0022");
        Assert.Equal((76, 7680L), (e5.Pitch.MidiNumber, e5.Span.Onset.Value));
        Assert.Empty(opened.Hidden);
        Assert.NotNull(Assert.Single(opened.Observations).Chain);
        Assert.Equal(worked.Variations.Count, ProjectFile.Read(ProjectFile.Write(inserted)).Variations.Count);
    }

    [Fact]
    public void A_motif_across_the_page_s_place_refuses_the_page_and_nothing_moves()
    {
        string file = Worked(Read(3, Page("C"), Page("E")), first: 2);

        using JsonDocument refused = Insert(file, 2, Page("G"));

        Assert.Equal("unavailable", refused.RootElement.GetProperty("error").GetString());
        Assert.Contains("motif, an idea's variation or a mark on a note", refused.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(PageRefusal.Notes, ScanPageInsert.Into(ProjectFile.Read(Encoding.UTF8.GetBytes(file)), 2, Encoding.UTF8.GetBytes(Page("G")), null, 3).Refused);
    }

    [Fact]
    public void A_finger_on_a_note_the_page_cuts_refuses_the_page()
    {
        // As the correction across the place: bar 2's C4 tied into page 2's first bar is not the same note once the page is in.
        Project read = ProjectFile.Read(Encoding.UTF8.GetBytes(TiedAcross()));
        Note across = OpenedProject.Open(read).Notes.Single(note => note.Hand?.Value == Hand.Right && note.Span.Duration == 960);
        Project fingered = read with { Marks = read.Marks.SetFingering(new Fingering(across.Id, 2)) };

        Assert.Equal(PageRefusal.Notes, ScanPageInsert.Into(fingered, 2, Encoding.UTF8.GetBytes(Page("G")), null, 3).Refused);
        Assert.NotNull(ScanPageInsert.Into(read, 2, Encoding.UTF8.GetBytes(Page("G")), null, 3).Project);
    }

    [Fact]
    public void A_draft_refuses_the_page_in_words_that_name_what_stops_it()
    {
        // As the iPad's SCAN-05 test makes it: drafts.add of bar 1 on the notes read, saved, then the page read again.
        var facade = new MusicCoreFacade();
        Assert.DoesNotContain("\"error\"", facade.Open("scan.motiff.json", Encoding.UTF8.GetBytes(Read(3, Page("C"), Page("E"))), null), StringComparison.Ordinal);
        Assert.DoesNotContain("\"error\"", facade.Call(1, "drafts.add", $$"""{"bars": {"first": 1, "last": 1}, "now": "{{Now}}"}"""), StringComparison.Ordinal);
        string file = facade.Call(1, "project.save", "{}");

        using JsonDocument refused = Insert(file, 2, Page("G"));
        using JsonDocument russian = JsonDocument.Parse(new MusicCoreFacade().Call(0, "scan.insert", JsonSerializer.Serialize(new { file, page = 2, reading = Page("G"), pages = 3, language = "ru" })));

        Assert.Equal("unavailable", refused.RootElement.GetProperty("error").GetString());
        Assert.StartsWith("The page can be put into notes read from this PDF only while they are not compared with a version", refused.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("Страницу можно вставить в ноты из этого PDF, только пока их не сравнивают с версией", russian.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void What_a_later_build_wrote_on_the_notes_refuses_the_page()
    {
        Project read = ProjectFile.Read(Encoding.UTF8.GetBytes(Read(3, Page("C"), Page("E"))));
        Project later = read with { Marks = read.Marks with { Unknown = [new UnknownEntity("marks.highlights", 0, "h_0009", null, "{}")] } };

        Assert.False(later.CanInsertPage);
        Assert.Equal(PageRefusal.Work, ScanPageInsert.Into(later, 2, Encoding.UTF8.GetBytes(Page("G")), null, 3).Refused);
    }

    public static TheoryData<string> Work => ["performance", "assembly", "draft", "no PDF"];

    [Theory]
    [MemberData(nameof(Work))]
    public void Versions_an_assembly_a_draft_or_no_PDF_refuse_the_page(string kind)
    {
        Project read = ProjectFile.Read(Encoding.UTF8.GetBytes(Read(3, Page("C"), Page("E"))));
        Assert.True(read.CanInsertPage);
        DateTimeOffset now = DateTimeOffset.Parse(Now, System.Globalization.CultureInfo.InvariantCulture);
        Project with = kind switch
        {
            "performance" => read.AddPerformance(new MotiffLab.MusicCore.Performance.SavedPerformance("perf_1", "Take", MotiffLab.MusicCore.Performance.PerformanceSource.Recording, read.Source, 0, [])),
            "assembly" => read with { Assembly = new PieceAssembly([new MotiffLab.MusicCore.Assembly.NoteStep(1, now, "n_0002", 1)], 2) },
            "draft" => read with { Drafts = [Drafts.DraftFixtures.Drafted(Drafts.DraftFixtures.Octaves())] },
            _ => read with { Document = null },
        };

        Assert.False(with.CanInsertPage);
        Assert.Equal(PageRefusal.Work, ScanPageInsert.Into(with, 2, Encoding.UTF8.GetBytes(Page("G")), null, 3).Refused);
    }

    [Fact]
    public void A_resume_point_before_the_page_stays_and_one_at_or_after_it_moves_on_by_the_page()
    {
        // v1.5 i-7 (the 1.4 plan's answer 9): moved on by the page's two bars of 4/4, 3840 ticks, rather than back to the start.
        Project read = ProjectFile.Read(Encoding.UTF8.GetBytes(Read(3, Page("C"), Page("E"))));
        ResumePoint start = read.ResumePoints[0];
        static SavedPassage Passage(long onset, long length) => new(new TickSpan(new Tick(onset), length), MotiffLab.MusicCore.Motifs.SelectionFilter.All);
        Project resumed = read with
        {
            ResumePoints =
            [
                start with { At = new Tick(960), Passage = Passage(960, 2880) },
                ResumePoint.Start("r_2", start.CreatedAt) with { At = new Tick(4 * 1920), Loop = true, Focused = true },
                ResumePoint.Start("r_3", start.CreatedAt) with { At = new Tick(960), Passage = Passage(960, 3840) },
                ResumePoint.Start("r_4", start.CreatedAt) with { At = new Tick(3840), Passage = Passage(3840, 1920) },
            ],
        };

        Project inserted = ScanPageInsert.Into(resumed, 2, Encoding.UTF8.GetBytes(Page("G")), null, 3).Project!;

        Assert.Equal([960L, 4 * 1920 + 3840, 960, 3840 + 3840], inserted.ResumePoints.Select(point => point.At.Value));
        Assert.Equal((true, true), (inserted.ResumePoints[1].Loop, inserted.ResumePoints[1].Focused));
        // A passage ending where the page goes stays; one across it takes the page in; one from it on moves on whole.
        Assert.Equal([(960L, 2880L), (960L, 3840L + 3840), (7680L, 1920L)], inserted.ResumePoints.Where(point => point.Passage is not null).Select(point => (point.Passage!.Span.Onset.Value, point.Passage.Span.Duration)));
        // The page's own music is under the moved point: bar 5 of the notes now, page 3's first E5 as before.
        Assert.Equal(Four(76), RightHand(Encoding.UTF8.GetString(ProjectFile.Write(inserted)))[4]);
    }

    [Fact]
    public void Bookmarks_no_longer_refuse_the_page_and_move_on_by_it_their_bars_title_renumbered()
    {
        Project read = ProjectFile.Read(Encoding.UTF8.GetBytes(Read(3, Page("C"), Page("E"))));
        DateTimeOffset now = DateTimeOffset.Parse(Now, System.Globalization.CultureInfo.InvariantCulture);
        MeterMap meter = OpenedProject.Open(read).Piece.Meter;
        Bookmark Marked(string id, int first, int last, string? title = null)
        {
            TickSpan span = TickSpan.FromBounds(meter.GetBar(first).Span.Onset, meter.GetBar(last).Span.End);
            return new Bookmark(id, title ?? Bookmark.BarsTitle(meter, span, Words.English), "teacher", span, false, now);
        }

        Project marked = read with { Bookmarks = [Marked("b_1", 1, 2), Marked("b_2", 3, 4), Marked("b_3", 2, 3, "Тема"), Marked("b_4", 4, 4, "Coda, " + Bookmark.BarsTitle(meter, meter.GetBar(4).Span, Words.Russian))] };
        Assert.True(marked.CanInsertPage);

        Project inserted = ScanPageInsert.Into(marked, 2, Encoding.UTF8.GetBytes(Page("G")), null, 3).Project!;

        MeterMap after = OpenedProject.Open(inserted).Piece.Meter;
        Assert.Equal([(1, 2), (5, 6), (2, 5), (6, 6)], inserted.Bookmarks.Select(bookmark => (after.BarAt(bookmark.Span.Onset).Number, after.BarAt(new Tick(bookmark.Span.End.Value - 1)).Number)));
        Assert.Equal(Bookmark.BarsTitle(after, inserted.Bookmarks[0].Span, Words.English), inserted.Bookmarks[0].Title);
        Assert.Equal(Bookmark.BarsTitle(after, inserted.Bookmarks[1].Span, Words.English), inserted.Bookmarks[1].Title);
        Assert.Equal("Тема", inserted.Bookmarks[2].Title);
        Assert.Equal("Coda, " + Bookmark.BarsTitle(after, inserted.Bookmarks[3].Span, Words.Russian), inserted.Bookmarks[3].Title);
        using JsonDocument answered = Insert(Encoding.UTF8.GetString(ProjectFile.Write(marked)), 2, Page("G"));
        Assert.False(answered.RootElement.TryGetProperty("error", out _), answered.RootElement.ToString());
    }

    [Fact]
    public void Check_answers_where_each_page_of_the_notes_starts_and_ends_so_the_app_can_mark_a_page_not_read()
    {
        var facade = new MusicCoreFacade();
        Assert.DoesNotContain("\"error\"", facade.Open("scan.motiff.json", Encoding.UTF8.GetBytes(Read(3, Page("C"), Page("E"))), null), StringComparison.Ordinal);

        using JsonDocument check = JsonDocument.Parse(facade.Call(1, "check", "{}"));

        Assert.Equal(
            [(1, 1, 0L, 2, 1920L), (2, 3, 3840L, 4, 5760L)],
            check.RootElement.GetProperty("pageStarts").EnumerateArray().Select(page => (page.GetProperty("page").GetInt32(), page.GetProperty("bar").GetInt32(), page.GetProperty("tick").GetInt64(), page.GetProperty("lastBar").GetInt32(), page.GetProperty("lastTick").GetInt64())));
    }

    [Fact]
    public void The_page_s_report_goes_in_at_its_place_and_a_page_without_one_leaves_the_notes_keeping_none()
    {
        // v1.5 i-7, review 1: a page read again with no report (by a job that sent none) goes in rather than being refused.
        string file = Read(3, [Report(0.7), Report(0.8)], Page("C"), Page("E"));

        using JsonDocument inserted = Insert(file, 2, Page("G"), reports: [Report(0.9)]);
        using JsonDocument without = Insert(file, 2, Page("G"));

        Project project = ProjectFile.Read(Encoding.UTF8.GetBytes(inserted.RootElement.GetProperty("file").GetString()!));
        Assert.Equal([(1, 0.7), (2, 0.9), (3, 0.8)], project.Scan!.Pages.Select(report => (report.Page, report.Sharpness)));
        Assert.False(without.RootElement.TryGetProperty("error", out _));
        Project kept = ProjectFile.Read(Encoding.UTF8.GetBytes(without.RootElement.GetProperty("file").GetString()!));
        Assert.Empty(kept.Scan!.Pages);
        Assert.Equal(3, ScanJoin.Split(kept.Source).Count);
    }

    [Fact]
    public void Bars_the_engines_disagree_on_are_flagged_on_the_page_put_in()
    {
        string file = Read(3, Page("C"), Page("E"));
        static string Disagreeing(string step) => Encoding.UTF8.GetString(Score(2, "4", Bar("G", 5), Bar(step, 4)));
        object[] readings = [new { engine = "homr", musicxml = Disagreeing("A") }, new { engine = "copisteria", musicxml = Disagreeing("B") }, new { engine = "audiveris", musicxml = Disagreeing("D") }];

        using JsonDocument inserted = Insert(file, 2, readings);

        Project project = ProjectFile.Read(Encoding.UTF8.GetBytes(inserted.RootElement.GetProperty("file").GetString()!));
        Assert.Equal([4], project.Check!.Where(flag => flag.Reason == CheckReason.EnginesDisagree).Select(flag => flag.Bar));
        Assert.Equal("check", inserted.RootElement.GetProperty("pages")[0].GetProperty("merge").GetProperty("bars")[1].GetProperty("level").GetString());
    }
}