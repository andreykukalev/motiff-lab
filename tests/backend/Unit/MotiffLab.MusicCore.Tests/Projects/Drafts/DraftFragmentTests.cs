using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects.Drafts;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Projects.Drafts.DraftFixtures;

namespace MotiffLab.MusicCore.Tests.Projects.Drafts;

/// <summary>A fragment of a selection as a variant (decision record 0034, sections 2 and 3): all, a hand, or the melody where the voices are clean.</summary>
public class DraftFragmentTests
{
    [Fact]
    public void A_fragment_is_a_copy_from_0_with_its_bars_tempo_and_key_and_where_it_came_from()
    {
        DraftVariant variant = Variant("v_1", Score(fifths: 1, perMinute: 72), 3, 4);

        Assert.Equal(("n_1", DraftPart.All, (3, 4)), (variant.From.Notes, variant.From.Part, variant.From.Bars!.Value));
        Assert.Equal(Bars(3, 4), variant.From.Span);
        Assert.Equal(10, variant.Music.Notes.Count);
        Assert.Equal(Tick.Zero, variant.Music.Notes[0].Span.Onset);
        Assert.Equal("n2l", variant.Music.Notes[0].Id);
        Assert.Equal([new TickSpan(Tick.Zero, BarLength), new TickSpan(new Tick(BarLength), BarLength)], variant.Music.Bars.Select(bar => bar.Span));
        Assert.Equal(72, variant.Music.TempoAt(Tick.Zero));
        Assert.Equal((Tick.Zero, 1), (variant.Music.Keys.Single().At, variant.Music.Keys.Single().Fifths));
        Assert.Same(Read, variant.Provenance);
    }

    [Fact]
    public void A_fragment_of_another_piece_carries_that_piece_in_every_source_of_its_provenance()
    {
        DraftVariant variant = DraftFragment.Variant("v_1", At, Score(), Bars(1, 1), DraftPart.All, "n_2", Read, project: "p_other", milliseconds: (72_000, 84_500));

        Assert.Equal("p_other", variant.From.Project);
        Assert.Equal((72_000L, 84_500L), variant.From.Milliseconds);
        ProvenanceSource source = Assert.Single(variant.Provenance.From);
        Assert.Equal(("p_other", Read.From[0].Sha256), (source.Project, source.Sha256));
    }

    [Fact]
    public void A_hand_takes_only_its_notes_and_a_fragment_with_none_of_the_part_is_refused()
    {
        SourceMusic score = Score();

        Assert.All(Variant("v_1", score, 1, 2, DraftPart.Left).Music.Notes, note => Assert.Equal(Hand.Left, note.Hand!.Value));
        Assert.Equal(8, Variant("v_1", score, 1, 2, DraftPart.Right).Music.Notes.Count);
        SourceMusic rightOnly = SourceMusic.Of([Note("a", 72, 0, Quarter)], MeterMap.Default, new Tick(BarLength), [], []);
        Assert.Throws<ArgumentException>(() => DraftFragment.Of(rightOnly, Bars(1, 1), DraftPart.Left));
        Assert.Equal([DraftPart.All, DraftPart.Right, DraftPart.Left, DraftPart.Melody], DraftFragment.Parts(score.Notes));
    }

    [Fact]
    public void The_melody_is_the_top_voice_of_the_right_hand_marked_inferred()
    {
        IReadOnlyList<Note> notes = [Note("t1", 76, 0, Quarter), Note("b1", 67, 0, Quarter), Note("t2", 77, Quarter, Quarter), Note("b2", 69, Quarter, Quarter), Note("l", 48, 0, BarLength, Hand.Left)];

        IReadOnlyList<Note> melody = DraftFragment.Melody(notes)!;

        Assert.Equal(["t1", "t2"], melody.Select(note => note.Id));
        Assert.All(melody, note => Assert.Equal(Provenance.Inferred, note.Attribution.Provenance));
    }

    [Fact]
    public void The_melody_is_not_offered_where_the_voices_cross_split_or_change_voice_or_there_is_no_right_hand()
    {
        // Crossing: an inner note held from the first beat sounds above the line's next note.
        IReadOnlyList<Note> crossing = [Note("t1", 76, 0, Quarter), Note("inner", 74, 0, Quarter * 2), Note("t2", 72, Quarter, Quarter)];
        // Split: a line note still sounds when the next one starts.
        IReadOnlyList<Note> split = [Note("t1", 76, 0, Quarter * 2), Note("t2", 77, Quarter, Quarter)];
        // The line moves from one written voice to another.
        IReadOnlyList<Note> voices = [Note("t1", 76, 0, Quarter, voice: 1), Note("t2", 77, Quarter, Quarter, voice: 2)];
        IReadOnlyList<Note> noHands = [Note("t1", 76, 0, Quarter, hand: null)];

        Assert.Null(DraftFragment.Melody(crossing));
        Assert.Null(DraftFragment.Melody(split));
        Assert.Null(DraftFragment.Melody(voices));
        Assert.Null(DraftFragment.Melody(noHands));
        Assert.DoesNotContain(DraftPart.Melody, DraftFragment.Parts(split));
        SourceMusic music = SourceMusic.Of(split, MeterMap.Default, new Tick(BarLength), [], []);
        Assert.Throws<ArgumentException>(() => DraftFragment.Of(music, Bars(1, 1), DraftPart.Melody));
    }

    [Fact]
    public void The_same_selection_gives_the_same_variant()
    {
        DraftVariant one = Variant("v_1", Score(), 2, 5, DraftPart.Melody);
        DraftVariant two = Variant("v_1", Score(), 2, 5, DraftPart.Melody);

        Assert.Equal(one.Music.Notes, two.Music.Notes);
        Assert.Equal(one.Music.Bars, two.Music.Bars);
        Assert.Equal(one.From, two.From);
    }
}