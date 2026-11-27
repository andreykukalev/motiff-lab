using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Projects;

using static MotiffLab.MusicCore.Tests.Facade.FacadeCalls;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>
/// A piece whose only original is a recording through the facade (version 1.1, finding 29, within contract 12): open
/// with "recording" makes it, it opens without notes as a PDF piece does, saved and opened again it is the same piece,
/// and «Распознать» (sources.notes.hear) makes the notes heard its working notes; a PDF piece without notes hears a
/// recording added to it the same way.
/// </summary>
public class RecordingPieceFacadeTests
{
    private static readonly byte[] s_sound = Encoding.ASCII.GetBytes("RIFF....WAVEfmt the sound of a take");
    private static readonly byte[] s_pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");

    private const string Request = """{"id": "p_rec", "now": "2026-11-19T23:30:00Z", "language": "en", "recording": {"durationMs": 183500}}""";

    /// <summary>The notes a transcription heard in the whole recording, as the app sends them.</summary>
    internal static string Heard(string original)
    {
        var piece = PieceImporter.Import("fur-elise.musicxml", Fixture("fur-elise-original.musicxml")).Piece!;
        string midi = Convert.ToBase64String(MidiExporter.Export(piece.Notes, [], piece.Meter, "heard"));
        return $$$"""{"original": "{{{original}}}", "engine": "transkun", "milliseconds": 60000, "parts": [{"index": 0, "midi": "{{{midi}}}"}], "now": "2026-11-19T23:40:00Z"}""";
    }

    private static (MusicCoreFacade Facade, JsonElement Opened) Recorded()
    {
        var facade = new MusicCoreFacade();
        JsonElement opened = Json(facade.Open("Etude in E.m4a", s_sound, Request));
        Assert.Equal(1, opened.GetProperty("piece").GetInt32());
        return (facade, opened);
    }

    [Fact]
    public void Open_with_a_recording_makes_a_piece_with_no_notes_whose_only_original_is_the_recording()
    {
        (_, JsonElement opened) = Recorded();

        JsonElement recording = opened.GetProperty("recording");
        Assert.Equal(("src_rec", "Etude in E.m4a", 183_500L), (recording.GetProperty("id").GetString(), recording.GetProperty("fileName").GetString(), recording.GetProperty("durationMs").GetInt64()));
        Assert.False(opened.TryGetProperty("document", out _));
        JsonElement project = opened.GetProperty("project");
        Assert.Equal(("p_rec", "Etude in E"), (project.GetProperty("id").GetString(), project.GetProperty("title").GetString()));
        Assert.False(project.GetProperty("hasNotes").GetBoolean());
        Assert.False(project.GetProperty("document").GetBoolean());
        JsonElement original = Assert.Single(project.GetProperty("originals").EnumerateArray());
        Assert.Equal(("audio", "device", PieceOriginal.Fingerprint(s_sound), (long)s_sound.Length), (original.GetProperty("kind").GetString(), original.GetProperty("kept").GetString(), original.GetProperty("sha256").GetString(), original.GetProperty("bytes").GetInt64()));
        Assert.Equal("ipad", Assert.Single(original.GetProperty("held").EnumerateArray()).GetProperty("device").GetString());
    }

    [Fact]
    public void A_recording_whose_bytes_are_not_sent_takes_the_fingerprint_the_app_worked_out()
    {
        string sha = PieceOriginal.Fingerprint(s_sound);
        JsonElement opened = Json(new MusicCoreFacade().Open("Etude in E.m4a", [], $$$"""{"recording": {"sha256": "{{{sha}}}"}}"""));

        JsonElement original = Assert.Single(opened.GetProperty("project").GetProperty("originals").EnumerateArray());
        Assert.Equal(sha, original.GetProperty("sha256").GetString());
        Assert.Equal(JsonValueKind.Null, original.GetProperty("durationMs").ValueKind);
        Assert.Equal("badRequest", Json(new MusicCoreFacade().Open("Etude in E.m4a", [], """{"recording": {"sha256": "not a fingerprint"}}""")).GetProperty("error").GetString());
    }

    [Fact]
    public void Saved_and_opened_again_it_is_the_same_piece_without_notes_not_a_piece_whose_original_is_away()
    {
        (MusicCoreFacade facade, JsonElement opened) = Recorded();

        string file = facade.Call(1, "project.save", """{"now": "2026-11-19T23:35:00Z"}""");
        JsonElement reopened = Json(new MusicCoreFacade().Open("Etude in E" + ProjectFile.Extension, Encoding.UTF8.GetBytes(file), """{"language": "en"}"""));

        Assert.Equal(18, Json(file).GetProperty("version").GetInt32());
        Assert.Equal(1, reopened.GetProperty("piece").GetInt32());
        Assert.Equal(opened.GetProperty("recording").GetRawText(), reopened.GetProperty("recording").GetRawText());
        Assert.Equal("Etude in E", Json(new MusicCoreFacade().Call(0, "project.summary", JsonSerializer.Serialize(new { file }))).GetProperty("project").GetProperty("title").GetString());
    }

