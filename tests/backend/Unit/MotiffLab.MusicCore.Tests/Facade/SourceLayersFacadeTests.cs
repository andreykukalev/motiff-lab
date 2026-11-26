using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;

using static MotiffLab.MusicCore.Tests.Facade.FacadeCalls;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>
/// The sources by the layers of 0030 through the facade (version 0.6, within contract 12), for the Sources sheet and the
/// switch of views on the web and the iPad: originals and Notes with their states, the views in order with their groups,
/// hiding a view, recognising again, making other Notes the working ones, and removing and reviving an original.
/// </summary>
public class SourceLayersFacadeTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly byte[] s_pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");
    private static readonly byte[] s_otherPdf = Encoding.ASCII.GetBytes("%PDF-1.4\nanother edition\n%%EOF\n");

    private static byte[] Scanned() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Scan", "Fixtures", "homr-fur-elise-page1.musicxml"));

    /// <summary>A PDF read by the scan server, with the PDF here and the iPad holding it.</summary>
    private static MusicCoreFacade Opened(bool online = false)
    {
        byte[] musicXml = Scanned();
        Project read = Project.CreateDocument("p_5a1c4e1a0b7d4c3e8f6a5b4c3d2e1f00", "Nocturne.pdf", s_pdf, s_now)
            .Recognised(musicXml, "homr 0.7.0", PageMap.Of(PieceImporter.Import("a.musicxml", musicXml).Piece!, 1), s_now)
            .MarkHeld(PieceOriginal.DocumentId, HoldingDevice.Web, held: true, s_now);
        var facade = new MusicCoreFacade();
        JsonElement opened = Json(facade.Open("Nocturne.motiff.json", ProjectFile.Write(online ? read.ForOnline()! : read), """{"language": "en"}"""));
        Assert.Equal(1, opened.GetProperty("piece").GetInt32());
        return facade;
    }

    private static string[] Views(JsonElement answer) =>
        [.. answer.GetProperty("views").EnumerateArray().Select(view => $"{view.GetProperty("kind").GetString()}:{view.GetProperty("id").GetString()}")];

    private static JsonElement Item(JsonElement answer, string list, string id) =>
        answer.GetProperty(list).EnumerateArray().Single(each => each.GetProperty("id").GetString() == id);

    private static string Recognise() => JsonSerializer.Serialize(new { original = "src_pdf", musicXml = Convert.ToBase64String(Scanned()), engine = "homr 0.8.0", pages = 1, now = "2026-11-15T10:00:00Z" });

    [Fact]
    public void The_layers_list_the_originals_and_notes_with_their_states_and_the_views_pdf_before_notes()
    {
        MusicCoreFacade facade = Opened();

        JsonElement layers = Call(facade, "sources.layers");

        Assert.Equal("n_1", layers.GetProperty("working").GetString());
        Assert.Equal(["original:src_pdf", "notes:n_1"], Views(layers));
        Assert.Equal("src_pdf", Item(layers, "views", "n_1").GetProperty("group").GetString());
        JsonElement pdf = Item(layers, "originals", "src_pdf");
        Assert.Equal(("here", "pdf"), (pdf.GetProperty("state").GetString(), pdf.GetProperty("kind").GetString()));
        Assert.Equal(["n_1"], pdf.GetProperty("notes").EnumerateArray().Select(id => id.GetString()));
        JsonElement notes = Item(layers, "notes", "n_1");
        Assert.Equal(("working", "pdfRecognition", "homr 0.7.0", "here"), (notes.GetProperty("role").GetString(), notes.GetProperty("op").GetString(), notes.GetProperty("engine").GetString(), notes.GetProperty("state").GetString()));
        Assert.True(Item(layers, "views", "n_1").GetProperty("working").GetBoolean());
        Assert.Equal(JsonValueKind.Object, Call(facade, "sources").GetProperty("document").ValueKind);
    }

    [Fact]
    public void A_pdf_only_on_the_web_is_on_another_device_for_the_ipad_and_no_view()
    {
        MusicCoreFacade facade = Opened(online: true);

        JsonElement layers = Call(facade, "sources.layers");

        Assert.Equal("onAnotherDevice", Item(layers, "originals", "src_pdf").GetProperty("state").GetString());
        Assert.Equal("onAnotherDevice", Item(layers, "notes", "n_1").GetProperty("state").GetString());
        Assert.Equal(["notes:n_1"], Views(layers));
        Assert.Equal("here", Item(Call(facade, "sources.layers", """{"device": "web"}"""), "originals", "src_pdf").GetProperty("state").GetString());
        Assert.Equal("badRequest", Error(facade, "sources.layers", """{"device": "phone"}"""));
    }

    [Fact]
    public void Notes_read_again_take_the_working_notes_corrections_and_say_so_and_none_without_any()
    {
        MusicCoreFacade facade = Opened();
        ImportedPiece read = PieceImporter.Import("a.musicxml", Scanned()).Piece!;
        Note first = read.Notes[0];
        int bar = read.Meter.BarAt(first.Span.Onset).Number;
        Assert.False(Call(facade, "sources.notes.recognise", Recognise()).TryGetProperty("carried", out _));
        Call(facade, "corrections.set", $$"""{"bar": {{bar}}, "id": "{{first.Id}}", "pitch": {{first.Pitch.MidiNumber + 1}}}""");

        JsonElement carried = Call(facade, "sources.notes.recognise", Recognise()).GetProperty("carried");

        Assert.Equal((1, 0, 0), (carried.GetProperty("carried").GetInt32(), carried.GetProperty("byReading").GetInt32(), carried.GetProperty("notCarried").GetInt32()));
        Assert.Equal("1 correction carried over", carried.GetProperty("text").GetString());
        Call(facade, "sources.makeWorking", """{"id": "n_3"}""");
        NoteEdit edit = Assert.Single(ProjectFile.Read(Encoding.UTF8.GetBytes(facade.Call(1, "project.save"))).Edits.Edits);
        Assert.Equal((first.Pitch.MidiNumber + 1, "g_1"), (edit.After!.Value.Pitch.MidiNumber, edit.Group));
    }

    [Fact]
    public void A_correction_the_notes_read_again_read_otherwise_is_flagged_in_their_check_in_both_languages()
    {
        MusicCoreFacade facade = Opened();
        byte[] scanned = Scanned();
        Note first = PieceImporter.Import("a.musicxml", scanned).Piece!.Notes.OrderBy(static note => note.Span.Onset).ThenBy(static note => note.Pitch.MidiNumber).First();
        int bar = PieceImporter.Import("a.musicxml", scanned).Piece!.Meter.BarAt(first.Span.Onset).Number;
        Call(facade, "corrections.set", $$"""{"bar": {{bar}}, "id": "{{first.Id}}", "pitch": {{first.Pitch.MidiNumber + 1}}}""");
        string xml = Encoding.UTF8.GetString(scanned);
        int octave = xml.IndexOf("<octave>", StringComparison.Ordinal) + "<octave>".Length;
        byte[] otherwise = Encoding.UTF8.GetBytes(string.Concat(xml.AsSpan(0, octave), ((char)(xml[octave] == '3' ? '2' : '3')).ToString(), xml.AsSpan(octave + 1)));
        string request = JsonSerializer.Serialize(new { original = "src_pdf", musicXml = Convert.ToBase64String(otherwise), engine = "homr 0.8.0", pages = 1, now = "2026-11-15T10:00:00Z" });

        JsonElement carried = Call(facade, "sources.notes.recognise", request).GetProperty("carried");
        Call(facade, "sources.makeWorking", """{"id": "n_2"}""");

        Assert.Equal(1, carried.GetProperty("notCarried").GetInt32());
        Assert.Equal([bar], carried.GetProperty("bars").EnumerateArray().Select(static each => each.GetInt32()));
        Assert.Contains(ProjectFile.Read(Encoding.UTF8.GetBytes(facade.Call(1, "project.save"))).Check!, flag => flag.Bar == bar && flag.Reason == CheckReason.CorrectionUnfit && !flag.Resolved);
        Assert.Equal("Your correction did not fit", CheckFlag.NameOf(CheckReason.CorrectionUnfit, Words.English));
        Assert.StartsWith("Ваше исправление не подошло к новому чтению", CheckFlag.Describe(CheckReason.CorrectionUnfit, Words.Russian), StringComparison.Ordinal);
    }

    [Fact]
    public void Notes_read_again_are_grouped_under_their_pdf_can_be_hidden_and_made_working_and_back()
    {
        MusicCoreFacade facade = Opened();

        JsonElement again = Call(facade, "sources.notes.recognise", Recognise());
        Assert.Equal("n_2", again.GetProperty("id").GetString());
        Assert.Equal(["original:src_pdf", "notes:n_1", "notes:n_2"], Views(again));
        Assert.Equal("src_pdf", Item(again, "views", "n_2").GetProperty("group").GetString());
        Assert.Equal("other", Item(again, "notes", "n_2").GetProperty("role").GetString());

        JsonElement hidden = Call(facade, "sources.view.hide", """{"id": "n_2", "hidden": true}""");
        Assert.Equal(1, hidden.GetProperty("hidden").GetInt32());
        Assert.True(Item(hidden, "views", "n_2").GetProperty("hidden").GetBoolean());
        Assert.Equal("unavailable", Error(facade, "sources.view.hide", """{"id": "n_1", "hidden": true}"""));

        JsonElement reopened = Call(facade, "sources.makeWorking", """{"id": "n_2"}""");
        Assert.Equal(1, reopened.GetProperty("piece").GetInt32());
        JsonElement swapped = Call(facade, "sources.layers");
        Assert.Equal("n_2", swapped.GetProperty("working").GetString());
        Assert.Equal(0, swapped.GetProperty("hidden").GetInt32());
        Assert.Equal(["original:src_pdf", "notes:n_2", "notes:n_1"], Views(swapped));

        Call(facade, "sources.makeWorking", """{"id": "n_1"}""");
        Assert.Equal("n_1", Call(facade, "sources.layers").GetProperty("working").GetString());
        Assert.Equal("badRequest", Error(facade, "sources.makeWorking", """{"id": "n_1"}"""));
        Assert.Equal("badRequest", Error(facade, "sources.notes.remove", """{"id": "n_9"}"""));
        Assert.DoesNotContain("notes:n_2", Views(Call(facade, "sources.notes.remove", """{"id": "n_2"}""")));
    }

    [Fact]
    public void A_removed_pdf_leaves_its_notes_removed_and_the_same_file_added_again_revives_it_beside_another_pdf()
    {
        MusicCoreFacade facade = Opened();

        JsonElement removed = Call(facade, "sources.original.remove", """{"id": "src_pdf", "now": "2026-11-15T11:00:00Z"}""");
        Assert.Equal(("removed", "2026-11-15T11:00:00.0000000+00:00"), (Item(removed, "originals", "src_pdf").GetProperty("state").GetString(), Item(removed, "originals", "src_pdf").GetProperty("removedAt").GetString()));
        Assert.Equal("removed", Item(removed, "notes", "n_1").GetProperty("state").GetString());
        Assert.Equal(["notes:n_1"], Views(removed));

        JsonElement revived = Call(facade, "sources.original.add", JsonSerializer.Serialize(new { kind = "pdf", fileName = "Nocturne.pdf", data = Convert.ToBase64String(s_pdf), now = "2026-11-15T12:00:00Z" }));
        Assert.Equal("src_pdf", revived.GetProperty("id").GetString());
        Assert.Equal("here", Item(revived, "originals", "src_pdf").GetProperty("state").GetString());

        JsonElement second = Call(facade, "sources.original.add", JsonSerializer.Serialize(new { kind = "pdf", fileName = "Henle.pdf", data = Convert.ToBase64String(s_otherPdf), now = "2026-11-15T12:00:00Z" }));
        Assert.Equal("src_pdf_2", second.GetProperty("id").GetString());
        Assert.Equal(["original:src_pdf", "notes:n_1", "original:src_pdf_2"], Views(second));
        Assert.Equal(Convert.ToBase64String(s_otherPdf), Call(facade, "sources.pdf", """{"id": "src_pdf_2"}""").GetProperty("pdf").GetString());
        Assert.Equal(Convert.ToBase64String(s_pdf), Call(facade, "sources.pdf").GetProperty("pdf").GetString());
        Assert.Equal("unavailable", Error(facade, "sources.original.remove", """{"id": "src_pdf", "withNotes": true}"""));
    }

    [Fact]
    public void A_recording_whose_sound_the_app_has_is_a_performance_view()
    {
        MusicCoreFacade facade = Opened();
        Call(facade, "sources.original.add", """{"kind": "audio", "fileName": "Live.m4a", "kept": "device", "durationMs": 161000, "now": "2026-11-15T12:00:00Z"}""");

        Assert.Equal(["original:src_pdf", "notes:n_1"], Views(Call(facade, "sources.layers")));
        JsonElement here = Call(facade, "sources.layers", """{"here": ["src_rec"]}""");
        Assert.Equal(["original:src_pdf", "notes:n_1", "performance:src_rec"], Views(here));
        Assert.Equal("nowhere", Item(Call(facade, "sources.layers"), "originals", "src_rec").GetProperty("state").GetString());
    }

    [Fact]
    public void A_piece_that_is_only_its_pdf_answers_its_layers_too()
    {
        var facade = new MusicCoreFacade();
        Assert.Equal(1, Json(facade.Open("Nocturne.pdf", s_pdf, """{"language": "en", "now": "2026-11-15T09:00:00Z"}""")).GetProperty("piece").GetInt32());

        JsonElement layers = Call(facade, "sources.layers");

        Assert.Equal(JsonValueKind.Null, layers.GetProperty("working").ValueKind);
        Assert.Empty(layers.GetProperty("notes").EnumerateArray());
        Assert.Equal(["original:src_pdf"], Views(layers));
    }

    private static string Heard()
    {
        ImportedPiece piece = PieceImporter.Import("fur-elise.musicxml", Fixture("fur-elise-original.musicxml")).Piece!;
        string midi = Convert.ToBase64String(global::MotiffLab.MusicCore.Export.MidiExporter.Export(piece.Notes, [], piece.Meter, "heard"));
        return $$$"""{"original": "src_rec", "engine": "transkun", "milliseconds": 60000, "sentFromMs": 30000, "parts": [{"index": 0, "midi": "{{{midi}}}"}], "now": "2026-11-15T13:00:00Z"}""";
    }

    [Fact]
    public void A_recording_already_added_is_heard_into_notes_beside_the_working_ones_with_the_stretch_sent()
    {
        MusicCoreFacade facade = Opened();
        Call(facade, "sources.original.add", """{"kind": "audio", "fileName": "Live.m4a", "kept": "device", "now": "2026-11-15T12:00:00Z"}""");

        JsonElement heard = Call(facade, "sources.notes.hear", Heard());

        Assert.Equal("n_2", heard.GetProperty("id").GetString());
        JsonElement notes = Item(heard, "notes", "n_2");
        Assert.Equal(("audioRecognition", "transkun", "src_rec"), (notes.GetProperty("op").GetString(), notes.GetProperty("engine").GetString(), notes.GetProperty("original").GetString()));
        Assert.Equal(["original:src_pdf", "notes:n_1", "performance:src_rec", "notes:n_2"], Views(Call(facade, "sources.layers", """{"here": ["src_rec"]}""")));
        Project saved = ProjectFile.Read(Encoding.UTF8.GetBytes(facade.Call(1, "project.save")));
        OtherNotes other = Assert.Single(saved.Sources.Notes);
        Assert.Equal((30_000L, 90_000L), other.Provenance.Original!.Milliseconds);
        Assert.Equal(30_000L, other.Transcription!.SentFromMilliseconds);
        Assert.Equal("badRequest", Error(facade, "sources.notes.hear", Heard().Replace("src_rec", "src_pdf", StringComparison.Ordinal)));
    }

    [Fact]
    public void Notes_that_are_not_working_open_under_a_new_number_only_to_be_shown()
    {
        MusicCoreFacade facade = Opened();
        Call(facade, "sources.notes.recognise", Recognise());

        JsonElement opened = Call(facade, "sources.notes.open", """{"id": "n_2"}""");

        int shown = opened.GetProperty("piece").GetInt32();
        Assert.Equal(2, shown);
        Assert.True(opened.GetProperty("readOnly").GetBoolean());
        Assert.False(Json(facade.Call(shown, "notation")).TryGetProperty("error", out _));
        Assert.False(Json(facade.Call(shown, "piece")).TryGetProperty("error", out _));
        Assert.Equal("readOnly", Json(facade.Call(shown, "project.save")).GetProperty("error").GetString());
        Assert.Equal("readOnly", Json(facade.Call(shown, "sources.makeWorking", """{"id": "n_1"}""")).GetProperty("error").GetString());
        Assert.Equal("n_1", Call(facade, "sources.layers").GetProperty("working").GetString());
        Assert.True(facade.Close(shown));
        Assert.Equal("badRequest", Error(facade, "sources.notes.open", """{"id": "n_9"}"""));
    }

    [Fact]
    public void A_second_pdf_takes_page_marks_of_its_own_that_stay_apart_from_the_first()
    {
        var facade = new MusicCoreFacade();
        Json(facade.Open("Nocturne.pdf", s_pdf, """{"language": "en", "now": "2026-11-15T09:00:00Z"}"""));
        Assert.Equal("src_pdf_2", Call(facade, "sources.original.add", JsonSerializer.Serialize(new { kind = "pdf", fileName = "Henle.pdf", data = Convert.ToBase64String(s_otherPdf), now = "2026-11-15T12:00:00Z" })).GetProperty("id").GetString());

        JsonElement marked = Call(facade, "document.bookmark.add", """{"original": "src_pdf_2", "page": 1, "name": "Coda", "now": "2026-11-15T12:30:00Z"}""");

        Assert.Single(marked.GetProperty("marks").GetProperty("bookmarks").EnumerateArray());
        Assert.Empty(Call(facade, "document.marks").GetProperty("marks").GetProperty("bookmarks").EnumerateArray());
        Assert.Equal(Convert.ToBase64String(s_otherPdf), Call(facade, "document", """{"original": "src_pdf_2"}""").GetProperty("pdf").GetString());
        Project saved = ProjectFile.Read(Encoding.UTF8.GetBytes(facade.Call(1, "project.save")));
        Assert.Equal("Coda", Assert.Single(saved.PageMarksOf("src_pdf_2").Bookmarks).Name);
        Assert.Equal(ProjectFile.Write(saved), ProjectFile.Write(ProjectFile.Read(ProjectFile.Write(saved))));
        Project removed = saved.RemoveOriginal("src_pdf_2", s_now);
        Assert.Equal("Coda", Assert.Single(Assert.Single(removed.Sources.Removed).Marks.Bookmarks).Name);
        Assert.Equal("Coda", Assert.Single(removed.AddOriginal(new PieceOriginal("x", OriginalKind.Pdf, "Henle.pdf", OriginalKept.File, s_now.AddDays(1)), s_otherPdf).PageMarksOf("src_pdf_2").Bookmarks).Name);
        Assert.Equal("badRequest", Error(facade, "document.marks", """{"original": "src_none"}"""));
    }
}