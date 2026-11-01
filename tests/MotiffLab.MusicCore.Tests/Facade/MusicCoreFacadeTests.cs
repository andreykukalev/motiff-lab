using System.Text.Json;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>The one JSON contract the web app and the iPad app call music-core through (0020).</summary>
public class MusicCoreFacadeTests
{
    private const string FurElise = "fur-elise-original.musicxml";
    private const string OdeToJoy = "ode-to-joy.musicxml";

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", name));

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    /// <summary>Für Elise open as piece 1, in English unless asked otherwise.</summary>
    private static (MusicCoreFacade Facade, JsonElement Opened) Open(string? request = null)
    {
        var facade = new MusicCoreFacade();
        JsonElement opened = Json(facade.Open(FurElise, Fixture(FurElise), request));
        Assert.False(opened.TryGetProperty("error", out JsonElement error), error.ToString());
        return (facade, opened);
    }

    private static JsonElement Call(MusicCoreFacade facade, string name, string? request = null)
    {
        JsonElement answer = Json(facade.Call(1, name, request));
        Assert.False(answer.TryGetProperty("error", out JsonElement error), $"{name}: {error} {answer}");
        return answer;
    }

    private static string Error(MusicCoreFacade facade, int piece, string name, string? request = null)
    {
        JsonElement answer = Json(facade.Call(piece, name, request));
        return answer.TryGetProperty("error", out JsonElement error) ? error.GetString()! : throw new Xunit.Sdk.XunitException($"{name} answered without an error: {answer}");
    }

    [Fact]
    public void VersionNamesTheContractAndTheProjectFile()
    {
        JsonElement version = Json(new MusicCoreFacade().Call(0, "version"));

        Assert.Equal(MusicCoreFacade.Contract, version.GetProperty("contract").GetInt32());
        Assert.Equal(ProjectFile.Version, version.GetProperty("projectFile").GetInt32());
        Assert.Contains(".NET", version.GetProperty("runtime").GetString());
    }

    [Fact]
    public void OpensAFileAsANewProjectWithItsSummary()
    {
        (MusicCoreFacade facade, JsonElement opened) = Open("""{"id": "p_fur", "now": "2026-11-01T10:00:00Z"}""");

        Assert.Equal(1, opened.GetProperty("piece").GetInt32());
        Assert.Equal(1, facade.Count);
        JsonElement project = opened.GetProperty("project");
        Assert.Equal("p_fur", project.GetProperty("id").GetString());
        Assert.Equal("imported", project.GetProperty("origin").GetString());
        Assert.Equal("2026-11-01T10:00:00.0000000+00:00", project.GetProperty("createdAt").GetString());
        JsonElement summary = opened.GetProperty("summary");
        Assert.Equal("musicXml", summary.GetProperty("format").GetString());
        Assert.True(summary.GetProperty("noteCount").GetInt32() > 0);
        Assert.True(summary.GetProperty("barCount").GetInt32() > 0);
        Assert.False(summary.TryGetProperty("notes", out _), "the summary has no notes; the piece call has them");
        Assert.Equal(JsonValueKind.Null, opened.GetProperty("selection").ValueKind);
        Assert.Equal("en", opened.GetProperty("language").GetString());
    }

    [Fact]
    public void ARejectedFileAnswersWithTheImportersReason()
    {
        JsonElement answer = Json(new MusicCoreFacade().Open("notes.mid", [1, 2, 3, 4]));

        Assert.Equal("import", answer.GetProperty("error").GetString());
        Assert.False(string.IsNullOrEmpty(answer.GetProperty("reason").GetString()));
        Assert.False(string.IsNullOrEmpty(answer.GetProperty("message").GetString()));
    }

    [Fact]
    public void ThePieceHasNotesBarsKeysAndTracks()
    {
        (MusicCoreFacade facade, JsonElement opened) = Open();

        JsonElement piece = Call(facade, "piece");

        JsonElement[] notes = [.. piece.GetProperty("notes").EnumerateArray()];
        Assert.Equal(opened.GetProperty("summary").GetProperty("noteCount").GetInt32(), notes.Length);
        JsonElement first = notes[0];
        foreach (string field in new[] { "id", "pitch", "onset", "duration", "velocity", "track", "provenance" })
        {
            Assert.True(first.TryGetProperty(field, out _), field);
        }

        Assert.Equal("imported", first.GetProperty("provenance").GetString());
        Assert.Equal(opened.GetProperty("summary").GetProperty("barCount").GetInt32(), piece.GetProperty("bars").EnumerateArray().Count(bar => !bar.GetProperty("pickup").GetBoolean()));
        Assert.NotEmpty(piece.GetProperty("keys").EnumerateArray());
        Assert.NotEmpty(piece.GetProperty("tracks").EnumerateArray());
        Assert.Equal("Whole piece", piece.GetProperty("startChoices")[0].GetProperty("label").GetString());
    }

