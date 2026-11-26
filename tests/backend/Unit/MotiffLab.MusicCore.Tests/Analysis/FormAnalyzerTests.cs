using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Analysis.Phrasing;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>
/// The sections of the form on pieces built here (#365): a return of the opening starts a section, even inside a sentence,
/// and the music between two returns is one section; where the opening never comes back, the sentences stay the sections.
/// Für Elise's rondo is in <see cref="PieceStructureTests"/>.
/// </summary>
public class FormAnalyzerTests
{
    private const long Quarter = 480;

    /// <summary>Four bars of 4/4 in C major closing on the tonic, a minim and a minim's rest in the last bar.</summary>
    private static readonly int[] s_a = [60, 62, 64, 65, 67, 65, 64, 62, 60, 64, 67, 64, 60];

    /// <summary>Another four bars, closing on the dominant, so the sentence runs on into what follows.</summary>
    private static readonly int[] s_b = [72, 71, 69, 67, 65, 64, 62, 64, 65, 67, 69, 71, 67];

    /// <summary>A third four bars, closing on the second degree, a half close too.</summary>
    private static readonly int[] s_c = [57, 59, 60, 62, 64, 62, 60, 59, 57, 60, 64, 65, 62];

    [Fact]
    public void A_return_of_the_opening_inside_a_sentence_starts_a_section_a_b_a_c_a()
    {
        // b and c close on a half close, so each runs on into the a after it: the sentences are a, b a and c a.
        (Note[] notes, SentenceMap sentences) = Piece(s_a, s_b, s_a, s_c, s_a);
        Assert.Equal([(1, 4), (5, 12), (13, 20)], sentences.Sentences.Select(sentence => (sentence.FirstBar, sentence.LastBar)));

        IReadOnlyList<DnaSection> form = FormAnalyzer.Sections(notes, MeterMap.Default, sentences);

        Assert.Equal(["A", "B", "A", "C", "A"], form.Select(section => section.Label));
        Assert.Equal([(1, 4), (5, 8), (9, 12), (13, 16), (17, 20)], form.Select(section => (section.FirstBar, section.LastBar)));
        Assert.Equal(["section_3", "section_5"], form[0].SharesWith);
        Assert.Equal(form.Select(section => section.Span.Onset), sentences.Sentences.SelectMany(sentence => sentence.Phrases).Select(phrase => phrase.Span.Onset));
    }

    [Fact]
    public void Music_between_two_returns_is_one_section()
    {
        // b and c between the opening and its return: one episode, a ternary A B A.
        (Note[] notes, SentenceMap sentences) = Piece(s_a, s_b, s_c, s_a);

        IReadOnlyList<DnaSection> form = FormAnalyzer.Sections(notes, MeterMap.Default, sentences);

        Assert.Equal([("A", 1, 4), ("B", 5, 12), ("A", 13, 16)], form.Select(section => (section.Label, section.FirstBar, section.LastBar)));
    }

    [Fact]
    public void An_episode_that_follows_every_refrain_stays_a_section_of_its_own()
    {
        // a b a b a: b comes back after the refrain each time, yet it is not part of it.
        (Note[] notes, SentenceMap sentences) = Piece(s_a, s_b, s_a, s_b, s_a);

        Assert.Equal(["A", "B", "A", "B", "A"], FormAnalyzer.Sections(notes, MeterMap.Default, sentences).Select(section => section.Label));
    }

    [Fact]
    public void A_refrain_heard_again_and_again_is_a_return_each_time()
    {
        // (a a b) three times, then a: each return of a a is a refrain of its own, not one refrain twice as long.
        (Note[] notes, SentenceMap sentences) = Piece(s_a, s_a, s_b, s_a, s_a, s_b, s_a, s_a, s_b, s_a);

        IReadOnlyList<DnaSection> form = FormAnalyzer.Sections(notes, MeterMap.Default, sentences);

        Assert.Equal(["A", "B", "A", "B", "A", "B", "A′"], form.Select(section => section.Label));
        Assert.Equal([(1, 8), (9, 12), (13, 20)], form.Take(3).Select(section => (section.FirstBar, section.LastBar)));
    }

    [Fact]
    public void Where_the_opening_never_comes_back_the_sentences_are_the_sections()
    {
        (Note[] notes, SentenceMap sentences) = Piece(s_a, s_b, s_c);

        IReadOnlyList<DnaSection> form = FormAnalyzer.Sections(notes, MeterMap.Default, sentences);

        Assert.Equal(sentences.Sentences.Select(sentence => sentence.Span), form.Select(section => section.Span));
        Assert.Equal(["A", "B"], form.Select(section => section.Label));
    }

    [Fact]
    public void A_piece_of_one_phrase_is_one_section_and_no_notes_none()
    {
        (Note[] notes, SentenceMap sentences) = Piece(s_a);

        Assert.Equal([("A", 1, 4)], FormAnalyzer.Sections(notes, MeterMap.Default, sentences).Select(section => (section.Label, section.FirstBar, section.LastBar)));
        Assert.Empty(FormAnalyzer.Sections([], MeterMap.Default, sentences));
    }

    /// <summary>The phrases one after another, four bars each from bar 1, and the sentences the piece is read in.</summary>
    private static (Note[] Notes, SentenceMap Sentences) Piece(params int[][] phrases)
    {
        Note[] notes = [.. phrases.SelectMany((phrase, number) => phrase.Select((pitch, i) => (Pitch: pitch, At: (number * 16) + i, Last: i == phrase.Length - 1)))
            .Select((note, i) => new Note
            {
                Id = $"n{i}",
                Pitch = new Pitch(note.Pitch),
                Span = new TickSpan(new Tick(note.At * Quarter), note.Last ? 2 * Quarter : Quarter),
                Velocity = 80,
                Track = 0,
                Attribution = Attribution.Imported,
            })];
        Tick end = new(phrases.Length * 16 * Quarter);
        return (notes, PhraseLevels.Suggest(notes, MeterMap.Default, PhraseMarks.None, end, null).Sentences);
    }
}