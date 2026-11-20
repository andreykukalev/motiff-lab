using System.Text.Json;

using MotiffLab.MusicCore.Assistant;
using MotiffLab.MusicCore.Assistant.Conversation;

using static MotiffLab.MusicCore.Tests.Assistant.AssistantScores;

namespace MotiffLab.MusicCore.Tests.Assistant;

/// <summary>The engine's template lists the facts nearest to the question, not the digest's first four (assistant option A, item 5).</summary>
public class NearestFactsTests
{
    private static readonly AssistantPiece s_russian = Open(FurElise(), words: Words.Russian);

    private static readonly AssistantPiece s_english = Open(FurElise());

    private static IReadOnlyList<string> Cited(JsonElement context, Words words, string? question) =>
        AnswerCheck.Cited(AnswerCheck.Template(context, words, "noCitation", question).Text);

    [Fact]
    public void Bars_named_in_the_question_bring_their_facts_first_the_asked_kind_before_the_rest()
    {
        JsonElement russian = AssistantContext.OfPiece(s_russian);
        JsonElement english = AssistantContext.OfPiece(s_english);

        Assert.Equal(["chords.section_3", "phrase.6", "new.24", "form.section_3"], Cited(russian, Words.Russian, "Какие аккорды в такте 30?"));
        Assert.Equal(["chords.section_3", "phrase.6", "new.24", "form.section_3"], Cited(english, Words.English, "Which chords are in bar 30?"));
        Assert.Equal(["density.32", "density.34", "density.33", "density.35"], Cited(russian, Words.Russian, "Почему такты 32–35 такие трудные?"));
    }

    [Fact]
    public void A_section_named_by_its_letter_brings_the_section_and_what_lies_in_it()
    {
        JsonElement context = AssistantContext.OfPiece(s_english);

        IReadOnlyList<string> section = Cited(context, Words.English, "What happens in section B?");

        Assert.Equal("form.section_2", section[0]);
        Assert.Equal(section, Cited(AssistantContext.OfPiece(s_russian), Words.Russian, "Что происходит в части B?"));
        Assert.Equal("form.section_4", Cited(context, Words.English, "Is part A' the same as A?")[0]);
    }

    [Fact]
    public void Words_of_a_kind_bring_one_fact_of_each_kind_asked()
    {
        JsonElement context = AssistantContext.OfPiece(s_russian);

        Assert.Equal(["form.section_1", "piece.tempo.profile", "range.L", "density.32"], Cited(context, Words.Russian, "Порекомендуй, как разучить пьесу"));
        Assert.Equal(["form.section_1", "piece.tempo.profile", "range.L", "density.32"], Cited(AssistantContext.OfPiece(s_english), Words.English, "How should I practise this piece?"));
        Assert.Equal("piece.tempo.profile", Cited(context, Words.Russian, "В каком темпе играть?")[0]);
    }

    [Fact]
    public void A_passage_answers_with_its_own_facts_of_the_kind_asked_and_a_question_without_cues_with_the_digest_in_order()
    {
        JsonElement passage = AssistantContext.OfRange(s_russian, 1, 8);
        JsonElement piece = AssistantContext.OfPiece(s_russian);

        IReadOnlyList<string> chords = Cited(passage, Words.Russian, "Какие здесь аккорды?");
        Assert.Equal("harmony.motion", chords[0]);
        Assert.All(chords.Skip(1), id => Assert.StartsWith("chord.", id, StringComparison.Ordinal));
        Assert.Equal(["key.1", "summary", "form.section_1", "piece.tempo.profile"], Cited(piece, Words.Russian, "Расскажи что-нибудь"));
        Assert.Equal(Cited(piece, Words.Russian, null), Cited(piece, Words.Russian, "Расскажи что-нибудь"));
        Assert.Equal(Cited(passage, Words.Russian, "Какие здесь аккорды?"), Cited(passage, Words.Russian, "Какие здесь аккорды?"));
    }

    [Fact]
    public void A_word_that_only_starts_like_a_kind_or_a_small_letter_after_part_asks_for_nothing()
    {
        JsonElement russian = AssistantContext.OfPiece(s_russian);
        JsonElement english = AssistantContext.OfPiece(s_english);

        // «часто» is no «часть», "particular" no "part": the question has no cue, so the digest's order stands.
        Assert.Equal(Cited(russian, Words.Russian, null), Cited(russian, Words.Russian, "Как часто это играть?"));
        Assert.Equal(Cited(english, Words.English, null), Cited(english, Words.English, "What is particular here?"));

        // "part a bit" names no section A: it asks about the form only, as "part" alone does.
        Assert.Equal(Cited(english, Words.English, "Is this part odd?"), Cited(english, Words.English, "Is this part a bit odd?"));
        Assert.NotEqual(Cited(english, Words.English, "Is this part odd?"), Cited(english, Words.English, "Is this part A odd?"));
    }
}