    [Fact]
    public void PhrasesNameTheLevelsAndCanBeEdited()
    {
        (MusicCoreFacade facade, _) = Open();

        JsonElement phrases = Call(facade, "phrases");

        Assert.Equal(["phrase", "sentence", "period"], phrases.GetProperty("levels").EnumerateArray().Select(level => level.GetProperty("id").GetString()!).ToArray());
        Assert.True(phrases.GetProperty("levels")[0].GetProperty("available").GetBoolean());
        Assert.False(phrases.GetProperty("levels")[1].GetProperty("available").GetBoolean());
        JsonElement[] found = [.. phrases.GetProperty("phrases").EnumerateArray()];
        Assert.True(found.Length > 1);
        Assert.Equal("phrase", found[0].GetProperty("level").GetString());
        Assert.True(found[0].TryGetProperty("selectable", out _));

        JsonElement joined = Call(facade, "phrases.edit", $$$"""{"joinWithNext": {{{found[0].GetProperty("number").GetInt32()}}}}""");
        Assert.Equal(found.Length - 1, joined.GetProperty("phrases").GetArrayLength());
        Assert.NotEmpty(joined.GetProperty("edits").GetProperty("removed").EnumerateArray());

        JsonElement reset = Call(facade, "phrases.edit", """{"reset": true}""");
        Assert.Equal(found.Length, reset.GetProperty("phrases").GetArrayLength());
    }

    [Fact]
    public void SelectingBarsGivesTheSelectionAndItsKey()
    {
        (MusicCoreFacade facade, _) = Open();

        JsonElement answer = Call(facade, "select", """{"bars": {"first": 1, "last": 2}, "filter": {"hand": "right"}}""");

        JsonElement selection = answer.GetProperty("selection");
        Assert.Equal(1, selection.GetProperty("firstBar").GetInt32());
        Assert.Equal(2, selection.GetProperty("lastBar").GetInt32());
        Assert.Equal("right", selection.GetProperty("filter").GetProperty("hand").GetString());
        Assert.NotEmpty(selection.GetProperty("noteIds").EnumerateArray());
        Assert.NotEqual(JsonValueKind.Null, selection.GetProperty("keySignature").ValueKind);
    }

    [Fact]
    public void UnderstandGivesFactsInTheLanguageOfThePiece()
    {
        (MusicCoreFacade facade, _) = Open("""{"language": "ru"}""");
        Call(facade, "select", """{"bars": {"first": 1, "last": 2}}""");

        JsonElement understood = Call(facade, "understand");

        JsonElement motif = understood.GetProperty("motif");
        Assert.NotEmpty(motif.GetProperty("line").EnumerateArray());
        Assert.NotEmpty(motif.GetProperty("contour").EnumerateArray());
        JsonElement[] facts = [.. motif.GetProperty("facts").EnumerateArray()];
        Assert.NotEmpty(facts);
        Assert.Contains(facts, fact => fact.GetProperty("text").GetString()!.Any(c => c >= 'А' && c <= 'я'));
        foreach (string field in new[] { "id", "lens", "certainty", "text", "basis", "places" })
        {
            Assert.True(facts[0].TryGetProperty(field, out _), field);
        }

        Assert.True(understood.GetProperty("structure").TryGetProperty("recurrences", out _));
        Assert.True(understood.TryGetProperty("character", out _));

        Call(facade, "language", """{"language": "en"}""");
        JsonElement english = Call(facade, "understand");
        Assert.DoesNotContain(english.GetProperty("motif").GetProperty("facts").EnumerateArray(), fact => fact.GetProperty("text").GetString()!.Any(c => c >= 'А' && c <= 'я'));
    }

    [Fact]
    public void CallsThatNeedASelectionSaySo()
    {
        (MusicCoreFacade facade, _) = Open();

        Assert.Equal("noSelection", Error(facade, 1, "understand"));
        Assert.Equal("noSelection", Error(facade, 1, "experiments"));
        Assert.Equal("noVariation", Error(facade, 1, "variation"));
        Assert.Equal("noPiece", Error(facade, 2, "piece"));
        Assert.Equal("unknownCall", Error(facade, 1, "dance"));
        Assert.Equal("badRequest", Error(facade, 1, "select", """{"bars": {"first": "one"}}"""));
        Assert.Equal("badRequest", Error(facade, 1, "select", "not json"));
    }

