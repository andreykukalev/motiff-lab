using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>Three levels from one analysis (decision record 0018, section 3; preparation item 7): nested, edited per level, and a selection that follows the level.</summary>
public class PhraseLevelsTests
{
    private const long Quarter = 480;
    private const long Bar = 4 * Quarter;

    private static readonly KeySignature s_cMajor = new(Tick.Zero, 0, KeyMode.Major);

    private static Note Note(string id, int pitch, long onset, long duration = Quarter) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = 0,
        Attribution = Attribution.Imported,
    };

    /// <summary>
    /// Sixteen bars in C major as four phrases of four bars, each ending a beat early so a rest divides them: the first
    /// ends on G (a half close), the second on C (a full close), the third on G and the fourth on C, so there are two sentences.
    /// </summary>
    private static Note[] FourPhrases()
    {
        var notes = new List<Note>();
        for (int phrase = 0; phrase < 4; phrase++)
        {
            int close = phrase % 2 == 0 ? 67 : 60;
            for (int i = 0; i < 15; i++)
            {
                int pitch = i == 14 ? close : 48 + (phrase * 14) + i;
                notes.Add(Note($"p{phrase}n{i}", pitch, (phrase * 4 * Bar) + (i * Quarter)));
            }
        }

        return [.. notes];
    }

    private static PhraseLevels Levels(LevelEdits? edits = null) =>
        PhraseLevels.Suggest(FourPhrases(), MeterMap.Default, PhraseMarks.None, new Tick(16 * Bar), s_cMajor, Words.English, edits);

    private static (int, int)[] Bars(PhraseLevels levels, PhraseLevel level) => [.. levels.Units(level).Select(unit => (unit.FirstBar, unit.LastBar))];

    [Fact]
    public void The_levels_nest_and_a_unit_knows_its_parent()
    {
        PhraseLevels levels = Levels();

        Assert.Equal([(1, 4), (5, 8), (9, 12), (13, 16)], Bars(levels, PhraseLevel.Phrase));
        Assert.Equal([(1, 8), (9, 16)], Bars(levels, PhraseLevel.Sentence));
        Assert.Equal(Bars(levels, PhraseLevel.Phrase), Bars(levels, PhraseLevel.Short));

        Tick[] sentenceStarts = [.. levels.Units(PhraseLevel.Sentence).Select(unit => unit.Span.Onset)];
        Tick[] phraseStarts = [.. levels.Units(PhraseLevel.Phrase).Select(unit => unit.Span.Onset)];
        Tick[] shortStarts = [.. levels.Units(PhraseLevel.Short).Select(unit => unit.Span.Onset)];
        Assert.All(sentenceStarts, start => Assert.Contains(start, phraseStarts));
        Assert.All(phraseStarts, start => Assert.Contains(start, shortStarts));

        PhraseUnit third = levels.Units(PhraseLevel.Phrase)[2];
        Assert.Equal(2, levels.Parent(third)!.Number);
        Assert.Equal(PhraseLevel.Sentence, levels.Parent(third)!.Level);
        Assert.Null(levels.Parent(levels.Units(PhraseLevel.Sentence)[0]));
        Assert.Equal(PhraseLevel.Phrase, levels.Parent(levels.Units(PhraseLevel.Short)[1])!.Level);
        Assert.Equal("the start of the piece", levels.Units(PhraseLevel.Short)[0].Description);
        Assert.Equal("after a rest of a beat or more", levels.Units(PhraseLevel.Phrase)[1].Description);
        Assert.Equal(["rest"], levels.Units(PhraseLevel.Phrase)[1].Evidence);
        Assert.StartsWith("Phrase 1 ends on a half close", levels.Units(PhraseLevel.Sentence)[0].Description);
    }

    [Fact]
    public void A_selection_moves_to_the_enclosing_or_the_contained_unit_when_the_level_changes()
    {
        PhraseLevels levels = Levels();
        TickSpan phrase2 = levels.Units(PhraseLevel.Phrase)[1].Span;

        Assert.Equal(levels.Units(PhraseLevel.Sentence)[0].Span, levels.Map(phrase2, PhraseLevel.Sentence).Span);
        Assert.Equal(phrase2, levels.Map(phrase2, PhraseLevel.Short).Span);
        Assert.Equal(levels.Units(PhraseLevel.Phrase)[0].Span, levels.Map(levels.Units(PhraseLevel.Sentence)[0].Span, PhraseLevel.Phrase).Span);

        // A range across two phrases: at the sentence level the one enclosing it; at the phrase level the one it starts in.
        TickSpan across = TickSpan.FromBounds(new Tick(2 * Bar), new Tick(6 * Bar));
        Assert.Equal(1, levels.Map(across, PhraseLevel.Sentence).Number);
        Assert.Equal(1, levels.Map(across, PhraseLevel.Phrase).Number);
        // A phrase of four bars is also one short unit, so the finest level that has it names it.
        Assert.Equal(PhraseLevel.Short, levels.LevelOf(phrase2));
        Assert.Equal(PhraseLevel.Sentence, levels.LevelOf(levels.Units(PhraseLevel.Sentence)[0].Span));
        Assert.Null(levels.LevelOf(across));
    }

    [Fact]
    public void Joining_short_units_across_a_phrase_boundary_joins_the_phrases()
    {
        PhraseLevels levels = Levels();
        PhraseUnit first = levels.Units(PhraseLevel.Short)[0];

        PhraseLevels joined = levels.With(levels.JoinWithNext(PhraseLevel.Short, first));

        Assert.Equal([(1, 8), (9, 12), (13, 16)], Bars(joined, PhraseLevel.Phrase));
        Assert.Equal([new Tick(4 * Bar)], joined.Edits.Phrase.Removed);
        Assert.True(joined.Edits.Short.IsEmpty);
        // The joined phrase is eight bars, so the Short level divides it again by what the notes say.
        Assert.Equal([(1, 4), (5, 8), (9, 12), (13, 16)], Bars(joined, PhraseLevel.Short));
        Assert.Equal(ShortEvidence.Rest, joined.Shorts.Units[1].Start.Evidence);
    }

    [Fact]
    public void Making_a_sentence_puts_phrase_boundaries_at_its_edges_and_runs_on_inside()
    {
        PhraseLevels levels = Levels();

        // Bars 3–10 as one sentence: new phrase boundaries at bars 3 and 11, and the boundaries at 5 and 9 no longer end a sentence.
        PhraseLevels made = levels.With(levels.MakeUnit(PhraseLevel.Sentence, TickSpan.FromBounds(new Tick(2 * Bar), new Tick(10 * Bar))));

        Assert.Equal([(1, 2), (3, 4), (5, 8), (9, 10), (11, 12), (13, 16)], Bars(made, PhraseLevel.Phrase));
        Assert.Contains((3, 10), Bars(made, PhraseLevel.Sentence));
        Assert.Equal([new Tick(2 * Bar), new Tick(10 * Bar)], made.Edits.Phrase.Added);
        Assert.Equal([new Tick(2 * Bar), new Tick(10 * Bar)], made.Edits.Sentence.Added);
        Assert.Equal([new Tick(4 * Bar), new Tick(8 * Bar)], made.Edits.Sentence.Removed);
        PhraseUnit sentence = made.Units(PhraseLevel.Sentence).Single(unit => (unit.FirstBar, unit.LastBar) == (3, 10));
        Assert.Equal(Attribution.UserEdited, sentence.Start);
        Assert.Contains("you joined phrase", sentence.Description);

        // Reset at the sentence level forgets only the sentence edits: the phrase edges stay.
        PhraseLevels reset = made.With(made.Reset(PhraseLevel.Sentence));
        Assert.Equal(Bars(made, PhraseLevel.Phrase), Bars(reset, PhraseLevel.Phrase));
        Assert.True(reset.Edits.Sentence.IsEmpty);
        Assert.Equal(Bars(levels, PhraseLevel.Phrase), Bars(reset.With(reset.Reset(PhraseLevel.Phrase)), PhraseLevel.Phrase));
    }

    [Fact]
    public void Splitting_at_a_coarse_level_splits_the_finer_levels_too()
    {
        PhraseLevels levels = Levels();

        PhraseLevels split = levels.With(levels.SplitAt(PhraseLevel.Sentence, new Tick(6 * Bar)));

        Assert.Equal([(1, 4), (5, 6), (7, 8), (9, 12), (13, 16)], Bars(split, PhraseLevel.Phrase));
        Assert.Equal([(1, 6), (7, 8), (9, 16)], Bars(split, PhraseLevel.Sentence));
        Assert.Contains(new Tick(6 * Bar), split.Edits.Phrase.Added);
        Assert.Contains(new Tick(6 * Bar), split.Edits.Sentence.Added);
        Assert.Contains((5, 6), Bars(split, PhraseLevel.Short));

        // Splitting a short unit changes the phrases not at all.
        PhraseLevels shortSplit = levels.With(levels.SplitAt(PhraseLevel.Short, new Tick(2 * Bar)));
        Assert.Equal(Bars(levels, PhraseLevel.Phrase), Bars(shortSplit, PhraseLevel.Phrase));
        Assert.Equal([(1, 2), (3, 4), (5, 8), (9, 12), (13, 16)], Bars(shortSplit, PhraseLevel.Short));
    }

    [Fact]
    public void Edits_at_every_level_survive_being_saved_and_the_map_is_deterministic()
    {
        PhraseLevels levels = Levels();
        LevelEdits edits = levels.With(levels.SplitAt(PhraseLevel.Short, new Tick(2 * Bar))).Edits;
        edits = levels.With(edits).With(levels.With(edits).JoinWithNext(PhraseLevel.Sentence, levels.With(edits).Units(PhraseLevel.Sentence)[0])).Edits;

        PhraseLevels again = Levels(edits);

        Assert.Equal(Bars(levels.With(edits), PhraseLevel.Short), Bars(again, PhraseLevel.Short));
        Assert.Equal([(1, 16)], Bars(again, PhraseLevel.Sentence));
        Assert.Equal(edits, again.Edits);
        Assert.Equal(["short", "phrase", "sentence"], PhraseLevels.All.Select(PhraseLevels.Name));
        Assert.Equal(PhraseLevel.Sentence, PhraseLevels.Parse("sentences"));
        Assert.Null(PhraseLevels.Parse("period"));
    }
}