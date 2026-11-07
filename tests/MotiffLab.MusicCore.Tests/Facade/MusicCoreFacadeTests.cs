using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

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
    public void Corrections_change_take_out_put_back_and_undo_the_notes_of_a_bar_and_are_kept_with_the_project()
    {
        (MusicCoreFacade facade, _) = Open("""{"id": "p_fur"}""");
        JsonElement bar = Call(facade, "corrections", """{"bar": 1}""");
        Assert.Equal(1, bar.GetProperty("bar").GetInt32());
        Assert.Equal(0, bar.GetProperty("count").GetInt32());
        JsonElement first = bar.GetProperty("notes")[0];
        string id = first.GetProperty("id").GetString()!;
        int pitch = first.GetProperty("pitch").GetInt32();
        Assert.False(first.GetProperty("edited").GetBoolean());
        Assert.Equal(1, first.GetProperty("atBar").GetInt32());

        JsonElement higher = Call(facade, "corrections.change", $$"""{"bar": 1, "id": "{{id}}", "pitch": 1}""");
        JsonElement changed = higher.GetProperty("notes").EnumerateArray().Single(note => note.GetProperty("id").GetString() == id);
        Assert.Equal(pitch + 1, changed.GetProperty("pitch").GetInt32());
        Assert.True(changed.GetProperty("edited").GetBoolean());
        Assert.Equal(1, higher.GetProperty("count").GetInt32());
        Assert.Contains(Call(facade, "piece").GetProperty("notes").EnumerateArray(), note => note.GetProperty("id").GetString() == id && note.GetProperty("pitch").GetInt32() == pitch + 1);
        Assert.Contains("<mei", Call(facade, "notation").GetProperty("mei").GetString());

        JsonElement removed = Call(facade, "corrections.remove", $$"""{"bar": 1, "id": "{{id}}"}""");
        Assert.DoesNotContain(removed.GetProperty("notes").EnumerateArray(), note => note.GetProperty("id").GetString() == id);
        Assert.Equal(id, removed.GetProperty("removed")[0].GetProperty("id").GetString());
        Assert.Equal(2, removed.GetProperty("count").GetInt32());

        // Saved and opened again, the corrections are there.
        string saved = Call(facade, "project.save").GetRawText();
        var again = new MusicCoreFacade();
        Assert.False(Json(again.Open("fur.motiff.json", System.Text.Encoding.UTF8.GetBytes(saved))).TryGetProperty("error", out _));
        Assert.Equal(2, Json(again.Call(1, "corrections", """{"bar": 1}""")).GetProperty("count").GetInt32());

        Assert.Equal(1, Call(facade, "corrections.undo", """{"bar": 1}""").GetProperty("count").GetInt32());
        JsonElement back = Call(facade, "corrections.revert", $$"""{"bar": 1, "id": "{{id}}"}""");
        Assert.Equal(0, back.GetProperty("count").GetInt32());
        Assert.Equal(pitch, back.GetProperty("notes").EnumerateArray().Single(note => note.GetProperty("id").GetString() == id).GetProperty("pitch").GetInt32());

        Assert.Equal("unavailable", Error(facade, 1, "corrections.undo", """{"bar": 1}"""));
        Assert.Equal("badRequest", Error(facade, 1, "corrections", """{"bar": 999}"""));
        Assert.Equal("notFound", Error(facade, 1, "corrections.change", """{"bar": 1, "id": "n_none", "pitch": 1}"""));
        Assert.Equal("unavailable", Error(facade, 1, "corrections.remove", """{"bar": 1, "id": "perf-1"}"""));
    }

    [Fact]
    public void The_note_editor_adds_sets_and_resets_notes_on_a_draft_kept_only_once_saved()
    {
        (MusicCoreFacade facade, _) = Open("""{"id": "p_fur"}""");
        JsonElement begun = Call(facade, "corrections.begin", """{"bar": 2}""");
        Assert.True(begun.GetProperty("editing").GetBoolean());
        Assert.False(begun.GetProperty("canUndo").GetBoolean());
        long onset = begun.GetProperty("span").GetProperty("onset").GetInt64();

        // A note a scan missed, added where the player taps; it takes the hand of the notes near it.
        JsonElement added = Call(facade, "corrections.add", $$"""{"bar": 2, "pitch": 76, "onset": {{onset + 240}}, "length": 240}""");
        string id = added.GetProperty("added").GetString()!;
        Assert.Equal("a1", id);
        Assert.True(added.GetProperty("changed").GetBoolean());
        JsonElement note = added.GetProperty("notes").EnumerateArray().Single(note => note.GetProperty("id").GetString() == id);
        Assert.Equal(76, note.GetProperty("pitch").GetInt32());
        Assert.True(note.GetProperty("edited").GetBoolean());

        // Dragged on the roll: pitch, onset and length at once.
        JsonElement set = Call(facade, "corrections.set", $$"""{"bar": 2, "id": "{{id}}", "pitch": 77, "onset": {{onset + 480}}, "length": 120}""");
        note = set.GetProperty("notes").EnumerateArray().Single(note => note.GetProperty("id").GetString() == id);
        Assert.Equal(77, note.GetProperty("pitch").GetInt32());
        Assert.Equal(onset + 480, note.GetProperty("onset").GetInt64());

        // Not saved yet: the project file keeps the corrections as they were.
        string draft = Call(facade, "project.save").GetRawText();
        Assert.DoesNotMatch("\"noteId\":\\s*\"a1\"", draft);

        // Reset takes every correction out, and undo steps back through the reset and the set.
        Assert.Equal(0, Call(facade, "corrections.reset", """{"bar": 2}""").GetProperty("count").GetInt32());
        Assert.Equal(2, Call(facade, "corrections.undo", """{"bar": 2}""").GetProperty("count").GetInt32());
        JsonElement undone = Call(facade, "corrections.undo", """{"bar": 2}""");
        Assert.Equal(76, undone.GetProperty("notes").EnumerateArray().Single(note => note.GetProperty("id").GetString() == id).GetProperty("pitch").GetInt32());

        JsonElement saved = Call(facade, "corrections.save", """{"bar": 2}""");
        Assert.False(saved.GetProperty("editing").GetBoolean());
        string file = Call(facade, "project.save").GetRawText();
        var again = new MusicCoreFacade();
        Assert.False(Json(again.Open("fur.motiff.json", System.Text.Encoding.UTF8.GetBytes(file))).TryGetProperty("error", out _));
        Assert.Contains(Json(again.Call(1, "corrections", """{"bar": 2}""")).GetProperty("notes").EnumerateArray(), note => note.GetProperty("id").GetString() == id);

        // Discarded, a draft leaves the corrections as they were before it.
        Call(facade, "corrections.begin", """{"bar": 2}""");
        Call(facade, "corrections.remove", $$"""{"bar": 2, "id": "{{id}}"}""");
        JsonElement discarded = Call(facade, "corrections.discard", """{"bar": 2}""");
        Assert.Contains(discarded.GetProperty("notes").EnumerateArray(), note => note.GetProperty("id").GetString() == id);
        Assert.Equal("unavailable", Error(facade, 1, "corrections.save", """{"bar": 2}"""));
        Assert.Equal("badRequest", Error(facade, 1, "corrections.add", """{"bar": 2, "pitch": 200, "onset": 0}"""));
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
        Assert.Equal("phrase", found[0].GetProperty("name").GetString());
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

        // The device's level is shown without becoming the piece's own.
        Assert.Equal("sentence", Call(again, "phrases.level", """{"level": "sentence", "remember": false}""").GetProperty("level").GetString());
        Assert.Equal("short", Call(again, "settings").GetProperty("overrides").GetProperty("phrases.granularity").GetString());
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
    public void CircleAnswersTheKeyOfTheSelectionOnTheCircleOfFifths()
    {
        (MusicCoreFacade facade, _) = Open();
        Call(facade, "select", """{"bars": {"first": 1, "last": 2}}""");

        JsonElement circle = Call(facade, "circle").GetProperty("circle");

        Assert.Equal(12, circle.GetProperty("sectors").GetArrayLength());
        Assert.InRange(circle.GetProperty("position").GetInt32(), 0, 11);
        Assert.Equal(4, circle.GetProperty("neighbours").GetArrayLength());
        Assert.Equal(8, circle.GetProperty("scale").GetArrayLength());
        Assert.Equal(8, circle.GetProperty("scaleNotes").GetArrayLength());
        Assert.InRange(circle.GetProperty("lines").GetArrayLength(), 2, 3);
        Assert.False(string.IsNullOrEmpty(circle.GetProperty("name").GetString()));
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
        string[] inStream = [.. understood.GetProperty("inStream").EnumerateArray().Select(id => id.GetString()!)];
        Assert.Subset(inStream.ToHashSet(), strongest.ToHashSet());
        Assert.Equal(inStream.Length, inStream.Distinct().Count());
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

        // A fact that lists what it counts carries its one-line brief for the stream.
        Assert.Contains(facts, fact => fact.GetProperty("id").GetString() == "melody.line" && fact.TryGetProperty("brief", out JsonElement brief) && brief.GetString()!.Length > 0);

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
    public void TheRollGetsTheNotesAndBarsPackedAndTheirNames()
    {
        (MusicCoreFacade facade, JsonElement opened) = Open();
        int count = opened.GetProperty("summary").GetProperty("noteCount").GetInt32();

        JsonElement roll = Call(facade, "roll");
        Assert.Equal(count, roll.GetProperty("ids").GetArrayLength());
        Assert.Equal(count * 7, roll.GetProperty("notes").GetArrayLength());
        Assert.Equal(0, roll.GetProperty("bars").GetArrayLength() % 4);
        Assert.True(roll.GetProperty("end").GetInt64() > 0);
        JsonElement names = roll.GetProperty("names");
        Assert.Equal(count, names.GetProperty("notes").GetArrayLength());
        Assert.Equal(names.GetProperty("keyTicks").GetArrayLength(), names.GetProperty("rows").GetArrayLength());
        Assert.Equal("C4", names.GetProperty("rows")[0][60].GetString());
        // Without the octave inside the notes and on the keys; the octave once at the side, in words.
        Assert.Equal(count, names.GetProperty("short").GetArrayLength());
        Assert.Equal("C", names.GetProperty("shortRows")[0][60].GetString());
        Assert.Equal(11, names.GetProperty("octaves").GetArrayLength());
        Assert.Equal("octave 4", names.GetProperty("octaves")[5].GetString());

        Assert.Equal("до¹", Call(facade, "roll", """{"naming": "syllables"}""").GetProperty("names").GetProperty("rows")[0][60].GetString());
        Call(facade, "language", """{"language": "ru"}""");
        Assert.Equal("до¹", Call(facade, "roll").GetProperty("names").GetProperty("rows")[0][60].GetString());
        Assert.Equal("первая октава", Call(facade, "roll").GetProperty("names").GetProperty("octaves")[5].GetString());
        Assert.Equal("C4", Call(facade, "roll", """{"naming": "letters"}""").GetProperty("names").GetProperty("rows")[0][60].GetString());
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
        Assert.Equal(0, inked.GetProperty("ink")[0].GetProperty("barNumber").GetInt32());
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
    public void TheDetectedSentencesBecomeStructureBookmarksOnce()
    {
        (MusicCoreFacade facade, _) = Open();
        int sentences = Call(facade, "phrases", """{"level": "sentence"}""").GetProperty("units").GetArrayLength();
        Assert.True(sentences > 0);

        JsonElement added = Call(facade, "bookmark.structure", """{"now": "2026-11-05T10:00:00Z"}""");
        JsonElement[] bookmarks = [.. added.GetProperty("bookmarks").EnumerateArray()];
        Assert.Equal(sentences, bookmarks.Length);
        Assert.All(bookmarks, bookmark => Assert.Equal("structure", bookmark.GetProperty("category").GetString()));
        Assert.StartsWith("Sentence A, bars 0–", bookmarks[0].GetProperty("title").GetString());

        Assert.Equal(sentences, Call(facade, "bookmark.structure").GetProperty("bookmarks").GetArrayLength());
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
    public void A_project_files_summary_is_read_without_opening_the_piece()
    {
        var facade = new MusicCoreFacade();
        Json(facade.Open(FurElise, Fixture(FurElise), """{"id": "p_lib", "now": "2026-11-05T10:00:00Z"}"""));
        Call(facade, "resume.save", """{"name": "Coda"}""");
        string file = facade.Call(1, "project.save");
        string request = JsonSerializer.Serialize(new { file });

        JsonElement project = Json(new MusicCoreFacade().Call(0, "project.summary", request)).GetProperty("project");

        Assert.Equal("p_lib", project.GetProperty("id").GetString());
        Assert.True(project.GetProperty("hasNotes").GetBoolean());
        Assert.False(project.GetProperty("document").GetBoolean());
        Assert.Equal(0, project.GetProperty("checkBars").GetInt32());
        Assert.Equal("Coda", project.GetProperty("latest").GetProperty("name").GetString());
        Assert.Equal("badRequest", Json(facade.Call(0, "project.summary", """{"file": "{}"}""")).GetProperty("error").GetString());
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
    public void CheckNamesTheReasonsGivesEachBarsSpanAndWhereTheDoubtsComeFrom()
    {
        (MusicCoreFacade facade, _) = Open();
        JsonObject file = JsonNode.Parse(facade.Call(1, "project.save"))!.AsObject();
        file["check"] = JsonNode.Parse("""[{"bar": 3, "source": "Scan", "reason": "BarTooLong", "resolved": false}, {"bar": 3, "source": "Scan", "reason": "GraceNotes", "resolved": false}, {"bar": 500, "source": "Scan", "reason": "BarTooShort", "resolved": true}]""");
        file["scan"] = JsonNode.Parse("""{"engine": "homr 0.7.0", "confirmedBars": []}""");
        var reopened = new MusicCoreFacade();
        JsonElement opened = Json(reopened.Open("elise.motiff.json", System.Text.Encoding.UTF8.GetBytes(file.ToJsonString()), """{"language": "ru"}"""));
        int piece = opened.GetProperty("piece").GetInt32();

        JsonElement check = Json(reopened.Call(piece, "check"));
        Assert.Equal("scanned", check.GetProperty("origin").GetString());
        Assert.Equal("homr 0.7.0", check.GetProperty("engine").GetString());
        Assert.Equal(2, check.GetProperty("open").GetInt32());
        JsonElement first = check.GetProperty("flags")[0];
        Assert.Equal("Такт длиннее размера", first.GetProperty("name").GetString());
        JsonElement bar = Json(reopened.Call(piece, "select", """{"bars": {"first": 3, "last": 3}}""")).GetProperty("selection");
        Assert.Equal(bar.GetProperty("onset").GetInt32(), first.GetProperty("span").GetProperty("onset").GetInt32());
        Assert.Equal(bar.GetProperty("end").GetInt32(), first.GetProperty("span").GetProperty("end").GetInt32());
        Assert.Equal(JsonValueKind.Null, check.GetProperty("flags")[2].GetProperty("span").ValueKind);

        JsonElement resolved = Json(reopened.Call(piece, "check.resolve", """{"bar": 3}"""));
        Assert.Equal(0, resolved.GetProperty("open").GetInt32());
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
    public void PieceDnaGivesEveryLaneWithHowFarItIsFilled()
    {
        (MusicCoreFacade facade, _) = Open();

        JsonElement dna = Call(facade, "dna");

        Assert.Equal("partial", dna.GetProperty("status").GetString());
        int firstBar = dna.GetProperty("firstBar").GetInt32();
        int lastBar = dna.GetProperty("lastBar").GetInt32();
        Assert.True(lastBar > firstBar, $"{firstBar}–{lastBar}");

        // The form of the whole piece is not analysed yet, and without a selection there is no idea.
        Assert.Equal("notYet", dna.GetProperty("form").GetProperty("status").GetString());
        Assert.Empty(dna.GetProperty("form").GetProperty("sections").EnumerateArray());
        Assert.Equal("notYet", dna.GetProperty("ideas").GetProperty("status").GetString());
        Assert.Empty(dna.GetProperty("ideas").GetProperty("items").EnumerateArray());

        // The key lane from the file's key signature: A minor, no sharps or flats, covering the piece.
        JsonElement keys = dna.GetProperty("keys");
        Assert.Equal("partial", keys.GetProperty("status").GetString());
        JsonElement first = keys.GetProperty("spans")[0];
        Assert.Equal(0, first.GetProperty("key").GetProperty("fifths").GetInt32());
        Assert.Equal("minor", first.GetProperty("key").GetProperty("mode").GetString());
        Assert.Equal(firstBar, first.GetProperty("passage").GetProperty("firstBar").GetInt32());
        Assert.Equal(lastBar, keys.GetProperty("spans")[keys.GetProperty("spans").GetArrayLength() - 1].GetProperty("passage").GetProperty("lastBar").GetInt32());

        // New material: bars whose line repeats no earlier bar; the motif's repeat is not new.
        JsonElement newMaterial = dna.GetProperty("newMaterial");
        Assert.Equal("ready", newMaterial.GetProperty("status").GetString());
        int[] bars = [.. newMaterial.GetProperty("bars").EnumerateArray().Select(bar => bar.GetInt32())];
        Assert.NotEmpty(bars);
        Assert.Equal(bars.Order(), bars);
        Assert.All(bars, bar => Assert.InRange(bar, firstBar, lastBar));
        Assert.True(bars.Length < lastBar - firstBar + 1, $"{bars.Length} new bars of {lastBar - firstBar + 1}");
    }

    [Fact]
    public void PieceDnaTakesTheSelectionAsItsIdeaWithItsRepeats()
    {
        (MusicCoreFacade facade, _) = Open("""{"language": "ru"}""");
        Call(facade, "select", """{"bars": {"first": 1, "last": 2}}""");

        JsonElement ideas = Call(facade, "dna").GetProperty("ideas");

        Assert.Equal("partial", ideas.GetProperty("status").GetString());
        JsonElement idea = Assert.Single(ideas.GetProperty("items").EnumerateArray());
        Assert.Equal("idea_1", idea.GetProperty("id").GetString());
        Assert.True(idea.GetProperty("fromSelection").GetBoolean());
        Assert.StartsWith("Ваше выделение, такты 1–2", idea.GetProperty("title").GetString());
        Assert.StartsWith("Звучит ещё", idea.GetProperty("summary").GetString());

        JsonElement[] occurrences = [.. idea.GetProperty("occurrences").EnumerateArray()];
        Assert.Single(occurrences, occurrence => occurrence.GetProperty("kind").GetString() == "original");
        Assert.Contains(occurrences, occurrence => occurrence.GetProperty("kind").GetString() == "exact" && occurrence.GetProperty("transposition").GetInt32() == 0);
        long[] onsets = [.. occurrences.Select(occurrence => occurrence.GetProperty("passage").GetProperty("onset").GetInt64())];
        Assert.Equal(onsets.Order(), onsets);
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

    [Fact]
    public void A_piece_keeps_its_sources_a_pdf_inside_it_and_takes_what_a_version_is_sure_of()
    {
        // Testing item 16 (Andrei, 2026-11-06): one piece, many sources, and the result is the player's own version.
        (MusicCoreFacade facade, _) = Open("""{"id": "p_fur"}""");
        ImportedPiece piece = PieceImporter.Import(FurElise, Fixture(FurElise)).Piece!;
        TickSpan bar5 = piece.Meter.GetBar(5).Span;
        Note[] melody = [.. piece.Notes.Where(note => bar5.Contains(note.Span.Onset)).OrderByDescending(note => note.Pitch.MidiNumber).Take(3)];
        Note[] played = [.. piece.Notes, .. melody.Select((note, i) => note with { Id = $"x{i}", Pitch = new Pitch(note.Pitch.MidiNumber + (i * 7) - 3) })];
        string midi = Convert.ToBase64String(MidiExporter.Export(played, [], piece.Meter, "performance"));
        int printed = Call(facade, "piece").GetProperty("noteCount").GetInt32();

        JsonElement sources = Call(facade, "sources");
        Assert.Equal("file", sources.GetProperty("base").GetString());
        Assert.Equal("Base: the notes of the file", sources.GetProperty("summary").GetString());
        Assert.Equal(JsonValueKind.Null, sources.GetProperty("document").ValueKind);

        Call(facade, "performance.add", $$$"""{"name": "Live", "midi": "{{{midi}}}"}""");
        JsonElement all = Call(facade, "performance.takeAll", """{"id": "perf_1"}""").GetProperty("performance");
        Assert.Equal(1, all.GetProperty("takenBars").GetInt32());
        Assert.Equal(printed + 3, Call(facade, "piece").GetProperty("noteCount").GetInt32());
        Assert.Equal("Base: the notes of the file · 1 other version · 1 bar taken", Call(facade, "sources").GetProperty("summary").GetString());
        Assert.Equal(1, Call(facade, "performance.takeAll", """{"id": "perf_1"}""").GetProperty("performance").GetProperty("takenBars").GetInt32());

        // A PDF kept inside the piece, its pages shared over the bars, then its notes read and added as a version.
        byte[] pdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");
        Assert.Equal("unavailable", Error(facade, 1, "sources.documentNotes", JsonSerializer.Serialize(new { musicXml = Convert.ToBase64String(Fixture(FurElise)) })));
        JsonElement kept = Call(facade, "sources.document", JsonSerializer.Serialize(new { pdf = Convert.ToBase64String(pdf), pages = 2 }));
        Assert.Equal(2, kept.GetProperty("document").GetProperty("pages").GetInt32());
        Assert.False(kept.GetProperty("document").GetProperty("readFrom").GetBoolean());
        JsonElement version = Call(facade, "sources.documentNotes", JsonSerializer.Serialize(new { musicXml = Convert.ToBase64String(Fixture(FurElise)), engine = "homr 0.7.0", pages = 2 })).GetProperty("performance");
        Assert.Equal("perf_2", version.GetProperty("id").GetString());
        Assert.Equal("score", version.GetProperty("source").GetString());
        Assert.Equal("fur-elise-original", version.GetProperty("name").GetString());
        Assert.Equal("badRequest", Error(facade, 1, "sources.document", JsonSerializer.Serialize(new { pdf = Convert.ToBase64String([1, 2, 3]) })));

        // The PDF stays on the device: the online copy keeps the notes and leaves the PDF out.
        Project saved = ProjectFile.Read(System.Text.Encoding.UTF8.GetBytes(facade.Call(1, "project.save")));
        Assert.Equal(pdf, saved.Document);
        Assert.Null(saved.ForOnline()!.Document);
        Assert.Equal(saved.Performances, saved.ForOnline()!.Performances);
    }

    [Fact]
    public void A_performance_is_compared_taken_into_the_score_kept_and_restored()
    {
        (MusicCoreFacade facade, _) = Open("""{"id": "p_fur"}""");
        ImportedPiece piece = PieceImporter.Import(FurElise, Fixture(FurElise)).Piece!;
        TickSpan bar5 = piece.Meter.GetBar(5).Span;
        Note[] melody = [.. piece.Notes.Where(note => bar5.Contains(note.Span.Onset)).OrderByDescending(note => note.Pitch.MidiNumber).Take(3)];
        Note[] played = [.. piece.Notes, .. melody.Select((note, i) => note with { Id = $"x{i}", Pitch = new Pitch(note.Pitch.MidiNumber + (i * 7) - 3) })];
        string midi = Convert.ToBase64String(MidiExporter.Export(played, [], piece.Meter, "performance"));
        int printed = Call(facade, "piece").GetProperty("noteCount").GetInt32();

        JsonElement added = Call(facade, "performance.add", $$$"""{"name": "Live", "midi": "{{{midi}}}"}""").GetProperty("performance");
        Assert.Equal("perf_1", added.GetProperty("id").GetString());
        Assert.Equal("midi", added.GetProperty("source").GetString());
        Assert.Equal(printed, added.GetProperty("printed").GetInt32());
        Assert.Equal(printed, added.GetProperty("matched").GetInt32());
        JsonElement different = Assert.Single(added.GetProperty("bars").EnumerateArray(), bar => bar.GetProperty("kind").GetString() != "none");
        Assert.Equal(5, different.GetProperty("bar").GetInt32());
        Assert.Equal(bar5.Onset.Value, different.GetProperty("span").GetProperty("onset").GetInt64());
        Assert.Equal(bar5.End.Value, different.GetProperty("span").GetProperty("end").GetInt64());
        Assert.Equal("addedNotes", different.GetProperty("kind").GetString());
        Assert.Equal("+3 · added notes", different.GetProperty("marker").GetString());
        Assert.Equal(3, different.GetProperty("played").GetArrayLength());

        // The comparison layer: the version above the bars, the notes it adds named for their plum, and heard on its own.
        JsonElement layer = Call(facade, "notation", """{"bars": {"first": 5, "last": 5}, "ossia": "perf_1"}""");
        JsonElement above = Assert.Single(layer.GetProperty("ossias").EnumerateArray());
        Assert.Equal("Live", above.GetProperty("label").GetString());
        Assert.Equal(3, above.GetProperty("added").GetArrayLength());
        Assert.All(above.GetProperty("added").EnumerateArray(), id => Assert.StartsWith("ossia-v-", id.GetString(), StringComparison.Ordinal));
        Assert.Contains(above.GetProperty("added")[0].GetString()!, layer.GetProperty("mei").GetString(), StringComparison.Ordinal);
        Assert.Empty(above.GetProperty("artefacts").EnumerateArray());
        Assert.Equal(printed + 3, Call(facade, "playback", """{"of": "version", "id": "perf_1"}""").GetProperty("notes").GetArrayLength());
        Assert.Equal("badRequest", Error(facade, 1, "notation", """{"ossia": "perf_9"}"""));

        JsonElement taken = Call(facade, "performance.take", """{"id": "perf_1", "bars": [5]}""").GetProperty("performance");
        Assert.Equal(1, taken.GetProperty("takenBars").GetInt32());
        Assert.Equal(printed + 3, Call(facade, "piece").GetProperty("noteCount").GetInt32());
        Assert.Equal("badRequest", Error(facade, 1, "performance.take", """{"id": "perf_1", "bars": [999]}"""));
        Assert.Equal("notFound", Error(facade, 1, "performance.take", """{"id": "perf_9", "bars": [5]}"""));

        // The performance and the taken bar come back with the project file.
        string saved = facade.Call(1, "project.save", """{"now": "2026-11-05T12:00:00Z"}""");
        Assert.Equal(ProjectFile.Version, Json(saved).GetProperty("version").GetInt32());
        var again = new MusicCoreFacade();
        again.Open("fur-elise" + ProjectFile.Extension, System.Text.Encoding.UTF8.GetBytes(saved));
        Assert.Equal(printed + 3, Call(again, "piece").GetProperty("noteCount").GetInt32());
        Assert.Equal("Live", Assert.Single(Call(again, "performances").GetProperty("performances").EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal(saved, again.Call(1, "project.save", """{"now": "2026-11-05T12:00:00Z"}"""));

        JsonElement restored = Call(again, "performance.restore", """{"id": "perf_1", "bars": [5]}""").GetProperty("performance");
        Assert.Equal(0, restored.GetProperty("takenBars").GetInt32());
        Assert.Equal(printed, Call(again, "piece").GetProperty("noteCount").GetInt32());

        JsonElement dismissed = Call(again, "performance.dismiss", """{"id": "perf_1", "bar": 5}""").GetProperty("performance");
        Assert.True(dismissed.GetProperty("bars").EnumerateArray().Single(bar => bar.GetProperty("bar").GetInt32() == 5).GetProperty("dismissed").GetBoolean());
        Assert.Equal("Concert", Call(again, "performance.rename", """{"id": "perf_1", "name": " Concert "}""").GetProperty("performance").GetProperty("name").GetString());

        Assert.Empty(Call(again, "performance.remove", """{"id": "perf_1"}""").GetProperty("performances").EnumerateArray());
        Assert.Equal("badRequest", Error(again, 1, "performance.add", """{"name": "Bad", "midi": "not base64!"}"""));
        Assert.Equal("badRequest", Error(again, 1, "performance.add", """{"name": "Bad", "midi": "AAAA"}"""));
    }

    [Fact]
    public void Another_version_is_added_from_a_MusicXML_or_MIDI_file()
    {
        (MusicCoreFacade facade, _) = Open("""{"id": "p_fur"}""");
        string score = Convert.ToBase64String(Fixture(FurElise));

        JsonElement added = Call(facade, "performance.add", $$$"""{"name": "Official", "file": "{{{score}}}", "fileName": "{{{FurElise}}}"}""").GetProperty("performance");
        Assert.Equal("score", added.GetProperty("source").GetString());
        Assert.Equal(added.GetProperty("printed").GetInt32(), added.GetProperty("matched").GetInt32());
        Assert.All(added.GetProperty("bars").EnumerateArray(), bar => Assert.Equal("none", bar.GetProperty("kind").GetString()));

        Assert.Equal("badRequest", Error(facade, 1, "performance.add", """{"name": "Bad", "file": "not base64!", "fileName": "a.mid"}"""));
        Assert.Equal("badRequest", Error(facade, 1, "performance.add", """{"name": "Bad", "file": "AAAA", "fileName": "notes.txt"}"""));
        Assert.Equal("badRequest", Error(facade, 1, "performance.add", """{"name": "Bad", "file": "AAAA"}"""));
    }

    [Fact]
    public void A_recording_heard_in_parts_opens_as_a_new_piece_from_the_recording()
    {
        ImportedPiece piece = PieceImporter.Import(FurElise, Fixture(FurElise)).Piece!;
        string midi = Convert.ToBase64String(MidiExporter.Export(piece.Notes, [], piece.Meter, "heard"));
        var facade = new MusicCoreFacade();

        string request = $$$"""{"id": "p_heard", "now": "2026-11-06T12:00:00Z", "transcription": {"engine": "transkun", "milliseconds": 300000, "parts": [{"index": 0, "midi": "{{{midi}}}"}, {"index": 2, "midi": "{{{midi}}}"}]}}""";
        JsonElement opened = Json(facade.Open("Recital.m4a", [], request));
        Assert.Equal(1, opened.GetProperty("piece").GetInt32());
        Assert.Equal("transcribed", opened.GetProperty("project").GetProperty("origin").GetString());

        // Saved and opened again, it is the same transcribed project: its notes heard, its seams kept.
        Project project = ProjectFile.Read(System.Text.Encoding.UTF8.GetBytes(facade.Call(1, "project.save")));
        Assert.Equal("transkun", project.Transcription!.Engine);
        Assert.Equal(2, project.Transcription.Seams.Count);
        Assert.Equal("Recital.mid", project.FileName);
        Assert.Equal(1, Json(new MusicCoreFacade().Open("Recital.motiff.json", System.Text.Encoding.UTF8.GetBytes(facade.Call(1, "project.save")))).GetProperty("piece").GetInt32());

        JsonElement failed = Json(facade.Open("Recital.m4a", [], """{"transcription": {"engine": "transkun", "milliseconds": 100000, "parts": []}}"""));
        Assert.Equal("badRequest", failed.GetProperty("error").GetString());
    }

    [Fact]
    public void A_recording_heard_in_parts_is_joined_into_one_version()
    {
        (MusicCoreFacade facade, _) = Open("""{"id": "p_fur"}""");
        ImportedPiece piece = PieceImporter.Import(FurElise, Fixture(FurElise)).Piece!;
        string midi = Convert.ToBase64String(MidiExporter.Export(piece.Notes, [], piece.Meter, "heard"));

        // Seven minutes at most, heard in parts of up to two minutes that overlap by five seconds.
        JsonElement parts = Json(facade.Call(0, "recording.parts", """{"milliseconds": 600000}"""));
        Assert.Equal(420_000, parts.GetProperty("milliseconds").GetInt64());
        JsonElement[] planned = [.. parts.GetProperty("parts").EnumerateArray()];
        Assert.Equal(4, planned.Length);
        Assert.Equal(0, planned[0].GetProperty("sendFrom").GetInt64());
        Assert.Equal(planned[0].GetProperty("keepTo").GetInt64() + 5_000, planned[0].GetProperty("sendTo").GetInt64());
        Assert.Equal("badRequest", Error(facade, 0, "recording.parts", """{"milliseconds": 0}"""));

        JsonElement added = Call(facade, "performance.add", $$$"""{"name": "Recital", "engine": "transkun", "milliseconds": 100000, "parts": [{"index": 0, "midi": "{{{midi}}}"}]}""").GetProperty("performance");
        Assert.Equal("recording", added.GetProperty("source").GetString());
        Assert.Equal("transkun", added.GetProperty("engine").GetString());
        Assert.Equal(added.GetProperty("printed").GetInt32(), added.GetProperty("matched").GetInt32());

        JsonElement joined = Call(facade, "performance.add", $$$"""{"name": "Long", "milliseconds": 300000, "parts": [{"index": 0, "midi": "{{{midi}}}"}, {"index": 2, "midi": "{{{midi}}}"}]}""").GetProperty("performance");
        Assert.Equal("recording", joined.GetProperty("source").GetString());
        Assert.True(joined.GetProperty("matched").GetInt32() > 0);
        Assert.Equal("badRequest", Error(facade, 1, "performance.add", """{"name": "Bad", "milliseconds": 100000, "parts": [{"index": 3, "midi": "AAAA"}]}"""));
        Assert.Equal("badRequest", Error(facade, 1, "performance.add", """{"name": "Bad", "milliseconds": 100000, "parts": []}"""));
    }

    [Fact]
    public void APdfOpensAsAPdfPieceKeepingItsPagesBookmarksAndInkAndRefusesCallsAboutNotes()
    {
        byte[] pdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");
        var facade = new MusicCoreFacade();
        JsonElement opened = Json(facade.Open("Nocturne.pdf", pdf, """{"id": "p_pdf", "now": "2026-11-05T10:00:00Z"}"""));

        Assert.Equal(1, opened.GetProperty("piece").GetInt32());
        Assert.False(opened.GetProperty("project").GetProperty("hasNotes").GetBoolean());
        Assert.True(opened.GetProperty("project").GetProperty("document").GetBoolean());
        Assert.Equal("Nocturne.pdf", opened.GetProperty("document").GetProperty("fileName").GetString());
        Assert.False(opened.TryGetProperty("summary", out _));
        Assert.Equal(pdf, Call(facade, "document").GetProperty("pdf").GetBytesFromBase64());
        Assert.Equal("noNotes", Error(facade, 1, "phrases"));

        Call(facade, "document.bookmark.add", """{"page": 3, "now": "2026-11-05T10:01:00Z"}""");
        JsonElement marks = Call(facade, "document.bookmark.add", """{"page": 1, "name": "Start"}""").GetProperty("marks");
        Assert.Equal([1, 3], marks.GetProperty("bookmarks").EnumerateArray().Select(bookmark => bookmark.GetProperty("page").GetInt32()));
        string third = marks.GetProperty("bookmarks")[1].GetProperty("id").GetString()!;
        Call(facade, "document.bookmark.rename", $$"""{"id": "{{third}}", "name": "Coda"}""");
        Call(facade, "document.ink.add", """{"strokes": [{"id": "s1", "tool": "pen", "colour": "carmine", "size": 2, "page": 2, "u": 0.1, "v": 4, "points": [0, 0, 0.5, 1, 1, 0.5], "pressure": 0.5}, {"id": "s2", "tool": "pen", "colour": "graphite", "size": 2, "page": 3, "u": 0.2, "v": 9, "points": [0, 0, 1, 1, 1, 0.5], "pressure": 0.5}]}""");
        JsonElement ink = Call(facade, "document.ink.clear", """{"page": 3}""").GetProperty("marks").GetProperty("ink");
        Assert.Equal(2, Assert.Single(ink.EnumerateArray()).GetProperty("page").GetInt32());
        Assert.Equal("badRequest", Error(facade, 1, "document.ink.add", """{"strokes": [{"id": "s3", "tool": "pen", "colour": "carmine", "size": 2, "page": 0, "u": 0, "v": 0, "points": [0, 0, 1], "pressure": 0.5}]}"""));

        string file = facade.Call(1, "project.save", """{"now": "2026-11-05T11:00:00Z"}""");
        Assert.True(facade.Close(1));
        var again = new MusicCoreFacade();
        JsonElement reopened = Json(again.Open("Nocturne.motiff.json", System.Text.Encoding.UTF8.GetBytes(file)));
        Assert.True(reopened.TryGetProperty("document", out _), reopened.ToString());
        JsonElement kept = Json(again.Call(1, "document.marks")).GetProperty("marks");
        Assert.Equal(["Start", "Coda"], kept.GetProperty("bookmarks").EnumerateArray().Select(bookmark => bookmark.GetProperty("name").GetString()));
        Assert.Equal("s1", Assert.Single(kept.GetProperty("ink").EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal(pdf, Json(again.Call(1, "document")).GetProperty("pdf").GetBytesFromBase64());

        Assert.Equal("import", Json(new MusicCoreFacade().Open("Nocturne.pdf", "not a pdf"u8.ToArray())).GetProperty("error").GetString());
    }

    [Fact]
    public void Notes_a_scan_read_from_a_pdf_piece_fill_that_piece_which_then_opens_as_notes_or_as_its_pdf()
    {
        // Testing item 16 (Andrei, 2026-11-06): the PDF is available right inside the piece.
        var facade = new MusicCoreFacade();
        byte[] pdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");
        Json(facade.Open("Nocturne.pdf", pdf, """{"id": "p_pdf", "now": "2026-11-05T10:00:00Z"}"""));
        Call(facade, "document.bookmark.add", """{"page": 2, "name": "Trio"}""");
        string request = JsonSerializer.Serialize(new { musicXml = Convert.ToBase64String(Fixture(FurElise)), engine = "homr 0.7.0", pages = 3, now = "2026-11-05T12:00:00Z" });

        JsonElement read = Call(facade, "document.read", request);

        JsonElement project = read.GetProperty("project");
        Assert.Equal("p_pdf", project.GetProperty("id").GetString());
        Assert.Equal("scanned", project.GetProperty("origin").GetString());
        Assert.True(project.GetProperty("hasNotes").GetBoolean());
        Assert.Equal(1, project.GetProperty("resumePoints").GetInt32());
        byte[] file = System.Text.Encoding.UTF8.GetBytes(read.GetProperty("file").GetString()!);
        Assert.Equal(read.GetProperty("file").GetString(), facade.Call(1, "project.save", """{"now": "2026-11-05T12:00:00Z"}"""));
        Assert.Equal("unavailable", Error(facade, 1, "document.read", request));

        var again = new MusicCoreFacade();
        JsonElement notes = Json(again.Open("Nocturne.motiff.json", file));
        Assert.False(notes.TryGetProperty("document", out _), "a piece with notes opens as notes");
        JsonElement sources = Call(again, "sources", null);
        Assert.Equal("scan", sources.GetProperty("base").GetString());
        Assert.Equal("Base: the notes read from the PDF", sources.GetProperty("summary").GetString());
        Assert.Equal(3, sources.GetProperty("document").GetProperty("pages").GetInt32());
        Assert.True(sources.GetProperty("document").GetProperty("readFrom").GetBoolean());
        JsonElement asPdf = Json(again.Open("Nocturne.motiff.json", file, """{"view": "pdf"}"""));
        Assert.True(asPdf.TryGetProperty("document", out _), asPdf.ToString());
        Assert.Equal("Trio", Json(again.Call(2, "document.marks")).GetProperty("marks").GetProperty("bookmarks")[0].GetProperty("name").GetString());

        var other = new MusicCoreFacade();
        Json(other.Open("Nocturne.pdf", pdf));
        Assert.Equal("import", Error(other, 1, "document.read", """{"musicXml": "AAAA", "engine": "homr"}"""));
        Assert.Equal("badRequest", Error(other, 1, "document.read", """{"musicXml": "not base64!"}"""));
    }
}