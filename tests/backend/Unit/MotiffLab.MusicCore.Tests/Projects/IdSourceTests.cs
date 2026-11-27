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
    }

    [Fact]
    public void Random_ids_are_the_prefix_and_32_hex_digits_never_one_taken()
    {
        string[] ids = [.. Enumerable.Range(0, 100).Select(_ => IdSource.Random.Next("b_", ["b_0001"]))];

        Assert.All(ids, id => Assert.Matches("^b_[0-9a-f]{32}$", id));
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }
}