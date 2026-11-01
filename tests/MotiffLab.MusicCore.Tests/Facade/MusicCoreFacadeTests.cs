using System.Text.Json;
using System.Text.RegularExpressions;

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

        Assert.Equal("phrase", phrases.GetProperty("level").GetString());
        Assert.Equal(["short", "phrase", "sentence"], phrases.GetProperty("levels").EnumerateArray().Select(level => level.GetProperty("id").GetString()!).ToArray());
        Assert.All(phrases.GetProperty("levels").EnumerateArray(), level => Assert.True(level.GetProperty("available").GetBoolean()));
        JsonElement[] found = [.. phrases.GetProperty("units").EnumerateArray()];
        Assert.True(found.Length > 1);
        Assert.Equal(found.Length, phrases.GetProperty("levels")[1].GetProperty("count").GetInt32());
        Assert.Equal("phrase", found[0].GetProperty("level").GetString());
        foreach (string field in new[] { "number", "firstBar", "lastBar", "onset", "end", "selectable", "long", "parent", "start" })
        {
            Assert.True(found[0].TryGetProperty(field, out _), field);
        }

        Assert.Equal("the start of the piece", found[0].GetProperty("start").GetProperty("description").GetString());
        Assert.Equal(1, found[0].GetProperty("parent").GetInt32());

        JsonElement joined = Call(facade, "phrases.edit", $$$"""{"joinWithNext": {{{found[0].GetProperty("number").GetInt32()}}}}""");
        Assert.Equal(found.Length - 1, joined.GetProperty("units").GetArrayLength());
        Assert.NotEmpty(joined.GetProperty("edits").GetProperty("phrase").GetProperty("removed").EnumerateArray());

        JsonElement reset = Call(facade, "phrases.edit", """{"reset": true}""");
        Assert.Equal(found.Length, reset.GetProperty("units").GetArrayLength());
        Assert.Equal("badRequest", Error(facade, 1, "phrases.edit", """{"level": "period", "reset": true}"""));
    }

    [Fact]
    public void TheLevelIsRememberedWithThePieceAndMovesTheSelection()
    {
        (MusicCoreFacade facade, _) = Open();
        JsonElement shorts = Call(facade, "phrases", """{"level": "short"}""");
        JsonElement sentences = Call(facade, "phrases", """{"level": "sentence"}""");
        int phrases = Call(facade, "phrases").GetProperty("units").GetArrayLength();
        Assert.True(shorts.GetProperty("units").GetArrayLength() >= phrases);
        Assert.True(sentences.GetProperty("units").GetArrayLength() <= phrases);
        Assert.All(shorts.GetProperty("units").EnumerateArray(), unit => Assert.Equal("short", unit.GetProperty("level").GetString()));
        // Asking for a level does not change the one shown.
        Assert.Equal("phrase", Call(facade, "phrases").GetProperty("level").GetString());

        JsonElement firstPhrase = Call(facade, "phrases").GetProperty("units")[0];
        Call(facade, "select", $$$"""{"span": {"onset": {{{firstPhrase.GetProperty("onset").GetInt64()}}}, "end": {{{firstPhrase.GetProperty("end").GetInt64()}}}}}""");

        JsonElement changed = Call(facade, "phrases.level", """{"level": "sentence"}""");
        Assert.Equal("sentence", changed.GetProperty("level").GetString());
        JsonElement sentence = changed.GetProperty("units")[0];
        Assert.Equal(sentence.GetProperty("end").GetInt64(), changed.GetProperty("selection").GetProperty("end").GetInt64());
        Assert.Equal("sentences", Call(facade, "settings").GetProperty("overrides").GetProperty("phrases.granularity").GetString());

        JsonElement back = Call(facade, "phrases.level", """{"level": "short"}""");
        Assert.Equal(back.GetProperty("units")[0].GetProperty("end").GetInt64(), back.GetProperty("selection").GetProperty("end").GetInt64());

        // The level comes back with the project file.
        string saved = facade.Call(1, "project.save", """{"now": "2026-11-01T12:00:00Z"}""");
        var again = new MusicCoreFacade();
        again.Open("fur-elise" + ProjectFile.Extension, System.Text.Encoding.UTF8.GetBytes(saved));
        Assert.Equal("short", Call(again, "phrases").GetProperty("level").GetString());
    }

    [Fact]
    public void EditsAtOneLevelKeepTheLevelsNested()
    {
        (MusicCoreFacade facade, _) = Open();
        JsonElement[] shorts = [.. Call(facade, "phrases", """{"level": "short"}""").GetProperty("units").EnumerateArray()];
        JsonElement[] phrases = [.. Call(facade, "phrases").GetProperty("units").EnumerateArray()];
        // A short unit that ends where a phrase ends: joining it with the next joins the phrases too.
        JsonElement last = shorts.First(unit => unit.GetProperty("end").GetInt64() == phrases[0].GetProperty("end").GetInt64());

        JsonElement joined = Call(facade, "phrases.edit", $$$"""{"level": "short", "joinWithNext": {{{last.GetProperty("number").GetInt32()}}}}""");

        Assert.Equal(phrases.Length - 1, joined.GetProperty("units").GetArrayLength());
        Assert.NotEmpty(joined.GetProperty("edits").GetProperty("phrase").GetProperty("removed").EnumerateArray());
        Assert.Empty(joined.GetProperty("edits").GetProperty("short").GetProperty("removed").EnumerateArray());

        // Splitting a sentence puts a phrase boundary there as well.
        Call(facade, "phrases.edit", """{"reset": true}""");
        long inside = phrases[0].GetProperty("onset").GetInt64() + ((phrases[0].GetProperty("end").GetInt64() - phrases[0].GetProperty("onset").GetInt64()) / 2);
        JsonElement split = Call(facade, "phrases.edit", $$$"""{"level": "sentence", "split": {{{inside}}}}""");
        Assert.NotEmpty(split.GetProperty("edits").GetProperty("phrase").GetProperty("added").EnumerateArray());
        Assert.NotEmpty(split.GetProperty("edits").GetProperty("sentence").GetProperty("added").EnumerateArray());
        Assert.Equal(phrases.Length + 1, split.GetProperty("units").GetArrayLength());
    }

    [Fact]
    public void UnderstandRanksTheFactsAndOffersTryChipsOnlyForBuiltOperations()
    {
        (MusicCoreFacade facade, _) = Open();
        Call(facade, "select", """{"bars": {"first": 1, "last": 2}}""");

        JsonElement understood = Call(facade, "understand");

        string[] strongest = [.. understood.GetProperty("strongest").EnumerateArray().Select(id => id.GetString()!)];
        Assert.InRange(strongest.Length, 1, 3);
        Assert.Equal(strongest[0], understood.GetProperty("keyObservation").GetProperty("id").GetString());
        Assert.False(string.IsNullOrEmpty(understood.GetProperty("keyObservation").GetProperty("reason").GetString()));
        Assert.True(understood.TryGetProperty("sentence", out _));

        JsonElement[] facts = [.. understood.GetProperty("motif").GetProperty("facts").EnumerateArray().Concat(understood.GetProperty("character").EnumerateArray())];
        JsonElement[] chips = [.. facts.SelectMany(fact => fact.GetProperty("tryChips").EnumerateArray())];
        Assert.NotEmpty(chips);
        foreach (JsonElement chip in chips)
        {
            Assert.NotNull(MotiffLab.MusicCore.Variations.VariationOperations.Find(chip.GetProperty("operation").GetString()!));
            Assert.True(chip.TryGetProperty("parameters", out _));
            Assert.False(string.IsNullOrEmpty(chip.GetProperty("label").GetString()));
        }

        JsonElement tried = Call(facade, "try", $$$"""{"chip": "{{{chips[0].GetProperty("id").GetString()}}}"}""");
        Assert.Equal(chips[0].GetProperty("id").GetString(), tried.GetProperty("chip").GetString());
        Assert.Single(tried.GetProperty("steps").EnumerateArray());
        Assert.NotEqual(JsonValueKind.Null, tried.GetProperty("result").ValueKind);
        Assert.Equal("badRequest", Error(facade, 1, "try", """{"chip": "v99.nothing"}"""));
        Assert.Equal("unavailable", Error(facade, 1, "try", """{"chip": "v7.air"}"""));
    }

    [Fact]
    public void UnderstandGivesTheChordsAndHarmonyFactsWithTryChips()
    {
        (MusicCoreFacade facade, _) = Open();
        Call(facade, "select", """{"bars": {"first": 1, "last": 8}}""");

        JsonElement harmony = Call(facade, "understand").GetProperty("harmony");

        JsonElement[] chords = [.. harmony.GetProperty("chords").EnumerateArray()];
        Assert.Equal(["Am", "E", "Am", "Am", "E", "Am"], chords.Select(chord => chord.GetProperty("symbol").GetString()));
        Assert.All(chords, chord => Assert.NotEmpty(chord.GetProperty("noteIds").EnumerateArray()));
        Assert.Equal(9, chords[0].GetProperty("root").GetInt32());
        Assert.Equal("minor", chords[0].GetProperty("quality").GetString());
        Assert.NotEmpty(harmony.GetProperty("bass").EnumerateArray());

        JsonElement[] facts = [.. harmony.GetProperty("facts").EnumerateArray()];
        Assert.All(facts, fact => Assert.Equal("harmony", fact.GetProperty("lens").GetString()));
        JsonElement bass = facts.Single(fact => fact.GetProperty("id").GetString() == "harmony.bass");
        string[] offered = [.. bass.GetProperty("tryChips").EnumerateArray().Select(chip => chip.GetProperty("id").GetString()!)];
        Assert.Contains("v12.pedal", offered);

        JsonElement tried = Call(facade, "try", """{"chip": "v12.pedal"}""");
        Assert.Equal("V12", tried.GetProperty("steps")[0].GetProperty("operation").GetString());
        Assert.NotEqual(JsonValueKind.Null, tried.GetProperty("result").ValueKind);

        Call(facade, "language", """{"language": "ru"}""");
        JsonElement russian = Call(facade, "understand").GetProperty("harmony");
        Assert.All(russian.GetProperty("facts").EnumerateArray(), fact => Assert.Contains(fact.GetProperty("text").GetString()!, c => c >= 'А' && c <= 'я'));
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
    public void NotationEngravesAnExcerptWithTheVariationOrAnIdeaAsAnOssiaAbove()
    {
        (MusicCoreFacade facade, _) = Open();

        JsonElement excerpt = Call(facade, "notation", """{"bars": {"first": 3, "last": 4}}""");
        Assert.Equal(3, excerpt.GetProperty("excerpt").GetProperty("firstBar").GetInt32());
        Assert.Equal(4, excerpt.GetProperty("excerpt").GetProperty("lastBar").GetInt32());
        Assert.Equal(2, Regex.Count(excerpt.GetProperty("mei").GetString()!, "<measure "));
        Assert.Empty(excerpt.GetProperty("ossias").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, Call(facade, "notation").GetProperty("excerpt").ValueKind);
        Assert.Equal("badRequest", Error(facade, 1, "notation", """{"bars": {"first": 4, "last": 3}}"""));
        Assert.Equal("badRequest", Error(facade, 1, "notation", """{"bars": {"first": 1, "last": 999}}"""));
        Assert.Equal("noSelection", Error(facade, 1, "notation", """{"bars": "selection"}"""));
        Assert.Equal("unavailable", Error(facade, 1, "notation", """{"ossia": "asPlayed"}"""));

        Call(facade, "select", """{"bars": {"first": 1, "last": 2}}""");
        Assert.Equal("noVariation", Error(facade, 1, "notation", """{"bars": "selection", "ossia": "variation"}"""));
        Call(facade, "variation.propose", """{"operation": "V1", "parameters": {"kind": "HalfTime", "eighths": 1}}""");
        JsonElement withOssia = Call(facade, "notation", """{"bars": "selection", "ossia": "variation"}""");
        string mei = withOssia.GetProperty("mei").GetString()!;
        JsonElement ossia = Assert.Single(withOssia.GetProperty("ossias").EnumerateArray());
        Assert.Equal("Variation", ossia.GetProperty("label").GetString());
        Assert.Equal("ossia-", ossia.GetProperty("idPrefix").GetString());
        Assert.True(ossia.GetProperty("noteCount").GetInt32() > 0);
        Assert.Contains("<label>Variation</label>", mei);
        Assert.Contains("xml:id=\"ossia-", mei);
        int ossiaStaves = ossia.GetProperty("staves").GetArrayLength();
        Assert.Equal(ossiaStaves + 1, withOssia.GetProperty("staves")[0].GetProperty("number").GetInt32());

        Call(facade, "variation.accept");
        string idea = Call(facade, "idea.keep", """{"name": "Half time", "now": "2026-11-01T10:00:00Z"}""").GetProperty("ideas")[0].GetProperty("id").GetString()!;
        JsonElement compared = Call(facade, "notation", $$"""{"bars": {"first": 1, "last": 2}, "ossia": ["variation", "{{idea}}"]}""");
        Assert.Equal(["Variation", "Half time"], compared.GetProperty("ossias").EnumerateArray().Select(item => item.GetProperty("label").GetString()));
        Assert.Equal("badRequest", Error(facade, 1, "notation", """{"ossia": ["variation", "variation", "variation"]}"""));
        Assert.Equal("badRequest", Error(facade, 1, "notation", """{"ossia": "o_9999"}"""));

        Call(facade, "language", """{"language": "ru"}""");
        Assert.Equal("Вариация", Call(facade, "notation", """{"bars": "selection", "ossia": "variation"}""").GetProperty("ossias")[0].GetProperty("label").GetString());
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
        JsonElement firstPhrase = Call(facade, "phrases").GetProperty("units")[0];
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
        Assert.NotEmpty(Call(again, "phrases").GetProperty("edits").GetProperty("phrase").GetProperty("removed").EnumerateArray());

        // Saved again without a change, the file is the same bytes.
        Assert.Equal(saved, again.Call(1, "project.save", """{"now": "2026-11-01T12:00:00Z"}"""));
    }

    [Fact]
    public void MarksAreKeptWithThePieceAndSavedInTheProjectFile()
    {
        (MusicCoreFacade facade, JsonElement opened) = Open();
        Assert.Equal(0, opened.GetProperty("project").GetProperty("marks").GetInt32());

        JsonElement inked = Call(facade, "ink.add", """{"strokes": [{"id": "s1", "tool": "pen", "colour": "carmine", "size": 2, "bar": 0, "u": 0.1, "v": 0.2, "points": [0, 0, 0.5, 1, 1, 0.5], "pressure": 0.5}]}""");
        Assert.Equal("carmine", inked.GetProperty("ink")[0].GetProperty("colour").GetString());
        JsonElement noted = Call(facade, "note.add", """{"bars": {"first": 1, "last": 2}, "text": "Lean into the E", "now": "2026-11-01T10:00:00Z"}""");
        Assert.Equal("n_0001", noted.GetProperty("notes")[0].GetProperty("id").GetString());
        Assert.Equal(1, noted.GetProperty("notes")[0].GetProperty("bars").GetProperty("first").GetInt32());
        Call(facade, "highlight.add", """{"bars": {"first": 1, "last": 1}, "notes": []}""");
        JsonElement fingered = Call(facade, "fingering.set", """{"noteId": "n1", "finger": 3}""");
        Assert.Equal(3, fingered.GetProperty("fingering")[0].GetProperty("finger").GetInt32());
        Assert.Equal("badRequest", Error(facade, 1, "fingering.set", """{"noteId": "n1", "finger": 6}"""));
        Assert.Equal("badRequest", Error(facade, 1, "note.add", """{"bars": {"first": 1}, "text": "  "}"""));

        JsonElement saved = Json(facade.Call(1, "project.save"));
        Assert.Equal("s1", saved.GetProperty("marks").GetProperty("ink")[0].GetProperty("id").GetString());
        Assert.Equal("Lean into the E", saved.GetProperty("marks").GetProperty("notes")[0].GetProperty("text").GetString());
        Assert.Equal(4, Call(facade, "marks").GetProperty("ink").GetArrayLength() + Call(facade, "marks").GetProperty("notes").GetArrayLength() + Call(facade, "marks").GetProperty("highlights").GetArrayLength() + Call(facade, "marks").GetProperty("fingering").GetArrayLength());
        Assert.Empty(Call(facade, "ink.clear").GetProperty("ink").EnumerateArray());
    }

    [Fact]
    public void BookmarksHaveCategoriesAndFollowThePhraseTheyWereMadeFrom()
    {
        (MusicCoreFacade facade, _) = Open("""{"language": "ru"}""");
        JsonElement[] phrases = [.. Call(facade, "phrases").GetProperty("units").EnumerateArray()];
        int first = phrases[0].GetProperty("number").GetInt32();

        JsonElement categories = Call(facade, "bookmarks").GetProperty("categories");
        Assert.Equal(["practise", "performance", "structure", "teacher"], categories.EnumerateArray().Select(category => category.GetProperty("id").GetString()!).ToArray());
        Assert.Equal("Поиграть", categories[0].GetProperty("name").GetString());

        JsonElement added = Call(facade, "bookmark.add", $$$"""{"phrase": {{{first}}}, "category": "structure", "now": "2026-11-01T10:00:00Z"}""");
        JsonElement bookmark = added.GetProperty("bookmarks")[0];
        Assert.Equal("b_0001", bookmark.GetProperty("id").GetString());
        Assert.True(bookmark.GetProperty("fromPhrase").GetBoolean());
        Assert.StartsWith("такты", bookmark.GetProperty("title").GetString());
        Assert.Equal(phrases[0].GetProperty("end").GetInt64(), bookmark.GetProperty("span").GetProperty("end").GetInt64());

        Call(facade, "phrases.edit", $$$"""{"joinWithNext": {{{first}}}}""");
        JsonElement followed = Call(facade, "bookmarks").GetProperty("bookmarks")[0];
        Assert.Equal(phrases[1].GetProperty("end").GetInt64(), followed.GetProperty("span").GetProperty("end").GetInt64());

        JsonElement own = Call(facade, "category.add", """{"name": "Экзамен", "ink": "ochre"}""");
        Assert.Equal("c_0001", own.GetProperty("categories")[4].GetProperty("id").GetString());
        Call(facade, "bookmark.change", """{"id": "b_0001", "title": "Тема", "category": "c_0001"}""");
        JsonElement removed = Call(facade, "category.remove", """{"id": "c_0001"}""");
        Assert.Equal("practise", removed.GetProperty("bookmarks")[0].GetProperty("category").GetString());
        Assert.Equal("Тема", removed.GetProperty("bookmarks")[0].GetProperty("title").GetString());
        Assert.Equal("badRequest", Error(facade, 1, "category.remove", """{"id": "practise"}"""));
        Assert.Equal("badRequest", Error(facade, 1, "bookmark.add", """{"bars": {"first": 1}, "category": "nowhere"}"""));
    }

    [Fact]
    public void ResumePointsSaveWhereThePlayerIsAndComeBackMostRecentFirst()
    {
        (MusicCoreFacade facade, JsonElement opened) = Open();
        Assert.Equal(0, opened.GetProperty("project").GetProperty("resumePoints").GetInt32());

        JsonElement started = Call(facade, "resume.save", """{"now": "2026-11-01T10:00:00Z"}""");
        Assert.Equal("r_0001", started.GetProperty("saved").GetProperty("id").GetString());
        Assert.Equal(100, started.GetProperty("saved").GetProperty("tempoPercent").GetInt32());
        Assert.Equal("both", started.GetProperty("saved").GetProperty("hands").GetString());

        JsonElement touched = Call(facade, "resume.save", """{"id": "r_0001", "passage": {"span": {"onset": 1920, "end": 3840}, "filter": {"hand": "left"}}, "overlays": ["insights", "pianoRoll"], "tempoPercent": 70, "hands": "left", "loop": true, "focused": true, "now": "2026-11-01T11:00:00Z"}""");
        JsonElement point = touched.GetProperty("saved");
        Assert.Equal(1920, point.GetProperty("at").GetInt64());
        Assert.Equal("left", point.GetProperty("passage").GetProperty("filter").GetProperty("hand").GetString());
        Assert.Equal(["pianoRoll", "insights"], point.GetProperty("overlays").EnumerateArray().Select(overlay => overlay.GetString()!).ToArray());
        Assert.Equal((70, "left", true, true), (point.GetProperty("tempoPercent").GetInt32(), point.GetProperty("hands").GetString(), point.GetProperty("loop").GetBoolean(), point.GetProperty("focused").GetBoolean()));

        Call(facade, "resume.save", """{"name": "Coda", "now": "2026-11-01T09:00:00Z"}""");
        JsonElement listed = Call(facade, "resume").GetProperty("resumePoints");
        Assert.Equal(["r_0001", "r_0002"], listed.EnumerateArray().Select(entry => entry.GetProperty("id").GetString()!).ToArray());
        Assert.Equal("Coda", listed[1].GetProperty("name").GetString());
        Assert.Equal("badRequest", Error(facade, 1, "resume.save", """{"id": "r_0001", "tempoPercent": 10}"""));
        Assert.Single(Call(facade, "resume.remove", """{"id": "r_0002"}""").GetProperty("resumePoints").EnumerateArray());

        JsonElement saved = Json(facade.Call(1, "project.save"));
        Assert.Equal(1920, saved.GetProperty("resumePoints")[0].GetProperty("at").GetInt64());
    }

    [Fact]
    public void IdeasKeepAVariationWithTheFactItCameFromAndOpenAgain()
    {
        (MusicCoreFacade facade, _) = Open();
        Call(facade, "select", """{"bars": {"first": 1, "last": 2}}""");
        Assert.Equal("unavailable", Error(facade, 1, "idea.keep"));
        JsonElement experiment = Call(facade, "experiments").GetProperty("experiments")[0];
        Call(facade, "experiments.start", $$$"""{"id": "{{{experiment.GetProperty("id").GetString()}}}", "alternative": 0}""");

        JsonElement kept = Call(facade, "idea.keep", """{"now": "2026-11-01T10:00:00Z"}""").GetProperty("ideas")[0];
        Assert.Equal("o_0001", kept.GetProperty("id").GetString());
        Assert.Equal(experiment.GetProperty("id").GetString(), kept.GetProperty("experiment").GetProperty("id").GetString());
        Assert.Equal(experiment.GetProperty("factIds")[0].GetString(), kept.GetProperty("factId").GetString());
        Assert.True(kept.GetProperty("hasVariation").GetBoolean());

        Call(facade, "variation.clear");
        Call(facade, "select", """{"bars": {"first": 5, "last": 6}}""");
        Call(facade, "idea.note", """{"id": "o_0001", "note": "Try it slower"}""");
        JsonElement opened = Call(facade, "idea.open", """{"id": "o_0001"}""");
        Assert.Equal(1, opened.GetProperty("selection").GetProperty("firstBar").GetInt32());
        Assert.NotEmpty(Call(facade, "variation").GetProperty("steps").EnumerateArray());
        Assert.Equal("Try it slower", Call(facade, "ideas").GetProperty("ideas")[0].GetProperty("note").GetString());

        JsonElement saved = Json(facade.Call(1, "project.save"));
        Assert.Equal(experiment.GetProperty("factIds")[0].GetString(), saved.GetProperty("observations")[0].GetProperty("factId").GetString());
        Assert.Empty(Call(facade, "idea.remove", """{"id": "o_0001"}""").GetProperty("ideas").EnumerateArray());
    }

    [Fact]
    public void SettingsHaveACatalogueAPresetAndPerPieceOverrides()
    {
        var facade = new MusicCoreFacade();
        JsonElement catalogue = Json(facade.Call(0, "settings"));
        JsonElement granularity = catalogue.GetProperty("settings").EnumerateArray().Single(setting => setting.GetProperty("id").GetString() == "phrases.granularity");
        Assert.Equal("piece", granularity.GetProperty("scope").GetString());
        Assert.Equal(["short", "phrases", "sentences"], granularity.GetProperty("choices").EnumerateArray().Select(choice => choice.GetString()!).ToArray());

        JsonElement learning = Json(facade.Call(0, "settings.state", """{"preset": "learning"}"""));
        Assert.Equal("learning", learning.GetProperty("preset").GetString());
        Assert.Equal("on", learning.GetProperty("values").GetProperty("guidance.hints").GetString());
        JsonElement custom = Json(facade.Call(0, "settings.state", """{"preset": "clean", "set": {"id": "guidance.hints", "value": "on"}}"""));
        Assert.Equal("custom", custom.GetProperty("preset").GetString());
        Assert.Equal("badRequest", Json(facade.Call(0, "settings.state", """{"set": {"id": "guidance.hints", "value": "maybe"}}""")).GetProperty("error").GetString());

        Json(facade.Open(FurElise, Fixture(FurElise)));
        JsonElement overridden = Call(facade, "settings.override", """{"id": "phrases.granularity", "value": "sentences"}""");
        Assert.Equal("sentences", overridden.GetProperty("overrides").GetProperty("phrases.granularity").GetString());
        Assert.Equal("badRequest", Error(facade, 1, "settings.override", """{"id": "appearance.theme", "value": "night"}"""));
        JsonElement resolved = Json(facade.Call(0, "settings.state", """{"preset": "learning", "overrides": {"phrases.granularity": "sentences"}}"""));
        Assert.Equal("sentences", resolved.GetProperty("values").GetProperty("phrases.granularity").GetString());
        Assert.Equal("sentences", Json(facade.Call(1, "project.save")).GetProperty("settings").GetProperty("phrases.granularity").GetString());
        Assert.Empty(Call(facade, "settings.override", """{"id": "phrases.granularity", "value": null}""").GetProperty("overrides").EnumerateObject());
    }

    [Fact]
    public void CheckIsEmptyForAnImportedPieceAndFlagsCanBeResolved()
    {
        (MusicCoreFacade facade, JsonElement opened) = Open();
        Assert.Equal(0, opened.GetProperty("project").GetProperty("openChecks").GetInt32());

        JsonElement check = Call(facade, "check");
        Assert.Empty(check.GetProperty("flags").EnumerateArray());
        Assert.Equal(0, check.GetProperty("open").GetInt32());
        Assert.Equal("badRequest", Error(facade, 1, "check.resolve", """{"bar": 1}"""));
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