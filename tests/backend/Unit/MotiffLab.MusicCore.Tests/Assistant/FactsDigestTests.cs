using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Assistant;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Projects;

using static MotiffLab.MusicCore.Tests.Assistant.AssistantScores;

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
        Assert.Contains("check.5.graceNotes", digest.Ids);
        Assert.DoesNotContain("check.6.stavesDiffer", digest.Ids);
        Assert.StartsWith("ля минор", Json(Written(digest)).GetProperty("items")[0].GetProperty("text").GetString(), StringComparison.Ordinal);
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