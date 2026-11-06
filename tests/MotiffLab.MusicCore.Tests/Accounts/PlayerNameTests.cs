using MotiffLab.MusicCore.Accounts;

namespace MotiffLab.MusicCore.Tests.Accounts;

public class PlayerNameTests
{
    [Fact]
    public void The_name_comes_first_then_the_email()
    {
        Assert.Equal("Andrei", PlayerName.Of([("email", "a@example.com"), ("name", "Andrei")]));
        Assert.Equal("a@example.com", PlayerName.Of([("email", "a@example.com")]));
        Assert.Equal("a@example.com", PlayerName.Of([("emails", "a@example.com")]));
    }

    [Fact]
    public void External_IDs_unknown_is_no_name()
    {
        // A player who gave no name at sign-up is "unknown" in the token (Andrei, 2026-11-06).
        Assert.Equal("a@example.com", PlayerName.Of([("name", "unknown"), ("email", "a@example.com")], "unknown"));
        Assert.Equal("Ann Lee", PlayerName.Of([("name", "Unknown"), ("given_name", "Ann"), ("family_name", "Lee")]));
        Assert.Equal("", PlayerName.Of([("name", " unknown ")], "unknown"));
        Assert.Equal("andrei", PlayerName.Of([("name", "")], "andrei"));
    }
}