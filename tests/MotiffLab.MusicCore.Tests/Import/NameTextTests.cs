using MotiffLab.MusicCore.Import;

namespace MotiffLab.MusicCore.Tests.Import;

/// <summary>Names in one Unicode form, the same on every runtime (the iPad's native build has no ICU), and folded for matching.</summary>
public class NameTextTests
{
    [Theory]
    [InlineData("Für Elise", "Für Elise")]
    [InlineData("Dvořák", "Dvořák")]
    [InlineData("Ёлка й", "Ёлка й")]
    [InlineData("Việt", "Việt")]
    [InlineData("Für Elise", "Für Elise")]
    [InlineData("q\u0308", "q\u0308")]
    public void Letters_with_combining_marks_are_composed(string text, string composed) => Assert.Equal(composed, NameText.Composed(text));

    [Theory]
    [InlineData("Für  Élise!", "fur elise")]
    [InlineData("Für_Elise", "fur elise")]
    [InlineData("ЁЛКА", "елка")]
    [InlineData("Ballade No. 1", "ballade no 1")]
    [InlineData("  ", "")]
    public void Folded_names_ignore_case_marks_and_punctuation(string text, string folded) => Assert.Equal(folded, NameText.Folded(text));
}