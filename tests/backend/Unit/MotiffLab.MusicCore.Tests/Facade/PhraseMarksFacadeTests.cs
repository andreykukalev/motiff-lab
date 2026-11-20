using System.Text.Json;

using MotiffLab.MusicCore.Facade;

using static MotiffLab.MusicCore.Tests.Facade.FacadeCalls;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>
/// The phrase marks through the facade (the phrases spec, «Engine»; within contract 13): one answer for the marks (each
/// phrase's start with its bar and beat, provenance, the found boundary a moved one replaced and «Почему здесь», its
/// motifs, the sections of the form with their letters), the edits of the iPad each one step for Undo, what an edit
/// changed, the selection never changed by one, and the player's boundaries kept when the notes are read again.
/// </summary>
public class PhraseMarksFacadeTests
{
    private readonly MusicCoreFacade _facade = new();

    /// <summary>The example both apps carry, 21 phrases in four sentences, opened in Russian as piece 1 for each test.</summary>
    public PhraseMarksFacadeTests() =>
        Assert.Equal(1, Json(_facade.Open("FurElise.musicxml", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "FurElise.musicxml")), """{"id": "p_marks", "language": "ru"}""")).GetProperty("piece").GetInt32());

    private JsonElement[] Units(JsonElement answer) => [.. answer.GetProperty("units").EnumerateArray()];

    private static long Onset(JsonElement unit) => unit.GetProperty("onset").GetInt64();

    [Fact]
    public void The_phrases_answer_marks_every_phrase_start_and_every_section_start()
    {
        JsonElement phrases = Call(_facade, "phrases");
        JsonElement[] units = Units(phrases);

        Assert.Equal(0, phrases.GetProperty("own").GetInt32());
        JsonElement first = units[0].GetProperty("start");
        Assert.Equal(JsonValueKind.Null, first.GetProperty("close").ValueKind);
        Assert.Equal("с начала пьесы", first.GetProperty("why").GetString());
        foreach (JsonElement unit in units)
        {
            JsonElement start = unit.GetProperty("start");
            Assert.True(start.GetProperty("bar").GetInt32() >= 0);
            Assert.True(start.GetProperty("beat").GetInt32() >= 1);
            Assert.Equal(JsonValueKind.Null, start.GetProperty("found").ValueKind);
            Assert.False(string.IsNullOrEmpty(start.GetProperty("why").GetString()));
            JsonElement[] motifs = [.. unit.GetProperty("motifs").EnumerateArray()];
            Assert.NotEmpty(motifs);
            Assert.Equal(Onset(unit), motifs[0].GetProperty("onset").GetInt64());
            Assert.Equal(unit.GetProperty("end").GetInt64(), motifs[^1].GetProperty("end").GetInt64());
        }

        // A close of the phrase before is a reason for a found boundary, as «Почему здесь» words it.
        Assert.Contains(units.Skip(1), unit => unit.GetProperty("start").GetProperty("close").GetString() is "full" or "half"
            && unit.GetProperty("start").GetProperty("why").GetString()!.Contains("каденции", StringComparison.Ordinal));

        JsonElement[] sections = [.. phrases.GetProperty("sections").EnumerateArray()];
        Assert.NotEmpty(sections);
        Assert.Equal("A", sections[0].GetProperty("label").GetString());
        Assert.Equal(1, sections[0].GetProperty("phrase").GetInt32());
        JsonElement[] form = [.. Call(_facade, "dna").GetProperty("form").GetProperty("sections").EnumerateArray()];
        Assert.Equal(form.Select(static section => section.GetProperty("label").GetString()), sections.Select(static section => section.GetProperty("label").GetString()));
        Assert.All(sections, section => Assert.True(section.GetProperty("phrase").ValueKind == JsonValueKind.Number));
    }

    [Fact]
    public void A_mark_dragged_is_one_step_that_snaps_to_a_note_and_Undo_puts_the_edits_back()
    {
        JsonElement[] units = Units(Call(_facade, "phrases"));
        long from = Onset(units[1]);

        // Dragged a little before its note: it lands on the note nearest, as the player's own boundary.
        JsonElement moved = Call(_facade, "phrases.edit", $$$"""{"move": {"from": {{{from}}}, "to": {{{from - 470}}}}}""");
        JsonElement start = Units(moved)[1].GetProperty("start");
        long at = start.GetProperty("at").GetInt64();
        Assert.NotEqual(from, at);
        Assert.Contains(Call(_facade, "piece").GetProperty("notes").EnumerateArray(), note => note.GetProperty("onset").GetInt64() == at);
        Assert.Equal("userEdited", start.GetProperty("provenance").GetString());
        Assert.Equal(from, start.GetProperty("found").GetProperty("at").GetInt64());
        Assert.Equal("там, где вы поставили границу", start.GetProperty("why").GetString());
        Assert.Equal(1, moved.GetProperty("own").GetInt32());

        // What it changed: phrases 1 and 2, before and after, so a selection that was phrase 2 can be offered its new span.
        JsonElement[] changed = [.. moved.GetProperty("changed").EnumerateArray()];
        Assert.Equal([1, 2], changed.Select(static change => change.GetProperty("before").GetProperty("number").GetInt32()));
        Assert.Equal(from, changed[1].GetProperty("before").GetProperty("onset").GetInt64());
        Assert.Equal(at, changed[1].GetProperty("after").GetProperty("onset").GetInt64());
        Assert.Equal(at, changed[1].GetProperty("after").GetProperty("selectable").GetProperty("onset").GetInt64());

        // Undo is "restore" with the edits before: one step back, and Redo the same way.
        string previous = moved.GetProperty("previous").GetRawText();
        JsonElement undone = Call(_facade, "phrases.edit", $$"""{"restore": {{previous}}}""");
        Assert.Equal(from, Onset(Units(undone)[1]));
        Assert.Equal(0, undone.GetProperty("own").GetInt32());
        JsonElement redone = Call(_facade, "phrases.edit", $$"""{"restore": {{undone.GetProperty("previous").GetRawText()}}}""");
        Assert.Equal(at, Onset(Units(redone)[1]));

        // «Вернуть найденную»: the found boundary back.
        JsonElement back = Call(_facade, "phrases.edit", $$$"""{"putBack": {{{at}}}}""");
        Assert.Equal(from, Onset(Units(back)[1]));
        Assert.Equal(0, back.GetProperty("own").GetInt32());
    }

    [Fact]
    public void A_start_is_set_and_taken_away_and_a_tick_where_none_starts_is_refused()
    {
        JsonElement[] units = Units(Call(_facade, "phrases"));
        long inside = Onset(units[0]) + ((units[0].GetProperty("end").GetInt64() - Onset(units[0])) / 2);

        JsonElement started = Call(_facade, "phrases.edit", $$$"""{"start": {{{inside}}}}""");
        Assert.Equal(units.Length + 1, Units(started).Length);
        long at = Onset(Units(started)[1]);

        JsonElement removed = Call(_facade, "phrases.edit", $$$"""{"remove": {{{at}}}}""");
        Assert.Equal(units.Length, Units(removed).Length);
        Assert.Equal("badRequest", Error(_facade, "phrases.edit", $$$"""{"remove": {{{at}}}}"""));
        Assert.Equal("badRequest", Error(_facade, "phrases.edit", $$$"""{"move": {"from": {{{at}}}, "to": 0}}"""));
    }

    [Fact]
    public void Reset_returns_every_found_boundary_in_one_step_and_says_how_many_of_the_player_s_it_replaced()
    {
        JsonElement[] units = Units(Call(_facade, "phrases"));
        Call(_facade, "phrases.edit", $$$"""{"remove": {{{Onset(units[2])}}}}""");
        JsonElement two = Call(_facade, "phrases.edit", $$$"""{"move": {"from": {{{Onset(units[1])}}}, "to": {{{Onset(units[1]) - 480}}}}}""");
        Assert.Equal(2, two.GetProperty("own").GetInt32());

        JsonElement reset = Call(_facade, "phrases.edit", """{"resetAll": true}""");

        Assert.Equal(2, reset.GetProperty("replaced").GetInt32());
        Assert.Equal(0, reset.GetProperty("own").GetInt32());
        Assert.Equal(units.Select(Onset), Units(reset).Select(Onset));
        Assert.All(reset.GetProperty("edits").EnumerateObject(), level => Assert.Empty(level.Value.GetProperty("added").EnumerateArray()));
        JsonElement undone = Call(_facade, "phrases.edit", $$"""{"restore": {{reset.GetProperty("previous").GetRawText()}}}""");
        Assert.Equal(2, undone.GetProperty("own").GetInt32());
    }

    [Fact]
    public void An_edit_never_changes_the_selection_even_one_that_was_the_phrase()
    {
        JsonElement[] units = Units(Call(_facade, "phrases"));
        JsonElement selectable = units[1].GetProperty("selectable");
        Call(_facade, "select", $$"""{"span": {{selectable.GetRawText()}}}""");

        Call(_facade, "phrases.edit", $$$"""{"move": {"from": {{{Onset(units[1])}}}, "to": {{{Onset(units[1]) - 480}}}}}""");

        JsonElement motif = Json(_facade.Call(1, "project.save")).GetProperty("workspace").GetProperty("motifs")[0];
        Assert.Equal(selectable.GetProperty("onset").GetInt64(), motif.GetProperty("onset").GetInt64());
        Assert.Equal(selectable.GetProperty("end").GetInt64() - selectable.GetProperty("onset").GetInt64(), motif.GetProperty("duration").GetInt64());
    }

    [Fact]
    public void A_correction_of_the_notes_reads_the_phrases_again_and_leaves_the_player_s_boundaries_where_they_are()
    {
        JsonElement[] units = Units(Call(_facade, "phrases"));
        JsonElement moved = Call(_facade, "phrases.edit", $$$"""{"move": {"from": {{{Onset(units[1])}}}, "to": {{{Onset(units[1]) - 480}}}}}""");
        long at = Onset(Units(moved)[1]);
        string edits = moved.GetProperty("edits").GetRawText();
        string note = Call(_facade, "corrections", """{"bar": 4}""").GetProperty("notes")[0].GetProperty("id").GetString()!;

        Call(_facade, "corrections.remove", $$"""{"bar": 4, "id": "{{note}}"}""");

        JsonElement again = Call(_facade, "phrases");
        Assert.Equal(edits, again.GetProperty("edits").GetRawText());
        Assert.Contains(Units(again), unit => Onset(unit) == at && unit.GetProperty("start").GetProperty("provenance").GetString() == "userEdited");
    }
}