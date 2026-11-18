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
public class FacadeAnswersContractTests
{
    private const string UpdateVariable = "MOTIFF_UPDATE_SNAPSHOTS";

    private const string Piece = "FurElise.musicxml";

    /// <summary>The small PDF the requests send as "&lt;pdf&gt;".</summary>
    private const string Pdf = "%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n";

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
        // A bar's notes to correct on the piece opened strictly, as MusicCoreFacadeTests and AssistantFacadeTests correct
        // them: none yet (notes on the beat and off it), a note changed, one taken out and put back by an undo, the first
        // put back as imported; the note editor's draft (a note added and set, one taken out, an undo, a reset, Cancel,
        // and one taken out kept by Save); the assistant's draft accepted and undone whole; and a bar taken from a version,
        // whose notes taken are not corrected here. No layer: none of the layer's fields.
        ["corrections"] =
        [
            ("corrections", """{"bar":2}"""),
            ("corrections.change", """{"bar":2,"id":"n_0010","pitch":1,"onset":0,"length":0,"velocity":0}"""),
            ("corrections.remove", """{"bar":2,"id":"n_0013"}"""),
            ("corrections.undo", """{"bar":2}"""),
            ("corrections.revert", """{"bar":2,"id":"n_0010"}"""),
            ("corrections.begin", """{"bar":2}"""),
            ("corrections.add", """{"bar":2,"onset":1260,"pitch":62,"length":120}"""),
            ("corrections.set", """{"bar":2,"id":"a1","onset":1380,"pitch":62,"length":60}"""),
            ("corrections.remove", """{"bar":2,"id":"n_0011"}"""),
            ("corrections.undo", """{"bar":2}"""),
            ("corrections.reset", """{"bar":2}"""),
            ("corrections.discard", """{"bar":2}"""),
            ("corrections.begin", """{"bar":2}"""),
            ("corrections.remove", """{"bar":2,"id":"n_0012"}"""),
            ("corrections.save", """{"bar":2}"""),
            ("corrections.acceptDraft", """{"bar":2,"tool":"notes.edit","parameters":{"bars":{"from":2,"to":2},"edits":[{"kind":"remove","address":"2.R.1","bar":null,"hand":null,"start":null,"length":null,"pitch":null,"velocity":null}]},"index":0}"""),
            ("corrections.undoDraft", """{"bar":2}"""),
            ("performance.add", """{"name":"Live","midi":"<played>","now":"2026-11-15T13:00:00Z"}"""),
            ("performance.take", """{"id":"perf_1","bars":[5]}"""),
            ("corrections", """{"bar":5}"""),
        ],
        // The corrections laid over the notes one by one (the layer of version 0.5), on a project file built as
        // EditsLayerFacadeTests builds its stale one: a note changed and one taken out that fit, and four that no longer do
        // as the notes are now (one laid on a near note of its pitch, one whose note is gone, one whose note is different
        // now, a note added under an id the notes already have). Then one dropped, the layer off and on again, and the
        // correction laid on a near note put back.
        ["corrections.layer"] =
        [
            ("corrections", """{"bar":2}"""),
            ("corrections.drop", """{"bar":2,"noteIds":["n_9999"]}"""),
            ("corrections.layer", """{"on":false,"bar":2}"""),
            ("corrections", """{"bar":2}"""),
            ("corrections.layer", """{"on":true,"bar":2}"""),
            ("corrections.revert", """{"bar":2,"id":"n_0014"}"""),
        ],
        // A version merged through a draft, as MusicCoreFacadeTests merges one: the example played with three notes more in
        // bar 5, its draft opened keeping mine, bar 5 taken from the version, undone, redone, undecided, reset and taken
        // again; the result heard, applied (the version as compared then) and the merge undone; the draft opened again
        // with the choices applied, and cancelled (none open).
        ["performance.merge"] =
        [
            ("performance.add", """{"name":"Live","midi":"<played>","now":"2026-11-15T13:00:00Z"}"""),
            ("performance.merge.open", """{"id":"perf_1","start":"mine"}"""),
            ("performance.merge.choose", """{"bars":[5],"choice":"version"}"""),
            ("performance.merge.undo", "{}"),
            ("performance.merge.redo", "{}"),
            ("performance.merge.undecide", """{"bars":[5]}"""),
            ("performance.merge.reset", "{}"),
            ("performance.merge.choose", """{"bars":[5],"choice":"version"}"""),
            ("performance.merge.playback", """{"view":"result"}"""),
            ("performance.merge.apply", """{"at":"2026-11-15T14:00:00Z"}"""),
            ("performance.merge.undoApplied", """{"id":"perf_1"}"""),
            ("performance.merge.open", """{"id":"perf_1","start":"saved"}"""),
            ("performance.merge.cancel", "{}"),
        ],
        // Another level shown with nothing selected (the selection null), a passage selected and the level changed under
        // it (the selection moved to the units there), the device's level shown without becoming the piece's; then the
        // phrases with the player's own boundary (a phrase joined with the next) and with it reset.
        ["phrases.level"] =
        [
            ("phrases.level", """{"level":"sentence"}"""),
            ("select", """{"bars":{"first":5,"last":8}}"""),
            ("phrases.level", """{"level":"short"}"""),
            ("phrases.level", """{"level":"phrase","remember":false}"""),
            ("phrases", null),
            ("phrases.edit", """{"joinWithNext":1}"""),
            ("phrases.edit", """{"reset":true}"""),
        ],
        // The sources of a piece that is only its PDF, with a bookmark on its pages: no working notes, and page marks.
        ["sources.layers.pdf"] = [("sources.layers", """{"device":"ipad","here":[]}""")],
        // The piano roll's data (step B7): the notes and bars packed as the roll reads them, with their names spelled for
        // the keys in the piece's language, as the roll asks it while the player chose no naming of their own.
        ["roll"] = [("roll", null)],
    };

    /// <summary>
    /// Calls whose answer the file does not keep, only their request: those that only build a file's state (another
    /// answer's), and `performance.merge.playback`, the playback score that playback.json pins (`Answer.Playback`, the
    /// whole piece's notes).
    /// </summary>
    private static readonly HashSet<string> s_steps = ["performance.add", "performance.take", "performance.merge.playback"];

    /// <summary>
    /// The files whose calls start from a project file and not from the example opened anew: the file the project is made
    /// from (the file's "fixture"), and how it is built.
    /// </summary>
    private static readonly Dictionary<string, (string Fixture, Func<byte[], byte[]> Build)> s_projects = new()
    {
        ["corrections.layer"] = (Piece, WithStaleCorrections),
        ["sources.layers.pdf"] = ("Nocturne.pdf", _ => PdfWithABookmark()),
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
        int piece = fromProject
            ? Opened(facade, "p_answers.motiff.json", project.Build(data), """{"language":"en","edits":"on"}""")
            : Opened(facade, Piece, data, """{"language":"en","id":"p_answers"}""");

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
            ["fixture"] = fromProject ? project.Fixture : Piece,
            ["answers"] = answers,
        };
        return file.ToJsonString(s_written) + "\n";
    }

    /// <summary>The piece's number in the facade, opened from the file.</summary>
    private static int Opened(MusicCoreFacade facade, string fileName, byte[] data, string request)
    {
        JsonElement opened = FacadeCalls.Json(facade.Open(fileName, data, request));
        Assert.False(opened.TryGetProperty("error", out JsonElement error), opened.ToString());
        return opened.GetProperty("piece").GetInt32();
    }

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
    /// in place of a MIDI file of the example played with three notes more in bar 5.
    /// </summary>
    private static string? WithBytes(string? request) => request?
        .Replace("<pdf>", Convert.ToBase64String(Encoding.ASCII.GetBytes(Pdf)), StringComparison.Ordinal)
        .Replace("<otherPdf>", Convert.ToBase64String(Encoding.ASCII.GetBytes("%PDF-1.4\nanother edition\n%%EOF\n")), StringComparison.Ordinal)
        .Replace("<scan>", Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Scan", "Fixtures", "homr-fur-elise-page1.musicxml"))), StringComparison.Ordinal)
        .Replace("<played>", request.Contains("<played>", StringComparison.Ordinal) ? Played() : "", StringComparison.Ordinal);

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