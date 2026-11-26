using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Analysis.Phrasing;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>
/// The sections of the form on pieces built here (#365): a return of the opening starts a section, even inside a sentence,
/// and the music between two returns keeps its own sentences; where the opening never comes back, the sentences stay the
/// sections. Für Elise's rondo is in <see cref="PieceStructureTests"/>.
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

    /// <summary>b a fifth lower, closing on the tonic: b transposed.</summary>
    private static readonly int[] s_bLower = [.. s_b.Select(pitch => pitch - 7)];

    /// <summary>Three more four bars of material of their own, each closing on the tonic, so each is a sentence.</summary>
    private static readonly int[] s_d = [76, 74, 72, 74, 76, 79, 77, 76, 74, 72, 71, 74, 72];

    private static readonly int[] s_e = [55, 57, 55, 53, 52, 53, 55, 60, 59, 57, 55, 59, 60];

    private static readonly int[] s_f = [48, 50, 52, 48, 53, 52, 50, 47, 48, 55, 52, 55, 48];

    /// <summary>a's first two bars, then two bars of its own: a changed a.</summary>
    private static readonly int[] s_aChanged = [.. s_a[..8], 69, 72, 71, 74, 72];

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
    public void An_episode_keeps_its_own_sentences_as_sections_a_b_c_d_a()
    {
        // d, e and f between the opening and its return each close on the tonic: three sentences, three sections (f reads
        // as one sentence with the return it runs into, yet closes, so it does not lead back into it).
        (Note[] notes, SentenceMap sentences) = Piece(s_a, s_d, s_e, s_f, s_a);
        Assert.All(sentences.Closes.Skip(1).Take(3), close => Assert.Equal(CloseKind.Full, close.Kind));

        IReadOnlyList<DnaSection> form = FormAnalyzer.Sections(notes, MeterMap.Default, sentences);

        Assert.Equal([("A", 1, 4), ("B", 5, 8), ("C", 9, 12), ("D", 13, 16), ("A", 17, 20)], form.Select(section => (section.Label, section.FirstBar, section.LastBar)));
    }

    [Fact]
    public void The_last_sentence_of_an_episode_that_does_not_close_is_its_lead_in()
    {
        // d closes on the tonic, b on a half close that runs on into the return: b leads back, so d b is one episode.
        (Note[] notes, SentenceMap sentences) = Piece(s_a, s_d, s_b, s_a);
        Assert.Equal([(1, 4), (5, 8), (9, 16)], sentences.Sentences.Select(sentence => (sentence.FirstBar, sentence.LastBar)));

        IReadOnlyList<DnaSection> form = FormAnalyzer.Sections(notes, MeterMap.Default, sentences);

        Assert.Equal([("A", 1, 4), ("B", 5, 12), ("A", 13, 16)], form.Select(section => (section.Label, section.FirstBar, section.LastBar)));
    }

    [Fact]
    public void A_sonata_rondo_reads_a_b_a_c_a_b_prime_a()
    {
        // The first episode comes back a fifth lower after the second: the same letter with a prime, the refrain without.
        (Note[] notes, SentenceMap sentences) = Piece(s_a, s_b, s_a, s_c, s_a, s_bLower, s_a);

        IReadOnlyList<DnaSection> form = FormAnalyzer.Sections(notes, MeterMap.Default, sentences);

        Assert.Equal(["A", "B", "A", "C", "A", "B′", "A"], form.Select(section => section.Label));
        Assert.Equal([(1, 4), (5, 8), (9, 12), (13, 16), (17, 20), (21, 24), (25, 28)], form.Select(section => (section.FirstBar, section.LastBar)));
        Assert.Equal(["section_6"], form[1].SharesWith);
    }

    [Fact]
    public void An_episode_that_follows_every_refrain_stays_a_section_of_its_own()
    {
        // a b a b a: b comes back after the refrain each time, yet it is not part of it.
        (Note[] notes, SentenceMap sentences) = Piece(s_a, s_b, s_a, s_b, s_a);

        Assert.Equal(["A", "B", "A", "B", "A"], FormAnalyzer.Sections(notes, MeterMap.Default, sentences).Select(section => section.Label));
    }

    [Fact]
    public void A_return_is_unchanged_only_against_the_first_section_with_its_letter()
    {
        // The changed a comes back twice: the second time it repeats the first changed a exactly, yet half its bars are
        // still new against the first A, so it keeps its prime.
        (Note[] notes, SentenceMap sentences) = Piece(s_a, s_b, s_aChanged, s_c, s_aChanged);

        IReadOnlyList<DnaSection> form = FormAnalyzer.Sections(notes, MeterMap.Default, sentences);

        Assert.Equal([("A", 1, 4), ("B", 5, 8), ("A′", 9, 12), ("C", 13, 16), ("A″", 17, 20)], form.Select(section => (section.Label, section.FirstBar, section.LastBar)));
    }

    [Fact]
    public void A_refrain_heard_again_and_again_is_a_return_each_time()
    {
        // (a a b) three times, then a: each return of a a is a refrain of its own, not one refrain twice as long; the last
        // brings back only its first a, which is no change.
        (Note[] notes, SentenceMap sentences) = Piece(s_a, s_a, s_b, s_a, s_a, s_b, s_a, s_a, s_b, s_a);

        IReadOnlyList<DnaSection> form = FormAnalyzer.Sections(notes, MeterMap.Default, sentences);

        Assert.Equal(["A", "B", "A", "B", "A", "B", "A"], form.Select(section => section.Label));
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

    /// <summary>The rondo a b a c a d a, for the cards of its ideas.</summary>
    internal static (Note[] Notes, PhraseLevels Levels) Rondo() => Built(s_a, s_b, s_a, s_c, s_a, s_d, s_a);

    /// <summary>The phrases one after another, four bars each from bar 1, and the sentences the piece is read in.</summary>
    private static (Note[] Notes, SentenceMap Sentences) Piece(params int[][] phrases)
    {
        (Note[] notes, PhraseLevels levels) = Built(phrases);
        return (notes, levels.Sentences);
    }

    /// <summary>The phrases one after another, four bars each from bar 1, and the levels the piece is read in.</summary>
    private static (Note[] Notes, PhraseLevels Levels) Built(params int[][] phrases)
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
        return (notes, PhraseLevels.Suggest(notes, MeterMap.Default, PhraseMarks.None, end, null));
    }
}