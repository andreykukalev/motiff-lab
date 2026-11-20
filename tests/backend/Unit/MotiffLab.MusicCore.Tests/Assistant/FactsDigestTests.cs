using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Analysis.Phrasing;
using MotiffLab.MusicCore.Assistant;
using MotiffLab.MusicCore.Assistant.Conversation;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Assistant.AssistantScores;
using static MotiffLab.MusicCore.Tests.Golden.TextGoldens;

namespace MotiffLab.MusicCore.Tests.Assistant;

/// <summary>The facts digest a model's answer may cite (the AI companion study, section 4.4).</summary>
public class FactsDigestTests
{
    private static string Written(FactsDigest digest)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            digest.Write(json);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    [Fact]
    public void A_range_digest_lists_facts_chords_flags_and_hands_each_with_one_id()
    {
        ImportedPiece imported = FurElise();
        AssistantPiece piece = Open(imported, [new CheckFlag(3, CheckSource.Scan, CheckReason.BarTooLong, false)]);

        FactsDigest digest = FactsDigest.OfRange(piece, 1, 8);

        Assert.Equal(digest.Ids.Count, digest.Ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("check.3.barTooLong", digest.Ids);
        Assert.Contains("range.R", digest.Ids);
        Assert.Contains(digest.Ids, id => id.StartsWith("chord.", StringComparison.Ordinal));
        Assert.InRange(digest.Ids.Count(id => id.Contains('.', StringComparison.Ordinal) && !id.StartsWith("chord.", StringComparison.Ordinal) && !id.StartsWith("check.", StringComparison.Ordinal) && !id.StartsWith("range.", StringComparison.Ordinal)), 1, FactsDigest.MaxFacts);
        Assert.Equal("3/8", digest.Header.Single(field => field.Name == "meter").Value);
    }

    [Fact]
    public void The_digest_is_deterministic_and_names_notes_only_by_address()
    {
        string first = Written(FactsDigest.OfRange(Open(FurElise()), 1, 8));

        Assert.Equal(first, Written(FactsDigest.OfRange(Open(FurElise()), 1, 8)));
        Assert.DoesNotContain("n_0", first, StringComparison.Ordinal);
        JsonElement root = Json(first);
        Assert.Equal(root.GetProperty("ids").EnumerateArray().Select(id => id.GetString()), root.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()));
        Assert.All(
            root.GetProperty("items").EnumerateArray().Where(item => item.TryGetProperty("places", out _)).SelectMany(item => item.GetProperty("places").EnumerateArray()).SelectMany(place => place.GetProperty("notes").EnumerateArray()),
            note => Assert.Matches(NoteAddress.Pattern, note.GetString()!));
    }

    [Fact]
    public void Only_ids_the_digest_lists_may_be_cited()
    {
        FactsDigest digest = FactsDigest.OfRange(Open(FurElise()), 1, 4);
        string listed = digest.Ids[0];

        Assert.Empty(digest.Unknown([listed]));
        Assert.Equal(["melody.invented", "chord.99.1"], digest.Unknown([listed, "melody.invented", "chord.99.1", "melody.invented"]));
    }

