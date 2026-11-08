using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Facade;

namespace MotiffLab.MusicCore.Tests.Golden;

/// <summary>
/// The phrases at their three levels, the form and the Piece DNA of the two pieces both apps carry, as the facade answers
/// them on Linux (task #293; basis rule S7 and pre-build check 2 of the project-continuity council). The golden lives
/// with the iPad tests (ipad/App/MotiffLabTests/Golden), whose AnalysisParityTests.swift asserts the same answers from
/// music-core built with NativeAOT; this test asserts it here, for the build the web app runs. Run the tests with
/// MOTIFF_UPDATE_SNAPSHOTS=1 to write it again after a deliberate change in the analysis.
/// </summary>
public class AnalysisParityTests
{
    private const string UpdateVariable = "MOTIFF_UPDATE_SNAPSHOTS";

    /// <summary>The calls, in each language, whose answers the golden keeps; "phrases" with a level does not change the piece.</summary>
    private static readonly (string Call, string? Request)[] s_calls =
    [
        ("phrases", """{"level":"short"}"""),
        ("phrases", """{"level":"phrase"}"""),
        ("phrases", """{"level":"sentence"}"""),
        ("dna", null),
    ];

    private static readonly string[] s_languages = ["en", "ru"];

    private static readonly JsonSerializerOptions s_written = new()
    {
        WriteIndented = true,
        IndentSize = 1,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The golden's name and the piece: the design kit's 97 bars the iPad tests use, and the example both apps open.</summary>
    public static TheoryData<string, string> Pieces => new()
    {
        { "elise", Path.Combine("Golden", "Analysis", "elise.musicxml") },
        { "FurElise", Path.Combine("Import", "MusicXml", "Fixtures", "FurElise.musicxml") },
    };

    /// <summary>What the facade answers today, in the golden's shape.</summary>
    private static JsonObject Answers(string fileName, byte[] data)
    {
        var answers = new JsonArray();
        foreach (string language in s_languages)
        {
            var facade = new MusicCoreFacade();
            JsonObject opened = JsonNode.Parse(facade.Open(fileName, data, $$"""{"language":"{{language}}","id":"p_golden"}"""))!.AsObject();
            Assert.False(opened.ContainsKey("error"), opened.ToJsonString());
            int piece = opened["piece"]!.GetValue<int>();
            foreach ((string call, string? request) in s_calls)
            {
                JsonNode answer = JsonNode.Parse(facade.Call(piece, call, request))!;
                Assert.False(answer.AsObject().ContainsKey("error"), $"{call} {request}: {answer.ToJsonString()}");
                answers.Add(new JsonObject
                {
                    ["language"] = language,
                    ["call"] = call,
                    ["request"] = request is null ? null : JsonNode.Parse(request),
                    ["answer"] = answer,
                });
            }
        }

        return new JsonObject
        {
            ["note"] = "Made on Linux by tests/MotiffLab.MusicCore.Tests/Golden/AnalysisParityTests.cs; asserted under NativeAOT by AnalysisParityTests.swift.",
            ["fixture"] = fileName,
            ["answers"] = answers,
        };
    }

    [Theory]
    [MemberData(nameof(Pieces))]
    public void The_facade_gives_the_phrases_form_and_piece_dna_the_golden_holds(string name, string piece)
    {
        string path = Path.Combine(AppContext.BaseDirectory, piece);
        JsonObject actual = Answers(Path.GetFileName(path), File.ReadAllBytes(path));
        string golden = $"{name}.analysis.json";

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(SourceGoldenPath(golden), actual.ToJsonString(s_written) + "\n");
            return;
        }

        JsonNode expected = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "Analysis", golden)))!;
        JsonArray expectedAnswers = expected["answers"]!.AsArray();
        JsonArray actualAnswers = actual["answers"]!.AsArray();
        Assert.Equal(expected["fixture"]!.GetValue<string>(), actual["fixture"]!.GetValue<string>());
        Assert.Equal(expectedAnswers.Count, actualAnswers.Count);
        for (int i = 0; i < actualAnswers.Count; i++)
        {
            JsonNode entry = actualAnswers[i]!;
            Assert.True(
                JsonNode.DeepEquals(expectedAnswers[i], entry),
                $"{name}: {entry["language"]} {entry["call"]} {entry["request"]?.ToJsonString()} differs from the golden");
        }
    }

    [Fact]
    public void The_answers_do_not_change_between_two_openings()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Golden", "Analysis", "elise.musicxml");
        byte[] data = File.ReadAllBytes(path);

        Assert.True(JsonNode.DeepEquals(Answers("elise.musicxml", data), Answers("elise.musicxml", data)));
    }

    private static string SourceGoldenPath(string name, [CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "..", "ipad", "App", "MotiffLabTests", "Golden", name);
}