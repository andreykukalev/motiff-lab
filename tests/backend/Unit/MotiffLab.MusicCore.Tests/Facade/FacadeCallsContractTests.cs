using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using MotiffLab.MusicCore.Facade;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>
/// The facade's call names as data (audit C2, blueprint 5.4): tests/contracts/facade-calls.json is exactly the calls
/// <see cref="MusicCoreFacade.Call"/> answers today and where it answers each, so a call added, renamed or removed in C#
/// fails here until the file says so, and tests/shared/facade-calls-contract.test.mjs checks every name the iPad's Swift
/// sends against the same file. Run the tests with MOTIFF_UPDATE_SNAPSHOTS=1 to write the file again after a deliberate
/// change of the calls.
/// </summary>
/// <remarks>
/// The facade dispatches by name in <c>switch</c> expressions spread over many files, and by prefix between them, so the
/// names are not read from the code's shape. Every name the facade could answer is a string literal compiled into
/// music-core; each literal shaped like a call name, and each name the file lists, is sent to the facade on piece 0, on
/// an open piece with notes, on an open PDF piece and on an open piece that is only a recording (version 1.1), and the
/// facade's own answer says whether it knows the call there: it refuses an unknown one with "unknownCall" (or "noPiece"
/// on piece 0 with nothing open) and, on a PDF piece or a recording, with
/// "noNotes" (or "unknownCall", should it hand a name on to a table that answers so). Any other answer, an error about the request included, means the call is dispatched. A call that one probe
/// answers is sent again on a facade just opened, so what an earlier probe changed (corrections turned off, say) never
/// decides it.
/// </remarks>
public class FacadeCallsContractTests
{
    private const string UpdateVariable = "MOTIFF_UPDATE_SNAPSHOTS";

    /// <summary>Where a call is answered, in the file's order: piece 0, an open piece with notes, an open PDF piece, an open recording.</summary>
    private static readonly string[] s_receivers = ["piece0", "piece", "pdf", "recording"];

    private static readonly byte[] s_pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");

    private static readonly Regex s_callShaped = new("^[a-z][A-Za-z0-9_-]*(?:\\.[A-Za-z0-9_-]+)*$", RegexOptions.CultureInvariant);

    [Fact]
    public void The_contract_file_lists_exactly_the_calls_the_facade_answers_and_where()
    {
        string kept = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contracts", "facade-calls.json"));
        IReadOnlyDictionary<string, string[]> listed = Listed(kept);
        SortedDictionary<string, string[]> answered = Answered(Candidates().Union(listed.Keys));

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(SourceContractPath(), Written(answered));
            return;
        }