    [Fact]
    public void The_piece_digest_lists_keys_form_closes_and_open_flags()
    {
        AssistantPiece piece = Open(FurElise(), [new CheckFlag(5, CheckSource.Scan, CheckReason.GraceNotes, false), new CheckFlag(6, CheckSource.Scan, CheckReason.StavesDiffer, true)], Words.Russian);

        FactsDigest digest = FactsDigest.OfPiece(piece);

        Assert.Equal("piece", digest.Scope);
        Assert.Contains("key.1", digest.Ids);
        Assert.Contains("form.section_1", digest.Ids);
        Assert.Contains("cadence.8", digest.Ids);
        Assert.Contains("check.5.graceNotes", digest.Ids);
        Assert.DoesNotContain("check.6.stavesDiffer", digest.Ids);
        Assert.StartsWith("ля минор", Json(Written(digest)).GetProperty("items")[0].GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_piece_digest_cites_the_computed_facts_each_with_its_bars()
    {
        AssistantPiece piece = Open(FurElise(), words: Words.Russian);

        JsonElement items = Json(Written(FactsDigest.OfPiece(piece))).GetProperty("items");
        string Text(string id) => items.EnumerateArray().Single(item => item.GetProperty("id").GetString() == id).GetProperty("text").GetString()!;
        string[] ids = [.. items.EnumerateArray().Select(item => item.GetProperty("id").GetString()!)];

        // The summary line has an id now, and is no plain field any more.
        Assert.StartsWith("Форма A B C A′", Text("summary"), StringComparison.Ordinal);
        Assert.False(Json(Written(FactsDigest.OfPiece(piece))).TryGetProperty("summary", out _));
        Assert.Equal("A′, такты 78–105, общий материал с A (такты 0–8)", Text("form.section_4"));
        Assert.Equal("Темп: четверть 72 BPM на всём протяжении.", Text("piece.tempo.profile"));
        Assert.Equal(["range.L", "range.R"], ids.Where(id => id.StartsWith("range.", StringComparison.Ordinal)));
        Assert.Equal("Такт 32, самый плотный: 7 нот на долю", Text("density.32"));
        Assert.Equal(FactsDigest.MaxDenseBars, ids.Count(id => id.StartsWith("density.", StringComparison.Ordinal)));
        Assert.Equal("новый материал: такты 24–33", Text("new.24"));
        Assert.Equal(piece.Structure.Levels.Phrases.Phrases.Count, ids.Count(id => id.StartsWith("phrase.", StringComparison.Ordinal)));
        Assert.Equal("Фраза 2, такты 4–8, начало: такт 4, доля 3", Text("phrase.2"));
        Assert.Equal("Предложение 1, такты 0–8: фразы 1–2", Text("sentence.1"));
        Assert.Equal("Аккорды части A (такты 0–8), тактов у каждого: Am (i) 4, E (V) 2", Text("chords.section_1"));
        Assert.All(
            items.EnumerateArray().Where(item => item.GetProperty("kind").GetString() is not ("summary" or "range" or "tempo")),
            item => Assert.True(item.TryGetProperty("bars", out _), item.GetProperty("id").GetString()));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public void The_piece_digest_of_fur_elise_is_its_golden_and_stays_within_the_turn(string language)
    {
        FactsDigest digest = FactsDigest.OfPiece(Open(FurElise(), words: Words.For(language)));

        JsonElement items = Json(Written(digest)).GetProperty("items");
        AssertTextGolden("Assistant", $"fur-elise-piece-digest.{language}.txt", string.Concat(items.EnumerateArray().Select(item =>
            $"{item.GetProperty("id").GetString()} | {item.GetProperty("kind").GetString()} | {item.GetProperty("certainty").GetString()} | {item.GetProperty("text").GetString()}"
            + (item.TryGetProperty("bars", out JsonElement bars) ? $" | {bars.GetProperty("from").GetInt32()}–{bars.GetProperty("to").GetInt32()}" : "") + "\n")));

        // About 2.8 thousand characters before (some 1–1.5 thousand tokens); the facts keep it near 3–5 thousand tokens.
        string text = JsonSerializer.Serialize(Json(Written(digest)), new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        Assert.InRange(text.Length, 6_000, 11_000);
        Assert.DoesNotContain("Elise", text, StringComparison.OrdinalIgnoreCase);

        // A question on the longest passage that also reads the piece's digest still fits one turn.
        AssistantPiece piece = Open(FurElise(), words: Words.For(language));
        ToolCall call = new("c1", "piece.describe", Json("{}"));
        AssistantSession session = AssistantSession.Start(AssistantRole.Proposer).Ask("Как учить?").Called([call]);
        session = session.Resulted(TurnRunner.Run(piece, AssistantRole.Proposer, [call], 0));
        Assert.InRange(session.Request(language, AssistantContext.OfRange(piece, 1, 32)).ToJson().Length, 1, TurnRequest.MaxBytes);
    }

    [Fact]
    public void A_phrase_the_player_started_says_so_and_the_digest_is_numbered_as_the_score()
    {
        ImportedPiece imported = FurElise();
        AssistantPiece found = Open(imported);
        PhraseLevels levels = found.Structure.Levels;
        Tick start = imported.Notes.Where(note => imported.Meter.BarAt(note.Span.Onset).Number == 12).Min(note => note.Span.Onset);
        PhraseLevels edited = levels.With(LevelEdits.None.With(PhraseLevel.Phrase, levels.Phrases.StartAt(start)));
        var piece = new AssistantPiece(imported, imported.Notes, [], Words.English, PieceStructure.Of(imported.Notes, imported.Meter, edited));

        JsonElement items = Json(Written(FactsDigest.OfPiece(piece))).GetProperty("items");
        JsonElement own = items.EnumerateArray().Single(item => item.GetProperty("kind").GetString() == "phrase" && item.GetProperty("bars").GetProperty("from").GetInt32() == 12);

        Assert.Equal("player", own.GetProperty("certainty").GetString());
        Assert.Equal(edited.Phrases.Phrases.Count, items.EnumerateArray().Count(item => item.GetProperty("kind").GetString() == "phrase"));
        Assert.Equal(levels.Phrases.Phrases.Count + 1, edited.Phrases.Phrases.Count);
    }

    [Fact]
    public void A_one_hand_digest_reads_only_that_hand()
    {
        FactsDigest digest = FactsDigest.OfRange(Open(FurElise()), 1, 8, MotiffLab.MusicCore.Events.Hand.Left);

        Assert.Contains("range.L", digest.Ids);
        Assert.DoesNotContain("range.R", digest.Ids);
        Assert.Contains("\"hand\":\"left\"", Written(digest), StringComparison.Ordinal);
    }
}