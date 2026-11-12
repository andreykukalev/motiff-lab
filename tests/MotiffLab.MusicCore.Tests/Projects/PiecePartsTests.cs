using System.Text;
using System.Text.Json;
using System.Xml.Linq;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Tests.Import.Scan;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// Several pieces over one original (v0.4, «Пьесы в файле»): a collection cut at the boundaries the player confirmed into
/// pieces of their own, whose notes join back into the collection's.
/// </summary>
public class PiecePartsTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private static int s_ids;

    private static PieceSplitOptions Options(string? playlist = null, HoldingDevice? device = null) =>
        new(s_now, () => $"p_part{Interlocked.Increment(ref s_ids)}") { Playlist = playlist, Device = device };

    private static byte[] TwoPieces() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Analysis", "Fixtures", "two-pieces.musicxml"));

    private static Project Book() => Project.Create("p_book", "two-pieces.musicxml", TwoPieces(), s_now);

    /// <summary>What a note is, without its id: the part's notes from 0, moved to where the part begins.</summary>
    private static string Shape(Note note, long by) =>
        $"{note.Pitch.MidiNumber}@{note.Span.Onset.Value + by}+{note.Span.Duration} v{note.Velocity} t{note.Track} voice{note.Voice} {note.Hand?.Value}:{note.Hand?.Attribution} {note.Attribution}";

    private static string[] Shapes(IEnumerable<Note> notes, long by = 0) =>
        [.. notes.Select(note => Shape(note, by)).Order(StringComparer.Ordinal)];

    /// <summary>Every part's notes, each moved back to where it was, as one list: the collection's notes when nothing was lost.</summary>
    private static string[] Joined(PieceSplit split) =>
        [.. split.Parts.SelectMany(part => Shapes(part.Notes, part.Span!.Value.Onset.Value)).Order(StringComparer.Ordinal)];

    [Fact]
    public void The_two_pieces_in_one_file_split_at_the_confirmed_boundary_give_fur_elise_and_the_ode_to_joy()
    {
        Project book = Book();
        ImportedPiece piece = OpenedProject.Open(book).Piece;
        FoundSections found = SectionFinder.Find(SectionEvidence.Of(piece, piece.Notes, PlaybackScore.FromPiece(piece).Tempos));
        FoundSection ode = found.Items[1];

        PieceSplit split = PieceParts.Split(book, [new("Für Elise", PartUnit.Bars, 0, ode.FirstBar - 1), new(ode.NameIn(Words.English), PartUnit.Bars, ode.FirstBar, ode.LastBar)], Options());

        Assert.Equal(2, split.Parts.Count);
        (PiecePart elise, PiecePart joy) = (split.Parts[0], split.Parts[1]);
        Assert.Equal(((0, 105), (106, 121)), (elise.Bars, joy.Bars));
        Assert.Equal(("Für Elise", "Ode to Joy"), (elise.Project.Metadata.Title, joy.Project.Metadata.Title));
        Assert.Equal(("Für Elise", "Ode to Joy"), (elise.Project.Details.Title.Value, joy.Project.Details.Title.Value));
        Assert.Equal(("Für Elise.musicxml", "Ode to Joy.musicxml"), (elise.Project.FileName, joy.Project.FileName));
        Assert.Equal(Joined(split), Shapes(piece.Notes));

        // The Ode starts at 0 in 4/4 and C major at a quarter of 100, with both staves; Für Elise keeps its pickup, 3/8 and 72.
        ImportedPiece second = OpenedProject.Open(joy.Project).Piece;
        Assert.Equal(0, joy.Notes.Min(note => note.Span.Onset.Value));
        Assert.Equal((new TimeSignature(4, 4), 1, 16), (second.Meter.SignatureAt(Tick.Zero), second.Meter.FirstBarNumber, second.Meter.BarsBefore(second.End).Count()));
        Assert.Equal((0, "major"), (second.MusicXml!.KeySignatures[0].Fifths, second.MusicXml.KeySignatures[0].Mode));
        Assert.Equal(600_000, PlaybackScore.FromPiece(second).Tempos[0].MicrosecondsPerQuarter);
        Assert.Equal(2, second.MusicXml.Parts[0].Staves);
        ImportedPiece first = OpenedProject.Open(elise.Project).Piece;
        Assert.Equal((new TimeSignature(3, 8), 0, 106), (first.Meter.SignatureAt(Tick.Zero), first.Meter.FirstBarNumber, first.Meter.BarsBefore(first.End).Count()));
    }

    [Fact]
    public void Each_piece_is_its_own_project_in_the_playlist_and_keeps_the_original_by_its_fingerprint()
    {
        PieceSplit split = PieceParts.Split(Book(), [new("Für Elise", PartUnit.Bars, 0, 105), new("Ode to Joy", PartUnit.Bars, 106, 121)], Options("Сборник", HoldingDevice.Ipad));

        PieceOriginal original = Assert.IsType<PieceOriginal>(split.Original);
        Assert.Equal((PieceParts.OriginalId, OriginalKind.MusicXml, "two-pieces.musicxml", OriginalKept.Device), (original.Id, original.Kind, original.FileName, original.Kept));
        Assert.Equal(PieceOriginal.Fingerprint(TwoPieces()), original.Sha256);
        Assert.Equal([HoldingDevice.Ipad], original.Held.Select(static mark => mark.Device));
        Assert.Equal("Сборник", split.Playlist);
        Assert.Equal(2, split.Parts.Select(static part => part.Project.Id).Distinct().Count());
        foreach (PiecePart part in split.Parts)
        {
            Assert.Equal(["Сборник"], part.Project.Metadata.Playlists);
            Assert.Equal(original, part.Project.FindOriginal(PieceParts.OriginalId));
            Assert.Equal(original.Sha256, part.Project.Originals.Single(each => each.Id == PieceParts.OriginalId).Sha256);

            // Saved and read back, the piece opens with the same notes and still names the original.
            Project read = ProjectFile.Read(ProjectFile.Write(part.Project));
            Assert.Equal(Shapes(part.Notes), Shapes(OpenedProject.Open(read).Notes));
            Assert.Equal(original, read.FindOriginal(PieceParts.OriginalId));
        }

        Assert.Equal("Two pieces: Für Elise and Ode to Joy", PieceParts.Split(Book(), [new(null, PartUnit.Bars, 106, 121)], Options()).Playlist);
        Assert.Equal("Two pieces: Für Elise and Ode to Joy · Часть 1", PieceParts.Split(Book(), [new(" ", PartUnit.Bars, 106, 121)], Options() with { Words = Words.Russian }).Parts[0].Project.Metadata.Title);
    }

    [Fact]
    public void A_cut_inside_a_piece_carries_the_divisions_key_time_staves_clefs_and_tempo_in_and_loses_nothing()
    {
        Project book = Book();
        ImportedPiece piece = OpenedProject.Open(book).Piece;

        // Bar 14 moves the left hand to the treble clef until bar 17; bar 15 begins inside it.
        PieceSplit split = PieceParts.Split(book, [new("A", PartUnit.Bars, 0, 14), new("B", PartUnit.Bars, 15, 60), new("C", PartUnit.Bars, 61, 121)], Options());

        Assert.Equal(Joined(split), Shapes(piece.Notes));
        XElement measure = XDocument.Parse(Encoding.UTF8.GetString(split.Parts[1].Project.Source)).Root!.Element("part")!.Element("measure")!;
        XElement attributes = measure.Element("attributes")!;
        Assert.Equal("1", (string?)measure.Attribute("number"));
        Assert.Equal(["divisions", "key", "time", "staves", "clef", "clef"], attributes.Elements().Select(static child => child.Name.LocalName));
        Assert.Equal(("384", "3", "G"), (attributes.Element("divisions")!.Value, attributes.Element("time")!.Element("beats")!.Value, attributes.Elements("clef").Last().Element("sign")!.Value));
        Assert.Equal("72", (string?)measure.Element("sound")!.Attribute("tempo"));
        Assert.Equal(833_333, PlaybackScore.FromPiece(OpenedProject.Open(split.Parts[1].Project).Piece).Tempos[0].MicrosecondsPerQuarter);
    }

    [Fact]
    public void The_same_collection_and_parts_give_the_same_files()
    {
        PartRange[] parts = [new("Für Elise", PartUnit.Bars, 0, 105) { Id = "p_1" }, new("Ode to Joy", PartUnit.Bars, 106, 121) { Id = "p_2" }];

        string[] Files() => [.. PieceParts.Split(Book(), parts, Options()).Parts.Select(static part => Encoding.UTF8.GetString(ProjectFile.Write(part.Project)))];

        Assert.Equal(Files(), Files());
    }

    [Fact]
    public void Parts_out_of_order_overlapping_or_outside_the_piece_are_refused_and_a_midi_file_is_not_split_yet()
    {
        Project book = Book();

        Assert.Throws<ArgumentException>(() => PieceParts.Split(book, [], Options()));
        Assert.Throws<ArgumentException>(() => PieceParts.Split(book, [new("A", PartUnit.Bars, 0, 60), new("B", PartUnit.Bars, 50, 121)], Options()));
        Assert.Throws<ArgumentException>(() => PieceParts.Split(book, [new("B", PartUnit.Bars, 106, 121), new("A", PartUnit.Bars, 0, 105)], Options()));
        Assert.Throws<ArgumentException>(() => PieceParts.Split(book, [new("A", PartUnit.Bars, 100, 122)], Options()));
        Assert.Throws<ArgumentException>(() => PieceParts.Split(book, [new("A", PartUnit.Bars, 10, 9)], Options()));
        Assert.Throws<ArgumentException>(() => PieceParts.Split(book, [new("A", PartUnit.Pages, 1, 1)], Options()));
        byte[] midi = MidiExporter.Export(OpenedProject.Open(book).Notes, [], MeterMap.Default, "book");
        Assert.Throws<NotSupportedException>(() => PieceParts.Split(Project.Create("p_mid", "book.mid", midi, s_now), [new("A", PartUnit.Bars, 1, 2)], Options()));
    }

    /// <summary>A scan of three pages read one at a time, with the PDF it was read from, a doubtful bar on page 2 and a bar the player checked on page 3.</summary>
    private static Project Scan()
    {
        byte[] score = ScanJoin.Join([ScanJoinTests.Page(3), ScanJoinTests.Page(2), ScanJoinTests.Page(2)]);
        ImportedPiece piece = PieceImporter.Import("scan.musicxml", score).Piece!;
        Project pdf = Project.CreateDocument("p_scan", "Songbook.pdf", Pdf(3), s_now);
        return pdf.Recognised(score, "homr 0.7.0", PageMap.Of(piece, 3), s_now) with
        {
            Check = [new CheckFlag(4, CheckSource.Scan, CheckReason.BarTooShort, false), new CheckFlag(6, CheckSource.Scan, CheckReason.GraceNotes, true)],
            Scan = new ScanOrigin("homr 0.7.0", [6]) { From = PieceOriginal.DocumentId, At = s_now },
        };
    }

    private static byte[] Pdf(int pages) =>
        Encoding.ASCII.GetBytes($"%PDF-1.4\n1 0 obj << /Type /Pages /Count {pages} >> endobj\n" + string.Concat(Enumerable.Range(2, pages).Select(n => $"{n} 0 obj << /Type /Page >> endobj\n")) + "%%EOF\n");

    [Fact]
    public void A_scan_split_by_pages_takes_their_bars_its_own_pdf_and_the_check_flags_and_checked_bars_on_them()
    {
        Project book = Scan();
        OpenedProject opened = OpenedProject.Open(book);
        byte[] pages = Pdf(2);

        PieceSplit split = PieceParts.Split(book, [new("Prelude", PartUnit.Pages, 1, 1), new("Waltz", PartUnit.Pages, 2, 3) { Pdf = pages }], Options());

        (PiecePart prelude, PiecePart waltz) = (split.Parts[0], split.Parts[1]);
        Assert.Equal(((1, 1), (2, 3)), (prelude.Pages, waltz.Pages));
        Assert.Equal(((1, 3), (4, 7)), (prelude.Bars, waltz.Bars));
        Assert.Equal(Joined(split), Shapes(opened.Notes));
        Assert.All(waltz.Notes, note => Assert.NotEqual(Provenance.Imported, note.Attribution.Provenance));

        // The waltz has its own PDF of the two pages, starting where they did; the prelude only its notes.
        Assert.Equal(pages, waltz.Project.Document);
        Assert.Equal([Tick.Zero, new Tick(2 * 3 * 480)], waltz.Project.DocumentPages!.Starts);
        Assert.Equal(PieceOriginal.Fingerprint(pages), waltz.Project.FindOriginal(PieceOriginal.DocumentId)!.Sha256);
        Assert.Equal((PieceOriginal.DocumentId, PieceParts.OriginalId), (waltz.Project.Scan!.From, prelude.Project.Scan!.From));
        Assert.Null(prelude.Project.Document);

        // Bar 4 of the book is bar 1 of the waltz, bar 6 its bar 3, still checked.
        Assert.Equal([(1, CheckReason.BarTooShort, false), (3, CheckReason.GraceNotes, true)], waltz.Project.Check!.Select(static flag => (flag.Bar, flag.Reason, flag.Resolved)));
        Assert.Equal([3], waltz.Project.Scan.ConfirmedBars);
        Assert.Empty(prelude.Project.Check!);
        Assert.Equal(("pdf", 3), (split.Original!.Kind.ToString().ToLowerInvariant(), split.Original.Pages));
        Assert.Equal(PieceOriginal.Fingerprint(book.Document!), split.Original.Sha256);
        Assert.Equal(2, PageMap.Of(OpenedProject.Open(waltz.Project).Piece, 2).Pages);
    }

    [Fact]
    public void A_pdf_without_notes_is_split_into_pdf_pieces_of_the_pages_the_app_copied()
    {
        Project book = Project.CreateDocument("p_pdf", "Songbook.pdf", Pdf(5), s_now);
        (byte[] first, byte[] rest) = (Pdf(2), Pdf(3));

        PieceSplit split = PieceParts.Split(book, [new("One", PartUnit.Pages, 1, 2) { Pdf = first }, new("Two", PartUnit.Pages, 3, 5) { Pdf = rest }], Options("Songbook"));

        Assert.Equal([first, rest], split.Parts.Select(static part => part.Project.Document));
        Assert.All(split.Parts, part => Assert.False(part.Project.HasNotes));
        Assert.Equal(("One.pdf", "One"), (split.Parts[0].Project.FileName, split.Parts[0].Project.Details.Title.Value));
        Assert.Equal(PieceOriginal.Fingerprint(book.Document!), split.Original!.Sha256);
        Assert.Equal(5, split.Original.Pages);
        Assert.Throws<ArgumentException>(() => PieceParts.Split(book, [new("One", PartUnit.Pages, 1, 2)], Options()));
        Assert.Throws<ArgumentException>(() => PieceParts.Split(book, [new("One", PartUnit.Bars, 1, 2) { Pdf = first }], Options()));
        Assert.Throws<ArgumentException>(() => PieceParts.Split(book, [new("One", PartUnit.Pages, 4, 6) { Pdf = first }], Options()));
    }

    private const long Beat = 250_000;

    /// <summary>
    /// A recording heard: 64 notes, one on each beat of a beat that bends with the playing, at 120 BPM as an engine writes
    /// them, and bars worked out with <paramref name="stepsPerBeat"/> 16ths a beat and a pickup of <paramref name="pickup"/> 16ths.
    /// </summary>
    private static Project Recording(int stepsPerBeat, int pickup = 0)
    {
        long[] beats = [.. Enumerable.Range(0, 70).Select(static beat => (beat * Beat) + (beat % 3 * 7_001))];
        Note[] notes = [.. Enumerable.Range(0, 64).Select(i => new Note
        {
            Id = $"h{i}",
            Pitch = new Pitch(i % 4 == 0 ? 48 : 64 + (i % 7)),
            Span = new TickSpan(new Tick(beats[i] * 480 / 500_000), 200),
            Velocity = 60 + (i % 20),
            Track = 0,
            Attribution = Attribution.Imported,
        })];
        byte[] midi = MidiExporter.Export(notes, [], MeterMap.Default, "recording");
        var grid = new BarGrid(Tick.Zero, (int)(Beat / stepsPerBeat), TimeSignature.CommonTime, [pickup], beats, stepsPerBeat);
        return Project.Create("p_rec", "recording.mid", midi, s_now) with
        {
            Transcription = new TranscriptionOrigin("transkun 2", grid) { Seams = [9_000] },
            Check = [new CheckFlag(5, CheckSource.Transcription, CheckReason.PartSeam, false)],
        };
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(2, 3)]
    public void A_recording_split_by_bars_keeps_its_bars_and_notes_and_says_which_stretch_of_the_sound_each_part_takes(int stepsPerBeat, int pickup)
    {
        Project book = Recording(stepsPerBeat, pickup);
        OpenedProject opened = OpenedProject.Open(book);
        int last = opened.Piece.Meter.BarAt(new Tick(opened.Piece.End.Value - 1)).Number;

        PieceSplit split = PieceParts.Split(book, [new("Start", PartUnit.Bars, opened.Piece.Meter.FirstBarNumber, 3), new("End", PartUnit.Bars, 4, last)], Options());

        Assert.Equal(Joined(split), Shapes(opened.Notes));
        Assert.All(split.Parts.SelectMany(static part => part.Notes), note => Assert.Equal(Attribution.Detected(Transcribed.Confidence), note.Attribution));
        (PiecePart start, PiecePart end) = (split.Parts[0], split.Parts[1]);
        ImportedPiece second = OpenedProject.Open(end.Project).Piece;
        Assert.NotNull(second.Bars);
        Assert.Contains(second.Report.Issues, issue => issue.Code == BarGrid.IssueCode);
        Assert.Equal((1, TimeSignature.CommonTime), (second.Meter.FirstBarNumber, second.Meter.SignatureAt(Tick.Zero)));

        // The stretches follow each other; the end part starts at bar 4's beat as it was played.
        Assert.Equal(0, start.Milliseconds!.Value.From);
        Assert.Equal(start.Milliseconds.Value.To, end.Milliseconds!.Value.From);
        long bar4 = Beat * opened.Piece.Meter.GetBar(4).Span.Onset.Value / (stepsPerBeat * 120) / 1000;
        Assert.InRange(end.Milliseconds.Value.From, bar4 - 1, bar4 + 15);

        // The seam at 9 s and its Check flag go with the part they are in.
        bool seamInEnd = start.Milliseconds.Value.To < 9_000;
        Assert.Equal(seamInEnd ? [9_000 - end.Milliseconds.Value.From] : [], end.Project.Transcription!.Seams);
        Assert.Equal(seamInEnd ? [] : [9_000], start.Project.Transcription!.Seams);
        Assert.Equal(1, split.Parts.Sum(static part => part.Project.Check!.Count));

        // Saved and read back, the part opens on the same bars.
        Project read = ProjectFile.Read(ProjectFile.Write(end.Project));
        Assert.Equal(Shapes(end.Notes), Shapes(OpenedProject.Open(read).Notes));
    }

    [Fact]
    public void The_facade_answers_each_piece_as_a_project_file_to_save_beside_the_collection()
    {
        var facade = new MusicCoreFacade();
        string file = Encoding.UTF8.GetString(ProjectFile.Write(Book()));
        string request = JsonSerializer.Serialize(new
        {
            file,
            parts = new object[] { new { title = "Für Elise", bars = new { first = 0, last = 105 } }, new { title = "Ode to Joy", bars = new { first = 106, last = 121 } } },
            playlist = "Two pieces",
            device = "ipad",
            now = "2026-11-12T09:00:00Z",
        });

        using JsonDocument answer = JsonDocument.Parse(facade.Call(0, "parts.split", request));

        JsonElement root = answer.RootElement;
        Assert.Equal(("src_book", "musicxml"), (root.GetProperty("original").GetProperty("id").GetString(), root.GetProperty("original").GetProperty("kind").GetString()));
        Assert.Equal("Two pieces", root.GetProperty("playlist").GetString());
        JsonElement ode = root.GetProperty("parts")[1];
        Assert.Equal((106, 121), (ode.GetProperty("bars").GetProperty("first").GetInt32(), ode.GetProperty("bars").GetProperty("last").GetInt32()));
        Assert.Equal(JsonValueKind.Null, ode.GetProperty("pages").ValueKind);
        Assert.Equal(JsonValueKind.Null, ode.GetProperty("milliseconds").ValueKind);
        Assert.Equal(("Ode to Joy", "player"), (ode.GetProperty("project").GetProperty("title").GetString(), ode.GetProperty("project").GetProperty("titleFrom").GetString()));

        // The file opens as a piece of its own, from bar 1.
        using JsonDocument opened = JsonDocument.Parse(facade.Open("Ode to Joy.motiff.json", Encoding.UTF8.GetBytes(ode.GetProperty("file").GetString()!)));
        Assert.True(opened.RootElement.TryGetProperty("piece", out _), opened.RootElement.ToString());
        Assert.Equal(ode.GetProperty("notes").GetInt32(), OpenedProject.Open(ProjectFile.Read(Encoding.UTF8.GetBytes(ode.GetProperty("file").GetString()!))).Notes.Count);

        using JsonDocument refused = JsonDocument.Parse(facade.Call(0, "parts.split", JsonSerializer.Serialize(new { file, parts = new[] { new { bars = new { first = 50, last = 200 } } } })));
        Assert.Equal("badRequest", refused.RootElement.GetProperty("error").GetString());
        using JsonDocument none = JsonDocument.Parse(facade.Call(0, "parts.split", JsonSerializer.Serialize(new { file })));
        Assert.Equal("badRequest", none.RootElement.GetProperty("error").GetString());
    }
}