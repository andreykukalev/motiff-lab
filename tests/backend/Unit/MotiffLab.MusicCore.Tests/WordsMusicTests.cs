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
        Assert.Equal("в тактах 1–2, 5 и 9", Words.Russian.InBars("1–2, 5 и 9", one: false));
        Assert.Equal("тактах 5–6, 9", Words.Russian.BarsPrepositional("5–6, 9", one: false));
        Assert.Equal("bar 7", Words.English.BarsPrepositional("7", one: true));
        Assert.Equal("тактов 15–16", Words.Russian.BarsGenitive(15, 16));
        Assert.Equal("такта 15", Words.Russian.BarsGenitive(15, 15));
        Assert.Equal("9–16", Words.Range(9, 16));
        Assert.Equal("9", Words.Range(9, 9));
        Assert.Equal([(1, 2), (5, 5), (7, 9)], Words.Runs([1, 2, 5, 7, 8, 9]));
        Assert.Empty(Words.Runs([]));
    }

    [Theory]
    [InlineData(0, "unison")]
    [InlineData(-3, "minor 3rd")]
    [InlineData(6, "tritone")]
    [InlineData(12, "octave")]
    [InlineData(14, "octave and a major 2nd")]
    [InlineData(24, "2 octaves")]
    public void Intervals_are_named_by_size(int semitones, string name) =>
        Assert.Equal(name, Words.English.IntervalName(semitones));

    [Theory]
    [InlineData(3, "малая терция", "малую терцию")]
    [InlineData(7, "чистая квинта", "чистую квинту")]
    [InlineData(6, "тритон", "тритон")]
    [InlineData(12, "октава", "октаву")]
    [InlineData(14, "октава и большая секунда", "октаву и большую секунду")]
    [InlineData(24, "2 октавы", "2 октавы")]
    public void Intervals_are_named_in_russian(int semitones, string name, string by)
    {
        Assert.Equal(name, Words.Russian.IntervalName(semitones));
        Assert.Equal(by, Words.Russian.IntervalBy(-semitones));
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
        Assert.Equal("1 диез", Words.Russian.Accidentals(1));
        Assert.Equal("5 диезов", Words.Russian.Accidentals(5));
        Assert.Equal("6 flats", Words.English.Accidentals(-6));
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