    [Fact]
    public void ExperimentsAreOfferedAndStartAVariation()
    {
        (MusicCoreFacade facade, _) = Open();
        Call(facade, "select", """{"bars": {"first": 1, "last": 2}}""");

        JsonElement offered = Call(facade, "experiments");
        JsonElement[] experiments = [.. offered.GetProperty("experiments").EnumerateArray()];
        Assert.NotEmpty(experiments);
        JsonElement experiment = experiments[0];
        foreach (string field in new[] { "id", "topic", "title", "change", "question", "listeningFocus", "factIds", "passage", "keepPreset", "alternatives" })
        {
            Assert.True(experiment.TryGetProperty(field, out _), field);
        }

        JsonElement step = experiment.GetProperty("alternatives")[0].GetProperty("steps")[0];
        Assert.True(step.TryGetProperty("operation", out _));
        Assert.True(step.TryGetProperty("parameters", out _));

        JsonElement started = Call(facade, "experiments.start", $$$"""{"id": "{{{experiment.GetProperty("id").GetString()}}}", "alternative": 0}""");
        Assert.Equal(experiment.GetProperty("id").GetString(), started.GetProperty("experiment").GetProperty("id").GetString());
        Assert.NotEqual(JsonValueKind.Null, started.GetProperty("result").ValueKind);
        Assert.NotEmpty(started.GetProperty("heard").EnumerateArray());
        Assert.Equal("badRequest", Error(facade, 1, "experiments.start", """{"id": "nothing.like.it"}"""));
    }

    [Fact]
    public void SurprisePrefersExperimentsNotYetTried()
    {
        (MusicCoreFacade facade, _) = Open();
        Call(facade, "select", """{"bars": {"first": 1, "last": 2}}""");
        int offered = Call(facade, "experiments").GetProperty("experiments").GetArrayLength();
        var tried = new List<string>();

        for (int seed = 0; seed < offered; seed++)
        {
            string triedJson = string.Join(", ", tried.Select(id => $"\"{id}\""));
            JsonElement answer = Call(facade, "experiments.surprise", $$$"""{"seed": {{{seed}}}, "tried": [{{{triedJson}}}]}""");
            tried.Add(answer.GetProperty("experiment").GetProperty("id").GetString()!);
            Assert.NotEqual(JsonValueKind.Null, answer.GetProperty("result").ValueKind);
        }

        Assert.Equal(offered, tried.Distinct().Count());
    }

