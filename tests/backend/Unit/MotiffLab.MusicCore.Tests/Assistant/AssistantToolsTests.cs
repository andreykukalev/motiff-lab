using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Assistant;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Variations;

using static MotiffLab.MusicCore.Tests.Assistant.AssistantScores;

namespace MotiffLab.MusicCore.Tests.Assistant;

/// <summary>The assistant's tool registry (0028 section 2; the AI companion study, section 4.2).</summary>
public class AssistantToolsTests
{
    private const string Bars = "\"bars\": {\"from\": 1, \"to\": 4}";

    private static string Catalogue()
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            AssistantTools.WriteCatalogue(json);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    [Fact]
    public void The_catalogue_holds_every_operation_and_every_kind_once()
    {
        string[] names = [.. AssistantTools.All.Select(tool => tool.Name)];

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(VariationOperations.All.Count, AssistantTools.All.Count(tool => tool.Name.StartsWith("variation.", StringComparison.Ordinal)));
        Assert.Contains("facts.get", names);
        Assert.Contains("harmony.chords", names);
        Assert.Contains("check.flags", names);
        Assert.Contains("notes.edit", names);
        Assert.Equal([ToolKind.Read, ToolKind.ProposeDraft, ToolKind.Check, ToolKind.SuggestAction], AssistantTools.All.Select(tool => tool.Kind).Distinct().Order());
        Assert.All(AssistantTools.All.Where(tool => tool.Name.StartsWith("variation.", StringComparison.Ordinal)), tool => Assert.Equal(ToolKind.ProposeDraft, tool.Kind));
    }

    [Fact]
    public void Every_schema_is_strict_and_the_catalogue_is_the_same_each_time()
    {
        string first = Catalogue();
        Assert.Equal(first, Catalogue());
        foreach (JsonElement tool in Json(first).GetProperty("tools").EnumerateArray())
        {
            JsonElement parameters = tool.GetProperty("parameters");
            Assert.Equal("object", parameters.GetProperty("type").GetString());
            Assert.False(parameters.GetProperty("additionalProperties").GetBoolean());
            Assert.Equal(
                parameters.GetProperty("properties").EnumerateObject().Select(property => property.Name),
                parameters.GetProperty("required").EnumerateArray().Select(name => name.GetString()));
        }

        JsonElement sequence = Json(first).GetProperty("tools").EnumerateArray().Single(tool => tool.GetProperty("name").GetString() == "variation.sequence");
        Assert.Equal([-4, -3, -2, -1, 1, 2, 3, 4], sequence.GetProperty("parameters").GetProperty("properties").GetProperty("steps").GetProperty("enum").EnumerateArray().Select(step => step.GetInt32()));
    }

    [Theory]
    [InlineData("variation.sequence", "\"hand\": \"both\", \"scope\": \"all\", \"keep\": [], \"seed\": 0, \"steps\": 0, \"repeats\": 1, \"diatonic\": true", "\"steps\" must be one of")]
    [InlineData("variation.sequence", "\"hand\": \"both\", \"scope\": \"all\", \"keep\": [], \"seed\": 0, \"steps\": 2, \"repeats\": 4, \"diatonic\": true", "\"repeats\" must be a whole number from 1 to 3")]
    [InlineData("variation.simplify", "\"hand\": \"both\", \"scope\": \"all\", \"keep\": [], \"seed\": 0, \"level\": 1, \"extra\": 1", "\"extra\" is not a parameter")]
    [InlineData("variation.simplify", "\"hand\": \"both\", \"scope\": \"all\", \"keep\": [], \"seed\": 0", "\"level\" is missing")]
    [InlineData("variation.ornament", "\"hand\": \"both\", \"scope\": \"bass\", \"keep\": [], \"seed\": 0, \"kind\": \"trill\"", "\"scope\" must be one of all, melody")]
    [InlineData("variation.voicing", "\"hand\": \"both\", \"scope\": \"all\", \"keep\": [\"pitches\", \"pitches\"], \"seed\": 0, \"kind\": \"open\"", "names an item twice")]
    [InlineData("notes.edit", "\"edits\": [{\"kind\": \"change\", \"address\": \"1.R.1\", \"bar\": null, \"hand\": null, \"start\": null, \"length\": null, \"pitch\": 120, \"velocity\": null}]", "\"edits[0].pitch\" must be a whole number from 21 to 108")]
    [InlineData("practice.suggest", "\"hand\": \"both\", \"tempo\": 10, \"loop\": true", "\"tempo\" must be a whole number from 25 to 150")]
    public void A_parameter_out_of_range_is_refused_with_its_reason(string tool, string parameters, string reason)
    {
        AssistantPiece piece = Open(FurElise());

        ToolResult result = AssistantTools.Call(piece, tool, Json($"{{{Bars}, {parameters}}}"));

        Assert.False(result.Ok);
        Assert.Contains(reason, result.Refusal, StringComparison.Ordinal);
        Assert.Empty(result.Drafts);
    }