        string[] differences =
        [
            .. answered.Keys.Except(listed.Keys).Select(name => $"{name}: answered on {string.Join(", ", answered[name])}, not in the file"),
            .. listed.Keys.Except(answered.Keys).Select(name => $"{name}: in the file, answered nowhere"),
            .. answered.Keys.Intersect(listed.Keys).Where(name => !answered[name].SequenceEqual(listed[name]))
                .Select(name => $"{name}: answered on {string.Join(", ", answered[name])}, the file says {string.Join(", ", listed[name])}"),
        ];
        Assert.True(differences.Length == 0, $"tests/contracts/facade-calls.json differs from the facade ({UpdateVariable}=1 writes it again):\n{string.Join("\n", differences)}");
        Assert.Equal(Written(answered), kept);
    }

    [Fact]
    public void A_probe_tells_a_call_from_a_name_the_facade_does_not_know()
    {
        SortedDictionary<string, string[]> answered = Answered(["version", "select", "document", "project.metadata", "sources.layers", "reference.key", "phrases.granularity", "corrections.nothing", "language"]);

        Assert.Equal(["piece0"], answered["version"]);
        Assert.Equal(["piece"], answered["select"]);
        Assert.Equal(["pdf"], answered["document"]);
        Assert.Equal(["piece0", "piece", "pdf", "recording"], answered["project.metadata"]);
        Assert.Equal(["piece", "pdf", "recording"], answered["sources.layers"]);
        Assert.Equal(["piece0", "piece"], answered["reference.key"]);
        Assert.Equal(["piece"], answered["language"]);
        Assert.False(answered.ContainsKey("phrases.granularity"));
        Assert.False(answered.ContainsKey("corrections.nothing"));
    }

    /// <summary>
    /// The string literals compiled into music-core (its assembly's user strings, what every <c>switch</c> arm and
    /// comparison on a name loads) shaped like a call name: words of letters, digits, "_" or "-" joined by dots, the first
    /// starting with a lowercase letter.
    /// </summary>
    private static HashSet<string> Candidates()
    {
        using var assembly = new PEReader(File.OpenRead(typeof(MusicCoreFacade).Assembly.Location));
        MetadataReader metadata = assembly.GetMetadataReader();
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (UserStringHandle handle = MetadataTokens.UserStringHandle(1); !handle.IsNil; handle = metadata.GetNextHandle(handle))
        {
            if (s_callShaped.IsMatch(metadata.GetUserString(handle)))
            {
                names.Add(metadata.GetUserString(handle));
            }
        }

        Assert.True(names.Count > 500, $"only {names.Count} literals read from music-core");
        return names;
    }

    /// <summary>The names the facade answers among <paramref name="names"/>, each with where.</summary>
    private static SortedDictionary<string, string[]> Answered(IEnumerable<string> names)
    {
        (MusicCoreFacade Facade, int Piece)[] shared = [.. s_receivers.Select(Opened)];
        var answered = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            string[] on = [.. s_receivers.Where((receiver, index) => Answers(shared[index], receiver, name) && Answers(Opened(receiver), receiver, name))];
            if (on.Length > 0)
            {
                answered[name] = on;
            }
        }

        return answered;
    }

    /// <summary>A facade with the receiver open: nothing for piece 0, Ode to Joy for a piece with notes, a PDF for a PDF piece, a recording added as it is for a recording.</summary>
    private static (MusicCoreFacade Facade, int Piece) Opened(string receiver)
    {
        var facade = new MusicCoreFacade();
        if (receiver == "piece0")
        {
            return (facade, 0);
        }

        JsonElement opened = FacadeCalls.Json(receiver switch
        {
            "pdf" => facade.Open("Nocturne.pdf", s_pdf, """{"id": "p_pdf", "now": "2026-11-16T12:00:00Z"}"""),
            "recording" => facade.Open("Etude.m4a", [], """{"id": "p_rec", "now": "2026-11-16T12:00:00Z", "recording": {"durationMs": 60000}}"""),
            _ => facade.Open("ode-to-joy.musicxml", FacadeCalls.Fixture("ode-to-joy.musicxml"), """{"id": "p_calls", "now": "2026-11-16T12:00:00Z"}"""),
        });
        Assert.False(opened.TryGetProperty("error", out JsonElement error), $"{receiver}: {error}");
        return (facade, opened.GetProperty("piece").GetInt32());
    }

    /// <summary>Whether the facade knows the call there: any answer but the refusal of an unknown name.</summary>
    private static bool Answers((MusicCoreFacade Facade, int Piece) open, string receiver, string name)
    {
        JsonElement answer = FacadeCalls.Json(open.Facade.Call(open.Piece, name));
        string? error = answer.TryGetProperty("error", out JsonElement code) ? code.GetString() : null;
        return receiver switch
        {
            "piece0" => error is not ("unknownCall" or "noPiece"),
            "pdf" or "recording" => error is not ("noNotes" or "unknownCall"),
            _ => error is not "unknownCall",
        };
    }

    private static Dictionary<string, string[]> Listed(string file) =>
        JsonDocument.Parse(file).RootElement.GetProperty("calls").EnumerateArray().ToDictionary(
            call => call.GetProperty("name").GetString()!,
            call => call.GetProperty("on").EnumerateArray().Select(on => on.GetString()!).ToArray(),
            StringComparer.Ordinal);

    /// <summary>The file: a note and one call to a line, by name.</summary>
    private static string Written(SortedDictionary<string, string[]> answered)
    {
        var text = new StringBuilder();
        text.Append("{\n");
        text.Append("  \"note\": \"The calls MusicCoreFacade.Call answers by name (ml_call on the iPad), and where: piece0 (piece 0, nothing open), piece (an open piece with notes), pdf (an open PDF piece), recording (an open piece that is only a recording). ")
            .Append("ml_open and ml_close are not calls by name. Written and checked by tests/backend/Unit/MotiffLab.MusicCore.Tests/Facade/FacadeCallsContractTests.cs (")
            .Append(UpdateVariable)
            .Append("=1 writes it); every name the Swift sends is checked against it by tests/shared/facade-calls-contract.test.mjs.\",\n");
        text.Append("  \"calls\": [\n");
        text.AppendJoin(",\n", answered.Select(call => $"    {{ \"name\": \"{call.Key}\", \"on\": [{string.Join(", ", call.Value.Select(on => $"\"{on}\""))}] }}"));
        text.Append("\n  ]\n}\n");
        return text.ToString();
    }

    private static string SourceContractPath([CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "..", "..", "contracts", "facade-calls.json");
}