    [Fact]
    public void It_answers_the_project_its_metadata_and_its_sources_and_refuses_the_calls_about_notes_or_pages()
    {
        (MusicCoreFacade facade, _) = Recorded();

        JsonElement sources = Call(facade, "sources", """{"language": "ru"}""");
        Assert.Equal(("p_rec", "audio", "запись, нот пока нет"), (sources.GetProperty("projectId").GetString(), sources.GetProperty("base").GetString(), sources.GetProperty("baseName").GetString()));
        JsonElement layers = Call(facade, "sources.layers", """{"here": ["src_rec"]}""");
        Assert.Equal(JsonValueKind.Null, layers.GetProperty("working").ValueKind);
        Assert.Equal(["performance:src_rec"], layers.GetProperty("views").EnumerateArray().Select(view => $"{view.GetProperty("kind").GetString()}:{view.GetProperty("id").GetString()}"));
        Assert.Equal("Этюд", Call(facade, "project.metadata", """{"title": "Этюд"}""").GetProperty("project").GetProperty("title").GetString());
        Assert.Equal("unavailable", Error(facade, "sources.original.remove", """{"id": "src_rec"}"""));
        Assert.Equal("noNotes", Error(facade, "document"));
        Assert.Equal("noNotes", Error(facade, "notation"));
    }

    [Fact]
    public void Hearing_the_recording_makes_the_notes_heard_the_working_notes_and_the_piece_opens_again_as_notes()
    {
        (MusicCoreFacade facade, _) = Recorded();

        JsonElement heard = Call(facade, "sources.notes.hear", Heard("src_rec"));

        Assert.Equal("n_1", heard.GetProperty("id").GetString());
        Assert.Equal("n_1", heard.GetProperty("working").GetString());
        Project saved = ProjectFile.Read(Encoding.UTF8.GetBytes(facade.Call(1, "project.save")));
        Assert.True(saved.HasNotes);
        Assert.Equal(("Etude in E.mid", "src_rec"), (saved.FileName, saved.WorkingProvenance.Original!.Original));
        Assert.Equal(PieceOriginal.Fingerprint(s_sound), saved.WorkingProvenance.Original.Sha256);
        Assert.Equal("Etude in E", saved.Details.Title.Value);
        var reopened = new MusicCoreFacade();
        JsonElement asNotes = Json(reopened.Open("Etude in E" + ProjectFile.Extension, ProjectFile.Write(saved), """{"language": "en"}"""));
        Assert.False(asNotes.TryGetProperty("recording", out _));
        Assert.False(asNotes.TryGetProperty("document", out _));
        Assert.NotEmpty(Call(reopened, "piece").GetProperty("notes").EnumerateArray());
    }

    [Fact]
    public void A_pdf_piece_without_notes_hears_a_recording_added_to_it_into_its_working_notes()
    {
        var facade = new MusicCoreFacade();
        Assert.Equal(1, Json(facade.Open("Nocturne.pdf", s_pdf, """{"id": "p_pdf", "now": "2026-11-19T23:30:00Z"}""")).GetProperty("piece").GetInt32());
        Assert.Equal("p_pdf", Call(facade, "sources").GetProperty("projectId").GetString());
        Call(facade, "sources.original.add", """{"kind": "audio", "fileName": "Live.m4a", "kept": "device", "now": "2026-11-19T23:31:00Z"}""");

        JsonElement heard = Call(facade, "sources.notes.hear", Heard("src_rec"));

        Assert.Equal(("n_1", "n_1"), (heard.GetProperty("id").GetString(), heard.GetProperty("working").GetString()));
        Project saved = ProjectFile.Read(Encoding.UTF8.GetBytes(facade.Call(1, "project.save")));
        Assert.True(saved.HasNotes);
        Assert.NotNull(saved.Document);
        Assert.Equal("src_rec", saved.WorkingProvenance.Original!.Original);
        var reopened = new MusicCoreFacade();
        JsonElement asNotes = Json(reopened.Open("Nocturne" + ProjectFile.Extension, ProjectFile.Write(saved), """{"language": "en"}"""));
        Assert.False(asNotes.TryGetProperty("document", out _));
        Assert.NotEmpty(Call(reopened, "piece").GetProperty("notes").EnumerateArray());
    }
}