    [Fact]
    public void An_unknown_tool_and_bars_the_piece_has_not_are_refused()
    {
        AssistantPiece piece = Open(FurElise(), words: Words.Russian);

        Assert.Equal("Инструмента «notes.delete» нет.", AssistantTools.Call(piece, "notes.delete", Json("{}")).Refusal);
        Assert.StartsWith("В пьесе такты с ", AssistantTools.Call(piece, "facts.get", Json("""{"bars": {"from": 500, "to": 501}, "hand": "both"}""")).Refusal, StringComparison.Ordinal);
        Assert.Contains("32", AssistantTools.Call(piece, "harmony.chords", Json("""{"bars": {"from": 1, "to": 40}}""")).Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void The_engine_refuses_an_operation_the_keep_choices_rule_out_with_its_own_reason()
    {
        AssistantPiece piece = Open(FurElise());

        ToolResult result = AssistantTools.Call(piece, "variation.rhythm", Json($$"""{{{Bars}}, "hand": "right", "scope": "all", "keep": ["rhythm"], "seed": 0, "kind": "syncopate", "eighths": 1}"""));

        Assert.False(result.Ok);
        Assert.Equal(RhythmReshape.Instance.DisabledReason(new HashSet<KeepProperty> { KeepProperty.Rhythm }, Words.English), result.Refusal);
    }

    [Fact]
    public void A_variation_is_a_draft_of_the_engine_and_the_piece_stays_as_it_was()
    {
        ImportedPiece imported = FurElise();
        AssistantPiece piece = Open(imported);
        Note[] before = [.. piece.Notes];
        string call = $$"""{{{Bars}}, "hand": "both", "scope": "all", "keep": [], "seed": 3, "kind": "octaveUp"}""";

        ToolResult result = AssistantTools.Call(piece, "variation.voicing", Json(call));

        Assert.True(result.Ok, result.Refusal);
        AssistantDraft draft = Assert.Single(result.Drafts);
        Assert.True(result.Ok);
        Assert.Equal("variation.voicing", draft.Tool);
        Assert.NotEmpty(draft.Edits);
        Assert.All(draft.Edits, edit => Assert.Equal(DraftEditKind.Change, edit.Kind));
        Assert.All(draft.Edits, edit => Assert.Equal(edit.Before!.Value.Pitch.MidiNumber + 12, edit.After!.Value.Pitch.MidiNumber));
        Assert.Equal(before, piece.Notes);
        Assert.Equal(before, imported.Notes);
        // Same call, same draft (determinism of the operations).
        Assert.Equal(draft.Variation.Explanation, Assert.Single(AssistantTools.Call(piece, "variation.voicing", Json(call)).Drafts).Variation.Explanation);
        Assert.Equal(draft.Edits, AssistantTools.Call(Open(FurElise()), "variation.voicing", Json(call)).Drafts.Single().Edits);
    }

    [Fact]
    public void A_draft_names_notes_by_address_and_is_never_applied()
    {
        AssistantPiece piece = Open(FurElise());

        JsonElement answer = Answer(piece, "variation.articulation", $$"""{{{Bars}}, "hand": "right", "scope": "all", "keep": [], "seed": 0, "kind": "short"}""");

        Assert.True(answer.GetProperty("ok").GetBoolean());
        Assert.Equal("proposeDraft", answer.GetProperty("kind").GetString());
        JsonElement draft = answer.GetProperty("drafts")[0];
        Assert.False(draft.GetProperty("applied").GetBoolean());
        Assert.NotEmpty(draft.GetProperty("explanation").GetString()!);
        Assert.All(draft.GetProperty("edits").EnumerateArray(), edit => Assert.Matches(NoteAddress.Pattern, edit.GetProperty("address").GetString()!));
        Assert.DoesNotContain("n_0", answer.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Notes_edit_changes_removes_and_adds_by_address_on_the_grid()
    {
        ImportedPiece imported = Score(Note("E", 5, 2) + Note("D", 5, 2) + Note("C", 5, 2), Note("G", 4, 6));
        AssistantPiece piece = Open(imported);
        string edits = """
            [{"kind": "change", "address": "1.R.2", "bar": null, "hand": null, "start": null, "length": null, "pitch": 76, "velocity": null},
             {"kind": "remove", "address": "1.R.3", "bar": null, "hand": null, "start": null, "length": null, "pitch": null, "velocity": null},
             {"kind": "add", "address": null, "bar": 2, "hand": "right", "start": 8, "length": 8, "pitch": 72, "velocity": null}]
            """;

        ToolResult result = AssistantTools.Call(piece, "notes.edit", Json($$"""{"bars": {"from": 1, "to": 2}, "edits": {{edits}}}"""));

        AssistantDraft draft = Assert.Single(result.Drafts);
        Assert.Equal([DraftEditKind.Remove, DraftEditKind.Change, DraftEditKind.Add], draft.Edits.Select(edit => edit.Kind));
        Assert.Equal(76, draft.Edits[1].After!.Value.Pitch.MidiNumber);
        DraftEdit added = draft.Edits[2];
        Assert.Equal(1440 + 480, added.After!.Value.Span.Onset.Value);
        Assert.Equal(480, added.After.Value.Span.Duration);
        Assert.Equal(Hand.Right, added.Hand);
    }

    [Fact]
    public void Notes_edit_refuses_a_note_outside_the_draft_twice_edited_or_off_the_bar()
    {
        AssistantPiece piece = Open(Score(Note("E", 5, 2) + Note("D", 5, 2) + Note("C", 5, 2), Note("G", 4, 6)));
        static string Edit(string address, string start = "null") =>
            $$"""{"kind": "change", "address": "{{address}}", "bar": null, "hand": null, "start": {{start}}, "length": null, "pitch": null, "velocity": 90}""";

        Assert.Contains("outside the bars", AssistantTools.Call(piece, "notes.edit", Json($$"""{"bars": {"from": 1, "to": 1}, "edits": [{{Edit("2.R.1")}}]}""")).Refusal, StringComparison.Ordinal);
        Assert.Contains("edited twice", AssistantTools.Call(piece, "notes.edit", Json($$"""{"bars": {"from": 1, "to": 1}, "edits": [{{Edit("1.R.1")}}, {{Edit("1.R.1")}}]}""")).Refusal, StringComparison.Ordinal);
        Assert.Contains("32nd 0 to 23", AssistantTools.Call(piece, "notes.edit", Json($$"""{"bars": {"from": 1, "to": 1}, "edits": [{{Edit("1.R.1", "30")}}]}""")).Refusal, StringComparison.Ordinal);
        Assert.Contains("no note 1.R.9", AssistantTools.Call(piece, "notes.edit", Json($$"""{"bars": {"from": 1, "to": 1}, "edits": [{{Edit("1.R.9")}}]}""")).Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void Draft_check_reports_the_keep_checks_without_a_draft()
    {
        AssistantPiece piece = Open(Score(Note("E", 5, 2) + Note("D", 5, 2) + Note("C", 5, 2)));

        JsonElement answer = Answer(piece, "draft.check", """
            {"bars": {"from": 1, "to": 1}, "keep": ["rhythm", "pitches"],
             "edits": [{"kind": "change", "address": "1.R.1", "bar": null, "hand": null, "start": null, "length": null, "pitch": 77, "velocity": null}]}
            """);

        Assert.True(answer.GetProperty("ok").GetBoolean());
        Assert.False(answer.TryGetProperty("drafts", out _));
        Assert.Equal("kept", answer.GetProperty("result").GetProperty("keep").GetProperty("rhythm").GetString());
        Assert.Equal("noLongerKept", answer.GetProperty("result").GetProperty("keep").GetProperty("pitches").GetString());
    }

    [Fact]
    public void Reading_tools_name_notes_by_address()
    {
        AssistantPiece piece = Open(FurElise(), words: Words.Russian);

        JsonElement notes = Answer(piece, "notes.list", """{"bars": {"from": 1, "to": 2}, "hand": "right"}""").GetProperty("result").GetProperty("notes");
        JsonElement chords = Answer(piece, "harmony.chords", """{"bars": {"from": 1, "to": 8}}""").GetProperty("result").GetProperty("chords");
        JsonElement practice = Answer(piece, "practice.suggest", """{"bars": {"from": 3, "to": 4}, "hand": "left", "tempo": 60, "loop": true}""").GetProperty("result");

        Assert.Equal("1.R.1", notes[0].GetProperty("address").GetString());
        Assert.All(notes.EnumerateArray(), note => Assert.EndsWith(".R." + note.GetProperty("address").GetString()!.Split('.')[2], note.GetProperty("address").GetString()!, StringComparison.Ordinal));
        Assert.NotEqual(0, chords.GetArrayLength());
        Assert.All(chords.EnumerateArray(), chord => Assert.All(chord.GetProperty("notes").EnumerateArray(), note => Assert.Matches(NoteAddress.Pattern, note.GetString()!)));
        Assert.Equal("Зациклить такты 3–4 в темпе 60 % · левая рука", practice.GetProperty("label").GetString());
    }
}