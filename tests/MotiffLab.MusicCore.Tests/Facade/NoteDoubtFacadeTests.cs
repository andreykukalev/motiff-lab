using System.Text.Json;

using MotiffLab.MusicCore.Facade;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>Through the facade (contract 11): doubt about recognised notes reaches chords and facts, and goes when checked (#352).</summary>
public class NoteDoubtFacadeTests
{
    private static byte[] Fixture(params string[] path) => File.ReadAllBytes(Path.Combine([AppContext.BaseDirectory, .. path]));

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    private static JsonElement Call(MusicCoreFacade facade, string name, string? request = null)
    {
        JsonElement answer = Json(facade.Call(1, name, request));
        Assert.False(answer.TryGetProperty("error", out _), answer.GetRawText());
        return answer;
    }

    /// <summary>Transkun's Für Elise heard in two parts, so the seam between them is always flagged (0016).</summary>
    private static MusicCoreFacade Heard(string language = "en")
    {
        string midi = Convert.ToBase64String(Fixture("Import", "Transcription", "Fixtures", "transkun-fur-elise.mid"));
        string request = $$$"""{"id": "p_heard", "language": "{{{language}}}", "now": "2026-11-11T12:00:00Z", "transcription": {"engine": "transkun", "milliseconds": 130000, "parts": [{"index": 0, "midi": "{{{midi}}}"}, {"index": 1, "midi": "{{{midi}}}"}]}}""";
        var facade = new MusicCoreFacade();
        JsonElement opened = Json(facade.Open("recital.m4a", [], request));
        Assert.False(opened.TryGetProperty("error", out _), opened.GetRawText());
        return facade;
    }

    private static IEnumerable<JsonElement> Facts(JsonElement understood) =>
        new[] { "motif", "structure", "harmony" }
            .Where(part => understood.TryGetProperty(part, out _))
            .SelectMany(part => understood.GetProperty(part).TryGetProperty("facts", out JsonElement facts) ? facts.EnumerateArray() : Enumerable.Empty<JsonElement>());

    private static IEnumerable<JsonElement> Doubted(JsonElement understood) =>
        Facts(understood).Concat(understood.TryGetProperty("harmony", out JsonElement harmony) ? harmony.GetProperty("chords").EnumerateArray() : [])
            .Where(item => item.TryGetProperty("doubt", out _));

    /// <summary>The bar numbers a doubt names: "… in bars 5, 7 and 9."</summary>
    private static int[] BarsNamed(string doubt) =>
        [.. System.Text.RegularExpressions.Regex.Matches(doubt, "[0-9]+").Select(match => int.Parse(match.Value, System.Globalization.CultureInfo.InvariantCulture))];

    [Fact]
    public void A_flagged_bar_of_a_recording_makes_its_chords_and_facts_uncertain_until_it_is_checked()
    {
        MusicCoreFacade facade = Heard();
        JsonElement check = Call(facade, "check");
        int bar = check.GetProperty("flags").EnumerateArray().First(flag => !flag.GetProperty("resolved").GetBoolean()).GetProperty("bar").GetInt32();

        Call(facade, "select", $$$"""{"bars": {"first": {{{bar}}}, "last": {{{bar + 1}}}}}""");
        JsonElement understood = Call(facade, "understand");

        JsonElement[] doubted = [.. Doubted(understood)];
        Assert.NotEmpty(doubted);
        Assert.All(doubted, item =>
        {
            // An interpretation stays a reading; everything else turns uncertain.
            Assert.Contains(item.GetProperty("certainty").GetString(), new[] { "uncertain", "interpretation" });
            Assert.StartsWith("Rests on recognised notes still to check", item.GetProperty("doubt").GetString());
        });
        Assert.Contains(doubted, item => BarsNamed(item.GetProperty("doubt").GetString()!).Contains(bar));

        // Looks right: the bar's flags are resolved, and what rested on its notes no longer says so.
        Call(facade, "check.resolve", $$"""{"bar": {{bar}}}""");
        JsonElement again = Call(facade, "understand");
        Assert.All(Doubted(again), item => Assert.DoesNotContain(bar, BarsNamed(item.GetProperty("doubt").GetString()!)));
        Assert.NotEqual(understood.GetRawText(), again.GetRawText());
    }

    [Fact]
    public void Resolving_every_flag_clears_every_doubt_and_the_dna_with_it()
    {
        MusicCoreFacade facade = Heard();
        Call(facade, "select", """{"bars": {"first": 1, "last": 8}}""");
        bool doubtedDna = Call(facade, "dna").GetProperty("harmony").GetProperty("bars").EnumerateArray().Any(chord => chord.TryGetProperty("doubt", out _));

        foreach (int bar in Call(facade, "check").GetProperty("flags").EnumerateArray().Select(flag => flag.GetProperty("bar").GetInt32()).Distinct())
        {
            Call(facade, "check.resolve", $$"""{"bar": {{bar}}}""");
        }

        Assert.Empty(Doubted(Call(facade, "understand")));
        JsonElement dna = Call(facade, "dna");
        Assert.DoesNotContain(dna.GetProperty("harmony").GetProperty("bars").EnumerateArray(), chord => chord.TryGetProperty("doubt", out _));
        Assert.DoesNotContain(dna.GetProperty("keys").GetProperty("spans").EnumerateArray(), key => key.TryGetProperty("doubt", out _));
        // The seams flag at least one bar, whose chord in the map was marked before it was checked.
        Assert.True(doubtedDna);
    }

    [Fact]
    public void The_check_answers_the_bars_with_uncertain_lengths_apart_from_the_flags()
    {
        MusicCoreFacade facade = Heard("ru");

        JsonElement lengths = Call(facade, "check").GetProperty("lengths");

        Assert.NotEmpty(lengths.EnumerateArray());
        Assert.All(lengths.EnumerateArray(), bar =>
        {
            Assert.NotEmpty(bar.GetProperty("noteIds").EnumerateArray());
            Assert.All(bar.GetProperty("reasons").EnumerateArray(), reason => Assert.Contains(reason.GetString(), new[] { "pedal", "long" }));
            Assert.Equal(bar.GetProperty("reasons").GetArrayLength(), bar.GetProperty("texts").GetArrayLength());
            Assert.Matches("[а-я]", bar.GetProperty("names")[0].GetString()!);
        });
    }

    [Fact]
    public void Imported_notes_answer_without_doubt_or_uncertain_lengths()
    {
        var facade = new MusicCoreFacade();
        Assert.False(Json(facade.Open("fur-elise.musicxml", Fixture("Experiments", "Fixtures", "fur-elise-original.musicxml"))).TryGetProperty("error", out _));
        Call(facade, "select", """{"bars": {"first": 1, "last": 8}}""");

        Assert.Empty(Doubted(Call(facade, "understand")));
        Assert.DoesNotContain("\"doubt\"", Call(facade, "dna").GetRawText(), StringComparison.Ordinal);
        Assert.Empty(Call(facade, "check").GetProperty("lengths").EnumerateArray());
    }
}