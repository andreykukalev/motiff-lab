namespace MotiffLab.MusicCore.Tests;

/// <summary>The words of music the core's sentences share (Words.Music.cs, W1.8): each written once, the same in every sentence.</summary>
public sealed class WordsMusicTests
{
    [Theory]
    [InlineData(3, 3, "bar 3", "такт 3")]
    [InlineData(1, 8, "bars 1–8", "такты 1–8")]
    public void Bars_are_one_bar_or_a_run(int first, int last, string english, string russian)
    {
        Assert.Equal(english, Words.English.Bars(first, last));
        Assert.Equal(russian, Words.Russian.Bars(first, last));
    }

    [Fact]
    public void Bars_name_a_list_as_written_and_the_other_cases()
    {
        Assert.Equal("bars 2, 6", Words.English.Bars("2, 6", one: false));
        Assert.Equal("такт 9", Words.Russian.Bars("9", one: true));
        Assert.Equal("в тактах 5–6", Words.Russian.InBars(5, 6));
        Assert.Equal("in bar 5", Words.English.InBars(5, 5));
        Assert.Equal("тактов 15–16", Words.Russian.BarsGenitive(15, 16));
        Assert.Equal("такта 15", Words.Russian.BarsGenitive(15, 15));
        Assert.Equal("9–16", Words.Range(9, 16));
        Assert.Equal("9", Words.Range(9, 9));
    }

    [Theory]
    [InlineData("minor 3rd", "a minor 3rd")]
    [InlineData("octave", "an octave")]
    [InlineData("unison", "a unison")]
    [InlineData("eighth", "an eighth")]
    [InlineData("2 octaves", "2 octaves")]
    public void The_english_article_goes_by_the_sound(string noun, string withArticle) =>
        Assert.Equal(withArticle, Words.WithArticle(noun));

    [Fact]
    public void A_key_signature_is_named_in_a_sentence_and_as_a_label()
    {
        Assert.Equal("3 sharps", Words.English.Accidentals(3));
        Assert.Equal("1 бемоль", Words.Russian.Accidentals(-1));
        Assert.Equal("без диезов и бемолей", Words.Russian.Accidentals(0));
        Assert.Equal("без знаков", Words.Russian.AccidentalsLabel(0));
        Assert.Equal("no sharps or flats", Words.English.AccidentalsLabel(0));
        Assert.Equal("2 бемоля", Words.Russian.AccidentalsLabel(-2));
    }

    [Fact]
    public void A_list_is_cut_after_its_most_with_how_many_more()
    {
        string[] items = ["a", "b", "c", "d"];
        Assert.Equal("a, b, c, d", Words.English.Listed(items, 4, ", ", " "));
        Assert.Equal("a, b and 2 more", Words.English.Listed(items, 2, ", ", " "));
        Assert.Equal("a; b; и ещё 2", Words.Russian.Listed(items, 2, "; ", "; "));
        Assert.Equal("1 note", Words.EnglishCount(1, "note", "notes"));
        Assert.Equal("0 notes", Words.EnglishCount(0, "note", "notes"));
    }
}