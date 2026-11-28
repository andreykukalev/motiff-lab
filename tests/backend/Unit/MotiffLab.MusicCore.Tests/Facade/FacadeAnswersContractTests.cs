using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>
/// The facade's answers as data (audit C1, blueprint 5.4, step B5): tests/contracts/facade-answers/&lt;file&gt;.json holds,
/// text for text, what <see cref="MusicCoreFacade.Call"/> answers today to the calls the iPad decodes into its typed answers
/// (src/ipad/App/MotiffLab/Platform/PieceAnswers.swift), on the example piece both apps carry. A field renamed, retyped,
/// added or moved in C# fails here; tests/ipad/MotiffLabTests/FacadeAnswerFixturesTests.swift decodes the same files into
/// the typed answers, so the Swift side fails on the field it needs. Run the tests with MOTIFF_UPDATE_SNAPSHOTS=1 to write
/// the files again after a deliberate change of an answer.
/// </summary>
public partial class FacadeAnswersContractTests
{
    private const string UpdateVariable = "MOTIFF_UPDATE_SNAPSHOTS";

    private const string Piece = "FurElise.musicxml";

    /// <summary>The small PDF the requests send as "&lt;pdf&gt;".</summary>
    private const string Pdf = "%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n";

    /// <summary>The playlists file the playlists' requests send as "&lt;playlists&gt;", as the text of a JSON string: "Бах" with two pieces.</summary>
    private static readonly string s_playlistsFile = JsonEncodedText.Encode("""{"format":"motiff-lab-playlists","version":1,"minReader":1,"playlists":[{"id":"list_0001","name":"Бах","pieces":["p_answers","p_other"]}]}""", JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString();

    /// <summary>Calls whose answer the file does not keep, only their request: those that only build a file's state (another answer's).</summary>
    private static readonly HashSet<string> s_steps = ["performance.add", "performance.take"];

    /// <summary>Calls made on piece 0, with no piece, beside those on the piece open.</summary>
    private static readonly HashSet<string> s_withoutPiece = ["project.drafts", "scan.join", "catalogue", "reference.key", "reference.scale", "reference.chord", "reference.explorer", "settings.state", "library.plan", "playlists.migrate", "playlists", "playlists.create", "playlists.move", "playlists.resolve"];

    /// <summary>The files whose answers tell the project's times: the piece is opened at a fixed time, so they read the same tomorrow.</summary>
    private static readonly HashSet<string> s_dated = ["open", "marks", "document.marks"];

    /// <summary>The time those pieces are opened at.</summary>
    private const string OpenedAt = ",\"now\":\"2026-11-15T08:00:00Z\"";

    /// <summary>
    /// The files whose calls start from a project file and not from the example opened anew: the file the project is made
    /// from (the file's "fixture"), and how it is built.
    /// </summary>
    private static readonly Dictionary<string, (string Fixture, Func<byte[], byte[]> Build)> s_projects = new()
    {
        ["corrections.layer"] = (Piece, WithStaleCorrections),
        ["sources.layers.pdf"] = ("Nocturne.pdf", _ => PdfWithABookmark()),
        ["document.marks"] = ("Nocturne.pdf", _ => PdfWithABookmark()),
        ["drafts.techniques.heard"] = (Piece, WithAHeardVariant),
        ["drafts.recording"] = (Piece, data => Saved(WithARecording(data))),
        ["drafts.recording.new"] = (Piece, data => Saved(WithARecording(data))),
    };

    private static readonly JsonSerializerOptions s_written = new()
    {
        WriteIndented = true,
        IndentSize = 1,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static TheoryData<string> Files => [.. s_files.Keys];

    [Theory]
    [MemberData(nameof(Files))]
    public void The_file_holds_what_the_facade_answers_today(string name)
    {
        string written = Written(name);
        Assert.Equal(written, Written(name));

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(SourcePath(name), written);
            return;
        }

        string kept = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contracts", "facade-answers", $"{name}.json"));
        Assert.True(written == kept, $"tests/contracts/facade-answers/{name}.json differs from the facade's answers ({UpdateVariable}=1 writes it again)");
    }

    /// <summary>The file: a note, the file the piece is made from, and each call with its request and the facade's answer, opened in English.</summary>
    private static string Written(string name)
    {
        var facade = new MusicCoreFacade();
        byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", Piece));
        bool fromProject = s_projects.TryGetValue(name, out (string Fixture, Func<byte[], byte[]> Build) project);
        string at = s_dated.Contains(name) ? OpenedAt : "";
        string opened = fromProject
            ? facade.Open("p_answers.motiff.json", project.Build(data), $$"""{"language":"en","edits":"on"{{at}}}""")
            : facade.Open(Piece, data, $$"""{"language":"en","id":"p_answers"{{at}}}""");
        int piece = Number(opened);

        var answers = new JsonArray();
        foreach ((string call, string? request) in s_files[name])
        {
            JsonNode answer = JsonNode.Parse(call == "open" ? opened : facade.Call(s_withoutPiece.Contains(call) ? 0 : piece, call, WithBytes(request)))!;
            Assert.False(answer.AsObject().ContainsKey("error"), $"{call} {request}: {answer.ToJsonString()}");
            answers.Add(new JsonObject
            {
                ["call"] = call,
                ["request"] = request is null ? null : JsonNode.Parse(request),
                ["answer"] = s_steps.Contains(call) ? null : answer,
            });
        }

        var file = new JsonObject
        {
            ["note"] = "What music-core answers these calls on the example piece. Written and checked by tests/backend/Unit/MotiffLab.MusicCore.Tests/Facade/FacadeAnswersContractTests.cs ("
                + UpdateVariable + "=1 writes it); decoded into the iPad's typed answers by tests/ipad/MotiffLabTests/FacadeAnswerFixturesTests.swift.",
            ["fixture"] = fromProject ? project.Fixture : Piece,
            ["answers"] = answers,
        };
        return file.ToJsonString(s_written) + "\n";
    }

    /// <summary>The piece's number in the facade, opened from the file.</summary>
    private static int Opened(MusicCoreFacade facade, string fileName, byte[] data, string request) => Number(facade.Open(fileName, data, request));

    /// <summary>The piece's number in an open answer that must not fail.</summary>
    private static int Number(string opened)
    {
        JsonElement answer = FacadeCalls.Json(opened);
        Assert.False(answer.TryGetProperty("error", out JsonElement error), answer.ToString());
        return answer.GetProperty("piece").GetInt32();
    }

    /// <summary>
    /// The example's project file with a recording heard as Notes n_2 and a draft whose place is bars 1–4 of the notes and
    /// whose variant is bars 1–4 as heard.
    /// </summary>
    private static byte[] WithAHeardVariant(byte[] data)
    {
        (MusicCoreFacade facade, int piece) = WithARecording(data);
        int shown = FacadeCalls.Json(facade.Call(piece, "sources.notes.open", """{"id": "n_2"}""")).GetProperty("piece").GetInt32();
        Assert.DoesNotContain("error", facade.Call(shown, "drafts.add", """{"bars": {"first": 1, "last": 4}, "draft": "d_0001", "drop": "variant", "place": "pl_0001", "now": "2026-11-20T10:40:00Z"}"""), StringComparison.Ordinal);
        return Saved((facade, piece));
    }

    /// <summary>The example open with a recording heard as Notes n_2 and a draft whose place is bars 1–4 of the notes.</summary>
    private static (MusicCoreFacade Facade, int Piece) WithARecording(byte[] data)
    {
        var facade = new MusicCoreFacade();
        int piece = Opened(facade, Piece, data, """{"language":"en","id":"p_answers"}""");
        string[] calls =
        [
            "sources.original.add", """{"kind": "audio", "fileName": "Live.m4a", "kept": "device", "now": "2026-11-20T10:30:00Z"}""",
            "sources.notes.hear", FacadeCalls.Heard(),
            "drafts.add", """{"bars": {"first": 1, "last": 4}, "now": "2026-11-20T10:00:00Z"}""",
        ];
        for (int index = 0; index < calls.Length; index += 2)
        {
            Assert.DoesNotContain("error", facade.Call(piece, calls[index], calls[index + 1]), StringComparison.Ordinal);
        }

        return (facade, piece);
    }

    /// <summary>The project file of the piece open.</summary>
    private static byte[] Saved((MusicCoreFacade Facade, int Piece) open) => Encoding.UTF8.GetBytes(open.Facade.Call(open.Piece, "project.save"));

    /// <summary>
    /// The example's project file with bar 2's second A changed a semitone and its C taken out, and with four corrections
    /// that no longer fit the notes as they are (written as a file from elsewhere would hold them, EditsLayerFacadeTests):
    /// a change of a note "n_9998" that is not there, laid on the E of the same pitch and time; the C's removal again under
    /// an id that is not there; a change of the A3 from a pitch it no longer has; and a note added as "n_0015", an id taken.
    /// </summary>
    private static byte[] WithStaleCorrections(byte[] data)
    {
        var facade = new MusicCoreFacade();
        int piece = Opened(facade, Piece, data, """{"language":"en","id":"p_answers"}""");
        Assert.DoesNotContain("error", facade.Call(piece, "corrections.change", """{"bar":2,"id":"n_0010","pitch":1}"""), StringComparison.Ordinal);
        Assert.DoesNotContain("error", facade.Call(piece, "corrections.remove", """{"bar":2,"id":"n_0013"}"""), StringComparison.Ordinal);
        Project project = ProjectFile.Read(Encoding.UTF8.GetBytes(facade.Call(piece, "project.save")));
        IReadOnlyList<Note> notes = Example().Notes;
        NoteValues Imported(string id) => NoteValues.Of(notes.Single(note => note.Id == id));
        NoteEdit[] stale =
        [
            new("n_9998", Imported("n_0014"), Imported("n_0014") with { Velocity = 100 }),
            new("n_9999", Imported("n_0013"), null),
            new("n_0012", Imported("n_0012") with { Pitch = new Pitch(58) }, Imported("n_0012") with { Velocity = 60 }),
            new("n_0015", null, Imported("n_0015") with { Pitch = new Pitch(81) }),
        ];
        return ProjectFile.Write(project with { Edits = new NoteEditLog([.. project.Edits.Edits, .. stale]) });
    }

    /// <summary>The project file of a piece made from a PDF alone, as MusicCoreFacadeTests opens one, with a bookmark on its first page.</summary>
    private static byte[] PdfWithABookmark()
    {
        var facade = new MusicCoreFacade();
        int piece = Opened(facade, "Nocturne.pdf", Encoding.ASCII.GetBytes(Pdf), """{"language":"en","id":"p_answers","now":"2026-11-15T09:00:00Z"}""");
        Assert.DoesNotContain("error", facade.Call(piece, "document.bookmark.add", """{"page":1,"name":"Start","now":"2026-11-15T09:01:00Z"}"""), StringComparison.Ordinal);
        return Encoding.UTF8.GetBytes(facade.Call(piece, "project.save", """{"now":"2026-11-15T09:02:00Z"}"""));
    }

    /// <summary>
    /// The request as it is sent: the file keeps "&lt;pdf&gt;", "&lt;otherPdf&gt;" and "&lt;scan&gt;" in place of the bytes
    /// (base64) of two small PDFs and of the notes the scan server read from the example's first page, and "&lt;played&gt;"
    /// in place of a MIDI file of the example played with three notes more in bar 5; "&lt;homrPage&gt;" and
    /// "&lt;copisteriaPage&gt;" in place of the MusicXML text the two engines read from the example's first page; "&lt;playlists&gt;" in
    /// place of a playlists file's text.
    /// </summary>
    private static string? WithBytes(string? request) => request?
        .Replace("<pdf>", Convert.ToBase64String(Encoding.ASCII.GetBytes(Pdf)), StringComparison.Ordinal)
        .Replace("<otherPdf>", Convert.ToBase64String(Encoding.ASCII.GetBytes("%PDF-1.4\nanother edition\n%%EOF\n")), StringComparison.Ordinal)
        .Replace("<scan>", Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Scan", "Fixtures", "homr-fur-elise-page1.musicxml"))), StringComparison.Ordinal)
        .Replace("<homrPage>", request.Contains("<homrPage>", StringComparison.Ordinal) ? ScanPage("homr-fur-elise-page1") : "", StringComparison.Ordinal)
        .Replace("<copisteriaPage>", request.Contains("<copisteriaPage>", StringComparison.Ordinal) ? ScanPage("copisteria-fur-elise-page1") : "", StringComparison.Ordinal)
        .Replace("<played>", request.Contains("<played>", StringComparison.Ordinal) ? Played() : "", StringComparison.Ordinal)
        .Replace("<pieceA>", request.Contains("<pieceA>", StringComparison.Ordinal) ? PieceA() : "", StringComparison.Ordinal)
        .Replace("<playlists>", s_playlistsFile, StringComparison.Ordinal);

    /// <summary>A page an engine read, from the scan fixtures, as a JSON string's text.</summary>
    private static string ScanPage(string name) =>
        JsonEncodedText.Encode(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Import", "Scan", "Fixtures", $"{name}.musicxml")), JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString();

    /// <summary>Another piece's project file, as a JSON string's text: Ode to Joy with a draft "Ode" of its bars 1–2.</summary>
    private static string PieceA()
    {
        var facade = new MusicCoreFacade();
        int piece = Opened(facade, "Ode.musicxml", FacadeCalls.Fixture("ode-to-joy.musicxml"), """{"language":"en","id":"p_a","now":"2026-11-20T08:00:00Z"}""");
        Assert.DoesNotContain("error", facade.Call(piece, "drafts.add", """{"bars":{"first":1,"last":2},"name":"Ode","now":"2026-11-20T08:30:00Z"}"""), StringComparison.Ordinal);
        return JsonEncodedText.Encode(facade.Call(piece, "project.save", """{"now":"2026-11-20T08:45:00Z"}"""), JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString();
    }

    /// <summary>The example played with bar 5's three highest notes doubled a little off their pitch, as MusicCoreFacadeTests plays a performance.</summary>
    private static string Played()
    {
        ImportedPiece piece = Example();
        Note[] melody = [.. piece.Notes.Where(note => piece.Meter.GetBar(5).Span.Contains(note.Span.Onset)).OrderByDescending(note => note.Pitch.MidiNumber).Take(3)];
        Note[] played = [.. piece.Notes, .. melody.Select((note, i) => note with { Id = $"x{i}", Pitch = new Pitch(note.Pitch.MidiNumber + (i * 7) - 3) })];
        return Convert.ToBase64String(MidiExporter.Export(played, [], piece.Meter, "performance"));
    }

    /// <summary>The example piece as music-core imports it.</summary>
    private static ImportedPiece Example() =>
        PieceImporter.Import(Piece, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", Piece))).Piece!;

    private static string SourcePath(string name, [CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "..", "..", "contracts", "facade-answers", $"{name}.json");
}