    [Fact]
    public void TheVariationChainTakesStepsInTheProjectFilesShape()
    {
        (MusicCoreFacade facade, _) = Open();
        Call(facade, "select", """{"bars": {"first": 1, "last": 2}}""");

        JsonElement started = Call(facade, "variation.start", """{"keep": ["pitches", "pitchOrder"], "seed": 3}""");
        Assert.Equal(["pitchOrder", "pitches"], started.GetProperty("keep").EnumerateArray().Select(keep => keep.GetString()!).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(3, started.GetProperty("seed").GetInt32());
        Assert.Equal(JsonValueKind.Null, started.GetProperty("result").ValueKind);
        Assert.Contains(started.GetProperty("operations").EnumerateArray(), operation => operation.GetProperty("id").GetString() == "V1");

        Assert.Equal(JsonValueKind.Null, Call(facade, "variation.check", """{"operation": "V1", "parameters": {"kind": "HalfTime", "eighths": 1}}""").GetProperty("reason").ValueKind);

        JsonElement proposed = Call(facade, "variation.propose", """{"operation": "V1", "parameters": {"kind": "HalfTime", "eighths": 1}}""");
        JsonElement pending = proposed.GetProperty("pending");
        Assert.Equal("V1", pending.GetProperty("operation").GetString());
        Assert.Equal("HalfTime", pending.GetProperty("parameters").GetProperty("kind").GetString());
        Assert.False(string.IsNullOrEmpty(pending.GetProperty("explanation").GetString()));
        JsonElement result = proposed.GetProperty("result");
        Assert.Equal("V1", result.GetProperty("operation").GetString());
        Assert.NotEmpty(result.GetProperty("changes").EnumerateArray());
        Assert.NotEmpty(result.GetProperty("checks").EnumerateArray());
        Assert.Empty(proposed.GetProperty("steps").EnumerateArray());

        JsonElement accepted = Call(facade, "variation.accept");
        Assert.Single(accepted.GetProperty("steps").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, accepted.GetProperty("pending").ValueKind);
        Assert.True(accepted.GetProperty("canUndo").GetBoolean());

        JsonElement undone = Call(facade, "variation.undo");
        Assert.Empty(undone.GetProperty("steps").EnumerateArray());
        Assert.True(undone.GetProperty("canRedo").GetBoolean());
        Assert.Single(Call(facade, "variation.redo").GetProperty("steps").EnumerateArray());

        Assert.Equal("badRequest", Error(facade, 1, "variation.propose", """{"operation": "V99", "parameters": {}}"""));
        Assert.Equal("badRequest", Error(facade, 1, "variation.propose", """{"operation": "V1", "parameters": {"kind": "HalfTime", "eighths": 99}}"""));
        Assert.Equal("unavailable", Error(facade, 1, "variation.accept"));
        Assert.True(Call(facade, "variation.clear").GetProperty("cleared").GetBoolean());
        Assert.Equal("noVariation", Error(facade, 1, "variation"));
    }

    [Fact]
    public void EditingByHandIsAStepOfOperationE7()
    {
        (MusicCoreFacade facade, _) = Open();
        JsonElement selection = Call(facade, "select", """{"bars": {"first": 1, "last": 2}}""").GetProperty("selection");
        string noteId = selection.GetProperty("noteIds")[0].GetString()!;

        JsonElement edited = Call(facade, "variation.propose", $$$"""{"operation": "E7", "parameters": {"kind": "Remove", "noteId": "{{{noteId}}}"}}""");

        Assert.Equal("E7", edited.GetProperty("steps")[0].GetProperty("operation").GetString());
        Assert.Contains(edited.GetProperty("result").GetProperty("removed").EnumerateArray(), note => note.GetProperty("id").GetString() == noteId);
    }

    [Fact]
    public void NotationIsMeiForThePieceAndForTheVariationInPlace()
    {
        (MusicCoreFacade facade, _) = Open();

        JsonElement piece = Call(facade, "notation", """{"title": "Für Elise"}""");
        Assert.Contains("<mei", piece.GetProperty("mei").GetString());
        Assert.Contains("Für Elise", piece.GetProperty("mei").GetString());
        Assert.NotEmpty(piece.GetProperty("staves").EnumerateArray());
        Assert.Equal("noVariation", Error(facade, 1, "notation", """{"of": "variation"}"""));

        Call(facade, "select", """{"bars": {"first": 1, "last": 2}}""");
        Call(facade, "variation.propose", """{"operation": "V1", "parameters": {"kind": "HalfTime", "eighths": 1}}""");
        JsonElement variation = Call(facade, "notation", """{"of": "variation"}""");
        Assert.Contains("<mei", variation.GetProperty("mei").GetString());
        Assert.NotEqual(piece.GetProperty("mei").GetString(), variation.GetProperty("mei").GetString());
        Assert.Equal("badRequest", Error(facade, 1, "notation", """{"of": "moon"}"""));
    }

    [Fact]
    public void PlaybackGivesTheScoreOfThePieceAndOfWhatIsHeard()
    {
        (MusicCoreFacade facade, JsonElement opened) = Open();

        JsonElement piece = Call(facade, "playback");
        Assert.Equal(opened.GetProperty("summary").GetProperty("noteCount").GetInt32(), piece.GetProperty("notes").GetArrayLength());
        Assert.NotEmpty(piece.GetProperty("tempos").EnumerateArray());
        Assert.NotEmpty(piece.GetProperty("beats").EnumerateArray());
        Assert.NotEmpty(piece.GetProperty("bars").EnumerateArray());

        Call(facade, "select", """{"bars": {"first": 1, "last": 2}}""");
        Call(facade, "variation.propose", """{"operation": "V1", "parameters": {"kind": "HalfTime", "eighths": 1}}""");
        JsonElement heard = Call(facade, "playback", """{"of": "heard"}""");
        Assert.True(heard.GetProperty("notes").GetArrayLength() < piece.GetProperty("notes").GetArrayLength());
        JsonElement inPlace = Call(facade, "playback", """{"of": "inPlace"}""");
        Assert.Equal(piece.GetProperty("end").GetInt64(), inPlace.GetProperty("end").GetInt64());
    }

    [Fact]
    public void SavingGivesTheProjectFileAndOpeningItRestoresTheWork()
    {
        (MusicCoreFacade facade, _) = Open("""{"id": "p_fur"}""");
        Call(facade, "select", """{"bars": {"first": 1, "last": 2}, "filter": {"hand": "right"}}""");
        Call(facade, "variation.propose", """{"operation": "V1", "parameters": {"kind": "HalfTime", "eighths": 1}}""");
        Call(facade, "variation.accept");
        JsonElement firstPhrase = Call(facade, "phrases").GetProperty("phrases")[0];
        Call(facade, "phrases.edit", $$$"""{"joinWithNext": {{{firstPhrase.GetProperty("number").GetInt32()}}}}""");

        string saved = facade.Call(1, "project.save", """{"now": "2026-11-01T12:00:00Z"}""");
        JsonElement file = Json(saved);
        Assert.Equal(ProjectFile.FormatName, file.GetProperty("format").GetString());
        Assert.Equal(ProjectFile.Version, file.GetProperty("version").GetInt32());
        Assert.Equal("p_fur", file.GetProperty("id").GetString());
        Assert.Equal("m_0001", file.GetProperty("motifs")[0].GetProperty("id").GetString());
        Assert.Equal("v_0001", file.GetProperty("variations")[0].GetProperty("id").GetString());

        var again = new MusicCoreFacade();
        JsonElement reopened = Json(again.Open("fur-elise" + ProjectFile.Extension, System.Text.Encoding.UTF8.GetBytes(saved)));
        Assert.False(reopened.TryGetProperty("error", out JsonElement error), error.ToString());
        Assert.Equal("p_fur", reopened.GetProperty("project").GetProperty("id").GetString());
        Assert.Equal("right", reopened.GetProperty("selection").GetProperty("filter").GetProperty("hand").GetString());
        Assert.True(reopened.GetProperty("hasVariation").GetBoolean());
        JsonElement chain = Call(again, "variation");
        Assert.Single(chain.GetProperty("steps").EnumerateArray());
        Assert.Equal("V1", chain.GetProperty("steps")[0].GetProperty("operation").GetString());
        Assert.NotEmpty(Call(again, "phrases").GetProperty("edits").GetProperty("removed").EnumerateArray());

        // Saved again without a change, the file is the same bytes.
        Assert.Equal(saved, again.Call(1, "project.save", """{"now": "2026-11-01T12:00:00Z"}"""));
    }

    [Fact]
    public void TheCatalogueNamesKeepAndTheOperations()
    {
        var facade = new MusicCoreFacade();

        JsonElement english = Json(facade.Call(0, "catalogue"));
        JsonElement russian = Json(facade.Call(0, "catalogue", """{"language": "ru"}"""));

        Assert.Contains(english.GetProperty("keep").GetProperty("properties").EnumerateArray(), property => property.GetProperty("id").GetString() == "pitchOrder");
        Assert.NotEmpty(english.GetProperty("keep").GetProperty("presets").EnumerateArray());
        JsonElement[] operations = [.. english.GetProperty("operations").EnumerateArray()];
        Assert.Contains(operations, operation => operation.GetProperty("id").GetString() == "V1");
        Assert.Contains(operations, operation => operation.GetProperty("id").GetString() == "E7");
        Assert.Contains(operations[0].GetProperty("scopes").EnumerateArray(), scope => scope.GetString() == "all");
        Assert.NotEqual(operations[0].GetProperty("name").GetString(), russian.GetProperty("operations")[0].GetProperty("name").GetString());
    }

    [Fact]
    public void PieceDnaIsAPlaceholder()
    {
        (MusicCoreFacade facade, _) = Open();

        Assert.Equal("notYet", Call(facade, "dna").GetProperty("status").GetString());
    }

    [Fact]
    public void TheSameCallsGiveTheSameAnswers()
    {
        static List<string> Run()
        {
            var facade = new MusicCoreFacade();
            var answers = new List<string> { facade.Open(OdeToJoy, Fixture(OdeToJoy), """{"id": "p_ode", "now": "2026-11-01T10:00:00Z"}""") };
            foreach ((string name, string? request) in new (string, string?)[]
            {
                ("phrases", null), ("select", """{"bars": {"first": 1, "last": 4}}"""), ("understand", null), ("experiments", null),
                ("variation.start", """{"keep": ["pitches"], "seed": 7}"""), ("variation.propose", """{"operation": "V1", "parameters": {"kind": "HalfTime", "eighths": 1}}"""),
                ("notation", """{"of": "variation"}"""), ("playback", """{"of": "inPlace"}"""), ("project.save", """{"now": "2026-11-01T12:00:00Z"}"""),
            })
            {
                answers.Add(facade.Call(1, name, request));
            }

            return answers;
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void ClosingForgetsThePiece()
    {
        (MusicCoreFacade facade, _) = Open();

        Assert.True(facade.Close(1));
        Assert.False(facade.Close(1));
        Assert.Equal(0, facade.Count);
        Assert.Equal("noPiece", Error(facade, 1, "piece"));
    }
}