using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Facade;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>
/// The facade's answers as data (audit C1, blueprint 5.4, step B5): tests/contracts/facade-answers/&lt;file&gt;.json holds,
/// text for text, what <see cref="MusicCoreFacade.Call"/> answers today to the calls the iPad decodes into its typed answers
/// (src/ipad/App/MotiffLab/Platform/PieceAnswers.swift), on the example piece both apps carry. A field renamed, retyped,
/// added or moved in C# fails here; tests/ipad/MotiffLabTests/FacadeAnswerFixturesTests.swift decodes the same files into
/// the typed answers, so the Swift side fails on the field it needs. Run the tests with MOTIFF_UPDATE_SNAPSHOTS=1 to write
/// the files again after a deliberate change of an answer.
/// </summary>
public class FacadeAnswersContractTests
{
    private const string UpdateVariable = "MOTIFF_UPDATE_SNAPSHOTS";

    private const string Piece = "FurElise.musicxml";

    /// <summary>The calls and requests each file keeps, in its order.</summary>
    private static readonly Dictionary<string, (string Call, string? Request)[]> s_files = new()
    {
        // A passage, and the same narrowed to a hand: "filter" names the hand only then.
        ["select"] =
        [
            ("select", """{"bars":{"first":4,"last":6}}"""),
            ("select", """{"bars":{"first":4,"last":6},"filter":{"hand":"right"}}"""),
        ],
        // The playback score the player loads: notes, pedal, tempos, the tempo shown, beats, bars and the hands.
        ["playback"] = [("playback", null)],
        // The sources by the layers of 0030, built as SourceLayersFacadeTests builds them: the example's own file, a PDF
        // added and read again into other Notes (a group, an engine, a time), a recording kept on the iPad (a performance
        // view with its sound here, "nowhere" without it), the read Notes hidden from the switch, a second PDF removed (a
        // tombstone with its time), and a version (Notes of no original, no group). Each of the sheet's operations
        // answers the layers too.
        ["sources.layers"] =
        [
            ("sources.layers", """{"device":"ipad","here":[]}"""),
            ("sources.original.add", """{"device":"ipad","here":[],"kind":"pdf","fileName":"Nocturne.pdf","kept":"file","data":"<pdf>","pages":1,"now":"2026-11-15T09:00:00Z"}"""),
            ("sources.notes.recognise", """{"device":"ipad","here":[],"original":"src_pdf","musicXml":"<scan>","engine":"homr 0.8.0","pages":1,"now":"2026-11-15T10:00:00Z"}"""),
            ("sources.original.add", """{"device":"ipad","here":[],"kind":"audio","fileName":"Live.m4a","kept":"device","durationMs":161000,"now":"2026-11-15T11:00:00Z"}"""),
            ("sources.view.hide", """{"device":"ipad","here":["src_rec"],"id":"n_2","hidden":true}"""),
            ("sources.original.add", """{"device":"ipad","here":["src_rec"],"kind":"pdf","fileName":"Henle.pdf","kept":"file","data":"<otherPdf>","pages":1,"now":"2026-11-15T12:00:00Z"}"""),
            ("sources.original.remove", """{"device":"ipad","here":["src_rec"],"id":"src_pdf_2","withNotes":false,"now":"2026-11-15T12:30:00Z"}"""),
            ("performance.add", """{"name":"Official","file":"<scan>","fileName":"Official.musicxml","now":"2026-11-15T13:00:00Z"}"""),
            ("sources.layers", """{"device":"ipad","here":["src_rec"]}"""),
            ("sources.layers", """{"device":"ipad","here":[]}"""),
        ],
    };

    /// <summary>Calls that only build a file's state: the file keeps their request, not their answer (another answer's).</summary>
    private static readonly HashSet<string> s_steps = ["performance.add"];

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

    /// <summary>The file: a note, the piece, and each call with its request and the facade's answer, opened in English.</summary>
    private static string Written(string name)
    {
        var facade = new MusicCoreFacade();
        byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", Piece));
        JsonElement opened = FacadeCalls.Json(facade.Open(Piece, data, """{"language":"en","id":"p_answers"}"""));
        Assert.False(opened.TryGetProperty("error", out JsonElement error), error.ToString());
        int piece = opened.GetProperty("piece").GetInt32();

        var answers = new JsonArray();
        foreach ((string call, string? request) in s_files[name])
        {
            JsonNode answer = JsonNode.Parse(facade.Call(piece, call, WithBytes(request)))!;
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
            ["fixture"] = Piece,
            ["answers"] = answers,
        };
        return file.ToJsonString(s_written) + "\n";
    }

    /// <summary>
    /// The request as it is sent: the file keeps "&lt;pdf&gt;", "&lt;otherPdf&gt;" and "&lt;scan&gt;" in place of the bytes
    /// (base64) of two small PDFs and of the notes the scan server read from the example's first page.
    /// </summary>
    private static string? WithBytes(string? request) => request?
        .Replace("<pdf>", Convert.ToBase64String(Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n")), StringComparison.Ordinal)
        .Replace("<otherPdf>", Convert.ToBase64String(Encoding.ASCII.GetBytes("%PDF-1.4\nanother edition\n%%EOF\n")), StringComparison.Ordinal)
        .Replace("<scan>", Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Scan", "Fixtures", "homr-fur-elise-page1.musicxml"))), StringComparison.Ordinal);

    private static string SourcePath(string name, [CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "..", "..", "contracts", "facade-answers", $"{name}.json");
}