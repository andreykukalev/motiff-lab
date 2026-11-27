using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Projects;

using static MotiffLab.MusicCore.Tests.Facade.FacadeCalls;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>
/// The phrase as an object of the project (#318, 0026 C1) through the facade: an anchor <c>ph_</c> minted when an idea or a
/// resume point in phrase focus first attaches to a phrase, the same id on two devices, carried with what is attached by the
/// player's boundary edits (scenario 4), and, when it no longer matches the phrases, told and attached again as a group.
/// </summary>
public class PhraseAnchorsFacadeTests
{
    private const string Now = """ "now": "2026-11-27T10:00:00Z" """;

    private static readonly byte[] s_elise = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "FurElise.musicxml"));

    private static MusicCoreFacade Opened(IdSource? ids = null)
    {
        MusicCoreFacade facade = ids is null ? new() : new(ids);
        Assert.Equal(1, Json(facade.Open("FurElise.musicxml", s_elise, """{"id": "p_anchors", "language": "en"}""")).GetProperty("piece").GetInt32());
        return facade;
    }

    private static JsonElement[] Units(JsonElement answer) => [.. answer.GetProperty("units").EnumerateArray()];

    private static long Onset(JsonElement unit) => unit.GetProperty("onset").GetInt64();

    private static long End(JsonElement unit) => unit.GetProperty("end").GetInt64();

    private static string? Anchor(JsonElement entity) => entity.GetProperty("anchor").GetString();

    private static JsonElement[] Ideas(MusicCoreFacade facade) => [.. Call(facade, "ideas").GetProperty("ideas").EnumerateArray()];

    /// <summary>Keeps a note alone as an idea on <paramref name="onset"/> to <paramref name="end"/>; the anchor it took.</summary>
    private static string Keep(MusicCoreFacade facade, long onset, long end, string note = "Breathe here")
    {
        Call(facade, "select", $$$"""{"span": {"onset": {{{onset}}}, "end": {{{end}}}}}""");
        return Anchor(Call(facade, "idea.keep", $$$"""{"note": "{{{note}}}", {{{Now}}}}""").GetProperty("ideas").EnumerateArray().Last())!;
    }

    /// <summary>A tick inside the unit, on a note of it, so a mark dragged there snaps to that note.</summary>
    private static long Inside(JsonElement unit) => Onset(unit) + ((End(unit) - Onset(unit)) / 2);

    [Fact]
    public void Two_devices_attaching_to_one_phrase_offline_mint_one_id_from_its_level_and_bounds()
    {
        MusicCoreFacade one = Opened(IdSource.Random), other = Opened(IdSource.Random);
        JsonElement phrase = Units(Call(one, "phrases"))[3];

        string id = Keep(one, Onset(phrase), End(phrase));

        Assert.Equal(id, Keep(other, Onset(phrase) + 240, End(phrase), "Other device"));
        string key = string.Create(CultureInfo.InvariantCulture, $"phrase|{Onset(phrase)}|{End(phrase)}");
        Assert.Equal("ph_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32], id);
        JsonElement phrases = Call(one, "phrases");
        Assert.Equal(id, Anchor(Units(phrases)[3]));
        Assert.Null(Anchor(Units(phrases)[2]));
        Assert.Equal(id, phrases.GetProperty("anchors")[0].GetProperty("id").GetString());
        Assert.Equal(4, phrases.GetProperty("anchors")[0].GetProperty("phrase").GetInt32());
        Assert.Equal(0, phrases.GetProperty("unmatched").GetInt32());
    }

    [Fact]
    public void A_piece_with_nothing_attached_has_no_anchors_and_its_file_none()
    {
        MusicCoreFacade facade = Opened();
        Call(facade, "resume.save", $$$"""{"passage": {"span": {"onset": 0, "end": 1920}}, {{{Now}}}}""");

        Assert.Empty(Call(facade, "phrases").GetProperty("anchors").EnumerateArray());
        Assert.Null(Anchor(Call(facade, "resume").GetProperty("resumePoints")[0]));
        JsonNode saved = JsonNode.Parse(facade.Call(1, "project.save"))!;
        Assert.Null(saved["workspace"]!["phrases"]!["anchors"]);
        Assert.Null(saved["workspace"]!["resumePoints"]![0]!["anchor"]);
    }

    [Fact]
    public void Twelve_ideas_on_a_phrase_whose_start_is_dragged_a_bar_on_all_go_with_it_and_the_id_stays()
    {
        MusicCoreFacade facade = Opened();
        JsonElement[] units = Units(Call(facade, "phrases"));
        JsonElement phrase = units[4];
        string[] kept = [.. Enumerable.Range(1, 12).Select(n => Keep(facade, Onset(phrase), End(phrase), $"Idea {n}"))];
        Assert.All(kept, id => Assert.Equal(kept[0], id));

        JsonElement moved = Call(facade, "phrases.edit", $$$"""{"move": {"from": {{{Onset(phrase)}}}, "to": {{{Inside(phrase)}}}}}""");
        JsonElement now = Units(moved)[4];
        Assert.True(Onset(now) > Onset(phrase));
        Assert.Equal(kept[0], Anchor(now));
        Assert.Null(Anchor(Units(moved)[3]));
        JsonElement anchor = moved.GetProperty("anchors")[0];
        Assert.Equal((Onset(now), End(phrase)), (anchor.GetProperty("onset").GetInt64(), anchor.GetProperty("end").GetInt64()));
        Assert.All(Ideas(facade), idea => Assert.Equal(kept[0], Anchor(idea)));

        // Undo carries it back, and the end of the phrase before moving carries that phrase's end, not this anchor.
        JsonElement undone = Call(facade, "phrases.edit", $$$"""{"restore": {{{moved.GetProperty("previous")}}}, "expected": {{{moved.GetProperty("edits")}}}}""");
        Assert.Equal(kept[0], Anchor(Units(undone)[4]));
        Assert.Equal(Onset(phrase), undone.GetProperty("anchors")[0].GetProperty("onset").GetInt64());
        JsonElement next = Units(undone)[5];
        JsonElement longer = Call(facade, "phrases.edit", $$$"""{"move": {"from": {{{Onset(next)}}}, "to": {{{Inside(next)}}}}}""");
        Assert.Equal(kept[0], Anchor(Units(longer)[4]));
        Assert.Equal(Onset(Units(longer)[5]), longer.GetProperty("anchors")[0].GetProperty("end").GetInt64());
    }

    [Fact]
    public void A_start_taken_away_joins_two_anchors_into_the_smaller_id_with_everything_on_both()
    {
        MusicCoreFacade facade = Opened();
        JsonElement[] units = Units(Call(facade, "phrases"));
        string first = Keep(facade, Onset(units[1]), End(units[1]));
        string second = Keep(facade, Onset(units[2]), End(units[2]));
        string smaller = string.CompareOrdinal(first, second) < 0 ? first : second;

        JsonElement joined = Call(facade, "phrases.edit", $$$"""{"remove": {{{Onset(units[2])}}}}""");

        Assert.Equal(smaller, Anchor(Units(joined)[1]));
        Assert.Equal((Onset(units[1]), End(units[2])), (joined.GetProperty("anchors")[0].GetProperty("onset").GetInt64(), joined.GetProperty("anchors")[0].GetProperty("end").GetInt64()));
        Assert.Single(joined.GetProperty("anchors").EnumerateArray());
        Assert.All(Ideas(facade), idea => Assert.Equal(smaller, Anchor(idea)));

        // A start set inside leaves the anchor on the first part.
        JsonElement split = Call(facade, "phrases.edit", $$$"""{"start": {{{Onset(units[2])}}}}""");
        Assert.Equal(smaller, Anchor(Units(split)[1]));
        Assert.Null(Anchor(Units(split)[2]));
    }

    [Fact]
    public void Undo_of_a_join_brings_the_second_anchor_back_with_its_ideas_and_resume_point_and_Redo_joins_them_again()
    {
        MusicCoreFacade facade = Opened();
        JsonElement[] units = Units(Call(facade, "phrases"));
        string first = Keep(facade, Onset(units[1]), End(units[1]));
        string second = Keep(facade, Onset(units[2]), End(units[2]));
        Keep(facade, Onset(units[2]), End(units[2]), "Second again");
        string point = Call(facade, "resume.save", $$$"""{"passage": {"span": {{{units[2].GetProperty("selectable")}}}}, "focused": true, {{{Now}}}}""").GetProperty("saved").GetProperty("id").GetString()!;
        string smaller = string.CompareOrdinal(first, second) < 0 ? first : second;
        JsonElement joined = Call(facade, "phrases.edit", $$$"""{"remove": {{{Onset(units[2])}}}}""");
        Assert.All(Ideas(facade), idea => Assert.Equal(smaller, Anchor(idea)));

        JsonElement undone = Call(facade, "phrases.edit", $$$"""{"restore": {{{joined.GetProperty("previous")}}}, "expected": {{{joined.GetProperty("edits")}}}}""");

        Assert.Equal((first, second), (Anchor(Units(undone)[1]), Anchor(Units(undone)[2])));
        Assert.Equal([first, second, second], Ideas(facade).Select(Anchor));
        JsonElement resumed = Call(facade, "resume").GetProperty("resumePoints").EnumerateArray().Single(entry => entry.GetProperty("id").GetString() == point);
        Assert.Equal(second, Anchor(resumed));
        Assert.Equal(Onset(units[2]), resumed.GetProperty("passage").GetProperty("span").GetProperty("onset").GetInt64());

        JsonElement redone = Call(facade, "phrases.edit", $$$"""{"restore": {{{undone.GetProperty("previous")}}}, "expected": {{{undone.GetProperty("edits")}}}}""");
        Assert.Equal(smaller, Anchor(Units(redone)[1]));
        Assert.Single(redone.GetProperty("anchors").EnumerateArray());
        Assert.All(Ideas(facade), idea => Assert.Equal(smaller, Anchor(idea)));
    }

    [Fact]
    public void A_resume_point_in_phrase_focus_is_attached_and_its_passage_goes_with_a_boundary_the_player_moves()
    {
        MusicCoreFacade facade = Opened();
        JsonElement phrase = Units(Call(facade, "phrases"))[2];
        JsonElement selectable = phrase.GetProperty("selectable");
        JsonElement point = Call(facade, "resume.save", $$$"""{"passage": {"span": {{{selectable}}}}, "focused": true, {{{Now}}}}""").GetProperty("saved");
        string id = Anchor(point)!;
        Assert.StartsWith("ph_", id, StringComparison.Ordinal);

        JsonElement moved = Call(facade, "phrases.edit", $$$"""{"move": {"from": {{{Onset(phrase)}}}, "to": {{{Inside(phrase)}}}}}""");

        JsonElement resumed = Call(facade, "resume").GetProperty("resumePoints")[0];
        JsonElement now = Units(moved)[2].GetProperty("selectable");
        Assert.Equal(id, Anchor(resumed));
        Assert.Equal(now.GetProperty("onset").GetInt64(), resumed.GetProperty("passage").GetProperty("span").GetProperty("onset").GetInt64());
        Assert.Equal(now.GetProperty("end").GetInt64(), resumed.GetProperty("passage").GetProperty("span").GetProperty("end").GetInt64());
        Assert.Equal(now.GetProperty("onset").GetInt64(), resumed.GetProperty("at").GetInt64());

        // Out of focus, or on a passage that is no phrase, the point is attached to none.
        Assert.Null(Anchor(Call(facade, "resume.save", $$$"""{"id": "{{{resumed.GetProperty("id").GetString()}}}", "focused": false, {{{Now}}}}""").GetProperty("saved")));
    }

    [Fact]
    public void Anchors_that_no_longer_match_the_phrases_are_told_and_attached_again_as_one_group()
    {
        MusicCoreFacade facade = Opened();
        JsonElement[] units = Units(Call(facade, "phrases"));
        string first = Keep(facade, Onset(units[5]), End(units[5]));
        string second = Keep(facade, Onset(units[6]), End(units[6]));

        // As if the notes had been read again: each anchor a beat later than any phrase found now.
        JsonNode file = JsonNode.Parse(facade.Call(1, "project.save"))!;
        foreach (JsonNode? anchor in file["workspace"]!["phrases"]!["anchors"]!.AsArray())
        {
            anchor!["onset"] = anchor["onset"]!.GetValue<long>() + 480;
        }

        var reopened = new MusicCoreFacade();
        Assert.Equal(1, Json(reopened.Open("piece" + ProjectFile.Extension, Encoding.UTF8.GetBytes(file.ToJsonString()))).GetProperty("piece").GetInt32());
        JsonElement stale = Call(reopened, "phrases");
        Assert.Equal(2, stale.GetProperty("unmatched").GetInt32());
        Assert.All(stale.GetProperty("anchors").EnumerateArray(), anchor => Assert.Equal(JsonValueKind.Null, anchor.GetProperty("phrase").ValueKind));
        Assert.All(Units(stale), unit => Assert.Null(Anchor(unit)));

        JsonElement again = Call(reopened, "phrases.reattach");

        Assert.Equal(2, again.GetProperty("reattached").GetInt32());
        Assert.Equal(0, again.GetProperty("unmatched").GetInt32());
        Assert.Equal(first, Anchor(Units(again)[5]));
        Assert.Equal(second, Anchor(Units(again)[6]));
        Assert.Equal([first, second], Ideas(reopened).Select(Anchor));
        Assert.Equal(0, Call(reopened, "phrases.reattach").GetProperty("reattached").GetInt32());
    }
}