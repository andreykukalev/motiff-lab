using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>Where new ids come from (0026 S3, #317): random on the iPad, numbered for tests and goldens.</summary>
public class IdSourceTests
{
    [Fact]
    public void Numbered_ids_are_one_more_than_the_highest_of_their_prefix_in_four_digits_at_least()
    {
        Assert.Equal("o_0001", IdSource.Numbered.Next("o_", []));
        Assert.Equal("o_0004", IdSource.Numbered.Next("o_", ["o_0001", "o_0003", "r_0009", "o_x", "o_3f9c4e1a0b7d4c3e8f6a5b4c3d2e1f00"]));
        Assert.Equal("pl_10000", IdSource.Numbered.Next("pl_", ["pl_9999"]));
        // Notes and versions as builds before 1.6 wrote them.
        Assert.Equal("n_3", IdSource.Numbered.Next("n_", ["n_1", "n_2"], digits: 1));
    }

    [Fact]
    public void Ids_of_a_kind_are_numbered_from_the_stem_itself_or_random_after_it()
    {
        Assert.Equal("src_rec", IdSource.Numbered.Stemmed("src_rec", new HashSet<string>()));
        Assert.Equal("src_rec_3", IdSource.Numbered.Stemmed("src_rec", new HashSet<string> { "src_rec", "src_rec_2" }));
        Assert.Matches("^src_rec_[0-9a-f]{32}$", IdSource.Random.Stemmed("src_rec", new HashSet<string>()));
    }

    [Fact]
    public void Random_ids_are_the_prefix_and_32_hex_digits_never_one_taken()
    {
        string[] ids = [.. Enumerable.Range(0, 100).Select(_ => IdSource.Random.Next("b_", ["b_0001"]))];

        Assert.All(ids, id => Assert.Matches("^b_[0-9a-f]{32}$", id));
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }
}