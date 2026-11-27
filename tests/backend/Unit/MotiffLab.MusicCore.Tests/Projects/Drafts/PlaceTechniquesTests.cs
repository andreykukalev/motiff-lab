using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Projects.Drafts;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Projects.Drafts.DraftFixtures;

namespace MotiffLab.MusicCore.Tests.Projects.Drafts;

/// <summary>The приёмы of a place: a variant's differences from its base grouped by kind and shape, numbered, sure at 85 % (decision record 0034, section 4).</summary>
public class PlaceTechniquesTests
{
    [Fact]
    public void Differences_of_one_kind_and_shape_in_different_bars_are_one_numbered_sure_technique()
    {
        PlaceTechnique octave = Assert.Single(Drafted(Octaves()).TechniquesOf("pl_1"));

        Assert.Equal((1, "v_2", TechniqueKind.Octave), (octave.Number, octave.Variant, octave.Kind));
        Assert.Equal([2, 6], octave.Occurrences.Select(static occurrence => occurrence.Bar));
        Assert.True(octave.IsSure);
        Assert.Equal(100, octave.Confidence);
        Assert.Equal(TechniqueState.Open, octave.State);
        TechniqueOccurrence second = octave.Occurrences[0];
        Assert.Equal(["v_1.n1r0", "v_1.n1r1", "v_1.n1r2", "v_1.n1r3"], second.Removed);
        Assert.Equal(["v_2.h1r0", "v_2.h1r1", "v_2.h1r2", "v_2.h1r3"], second.Added);
        Assert.Equal(Bars(2, 2), second.Span);
    }

    [Fact]
    public void Another_kind_is_another_technique_numbered_in_the_order_of_its_first_bar()
    {
        IReadOnlyList<PlaceTechnique> techniques = Drafted(OctavesAndBass()).TechniquesOf("pl_1");

        Assert.Equal([(1, TechniqueKind.Octave, 2), (2, TechniqueKind.Bass, 4)], techniques.Select(static technique => (technique.Number, technique.Kind, technique.Occurrences[0].Bar)));
    }

    [Fact]
    public void A_place_of_one_variant_or_the_same_music_has_no_techniques()
    {
        Assert.Empty(Draft.Start("d_1", "Предложение", At, "pl_1", Variant("v_1", Score(), 1, 8)).TechniquesOf("pl_1"));
        Assert.Empty(Drafted(Score(prefix: "h")).TechniquesOf("pl_1"));
    }

    [Fact]
    public void Bars_the_variant_adds_are_a_technique_of_their_own_never_sure_put_before_the_bar_they_fall_on()
    {
        SourceMusic heard = WithABarAdded(after: 4);

        PlaceTechnique insert = Assert.Single(Drafted(heard).TechniquesOf("pl_1"));

        Assert.Equal(TechniqueKind.InsertBars, insert.Kind);
        Assert.False(insert.IsSure);
        TechniqueOccurrence occurrence = Assert.Single(insert.Occurrences);
        Assert.Equal(5, occurrence.Bar);
        Assert.Equal(new DraftInsert(new Tick(4 * BarLength), new TickSpan(new Tick(4 * BarLength), BarLength)), occurrence.Insert);
        Assert.Empty(occurrence.Added);
    }

    [Fact]
    public void A_technique_found_in_adjacent_bars_is_sure_by_each_bar_s_own_notes()
    {
        PlaceTechnique octave = Assert.Single(Drafted(Score(melody: bar => bar is 1 or 2 ? [84, 86, 88, 89] : [72, 74, 76, 77], prefix: "h")).TechniquesOf("pl_1"));

        Assert.Equal([2, 3], octave.Occurrences.Select(static occurrence => occurrence.Bar));
        Assert.True(octave.IsSure);
    }

    [Fact]
    public void A_whole_bar_octave_is_sure_as_an_empty_bar_matches_and_a_bar_played_otherwise_is_not()
    {
        SourceMusic Bar2(int[] melody, int bass)
        {
            SourceMusic score = Score(melody: bar => bar == 1 ? melody : [72, 74, 76, 77], prefix: "h");
            return score with { Notes = [.. score.Notes.Select(note => note.Id == "h1l" ? note with { Pitch = new Pitch(bass) } : note)] };
        }

        PlaceTechnique octave = Assert.Single(Drafted(Bar2([84, 86, 88, 89], 60)).TechniquesOf("pl_1"));
        // Its melody played otherwise over the same bass, so the bar is still found in its place: «иначе» counts its notes as missed.
        PlaceTechnique other = Assert.Single(Drafted(Bar2([61, 63, 66, 68], 48)).TechniquesOf("pl_1"));
        // Over another bass the whole bar is found nowhere in the base: bars put in, never sure.
        PlaceTechnique elsewhere = Assert.Single(Drafted(Bar2([61, 63, 66, 68], 45)).TechniquesOf("pl_1"));
        // Half the melody played otherwise over another bass: another bass explains only the bass, the rest counts as missed.
        PlaceTechnique bass = Assert.Single(Drafted(Bar2([72, 63, 66, 77], 45)).TechniquesOf("pl_1"));

        Assert.Equal((TechniqueKind.Octave, 0, 0, true), (octave.Kind, octave.Occurrences[0].Matched, octave.Occurrences[0].Total, octave.IsSure));
        Assert.Equal((TechniqueKind.Other, 1, 5, false), (other.Kind, other.Occurrences[0].Matched, other.Occurrences[0].Total, other.IsSure));
        Assert.Equal((TechniqueKind.InsertBars, false), (elsewhere.Kind, elsewhere.IsSure));
        Assert.Equal((TechniqueKind.Bass, 2, 4, false), (bass.Kind, bass.Occurrences[0].Matched, bass.Occurrences[0].Total, bass.IsSure));
        Assert.Equal(["v_1.n1l"], bass.Occurrences[0].Removed);
        Assert.Equal(["v_2.h1l"], bass.Occurrences[0].Added);
    }

    [Fact]
    public void A_fill_s_own_notes_are_the_ones_it_adds_and_a_note_it_leaves_out_is_a_miss()
    {
        // Bar 2 adds two notes off the beat and leaves out its last quarter.
        SourceMusic score = Score(melody: bar => bar == 1 ? [72, 74, 76] : [72, 74, 76, 77], prefix: "h");
        SourceMusic heard = score with { Notes = [.. score.Notes, Note("hf0", 79, BarLength + (Quarter / 2), Quarter / 2), Note("hf1", 84, BarLength + (5 * Quarter / 2), Quarter / 2)] };

        TechniqueOccurrence fill = Assert.Single(Assert.Single(Drafted(heard).TechniquesOf("pl_1"), static technique => technique.Kind == TechniqueKind.Fill).Occurrences);

        Assert.Equal((4, 5), (fill.Matched, fill.Total));
        Assert.Empty(fill.Removed);
        Assert.Equal(["v_2.hf0", "v_2.hf1"], fill.Added);
    }

    [Theory]
    [InlineData(4, 85, true)]
    [InlineData(3, 84, false)]
    public void A_technique_is_sure_when_85_per_cent_of_its_bar_s_other_notes_are_found_in_place(int chordNotes, int confidence, bool sure)
    {
        PlaceTechnique octave = Dense(chordNotes).TechniquesOf("pl_1")[0];

        Assert.Equal((TechniqueKind.Octave, confidence, sure), (octave.Kind, octave.Confidence, octave.IsSure));
    }

    [Fact]
    public void Take_all_sure_skips_a_technique_under_85_per_cent()
    {
        Draft draft = Dense(3);

        Assert.Equal([2], DraftTakes.TakeSure(draft, "pl_1", At, Ids).Taken);
    }

    /// <summary>
    /// Four bars whose bar 2 is dense: the melody, a triad on each beat, the bass with <paramref name="chordNotes"/> notes
    /// over it and three notes too low for a recording to hear. The variant plays the melody of bar 2 an octave higher, does
    /// not sound the low notes (the bar's notes not found in place that are not the приём's), and another bass in bar 4.
    /// </summary>
    internal static Draft Dense(int chordNotes)
    {
        SourceMusic Music(string prefix, bool low, Func<int, int[]> melody, int bass)
        {
            SourceMusic score = Score(bars: 4, melody: melody, prefix: prefix);
            List<Note> notes = [.. score.Notes.Select(note => note.Id == $"{prefix}3l" ? note with { Pitch = new Pitch(bass) } : note)];
            int[] under = [50, 52, 53, 57];
            notes.AddRange(Enumerable.Range(0, 4).SelectMany(beat => new[] { 60, 64, 67 }.Select(pitch => Note($"{prefix}c{beat}.{pitch}", pitch, BarLength + (beat * Quarter), Quarter))));
            notes.AddRange(under.Take(chordNotes).Select(pitch => Note($"{prefix}u{pitch}", pitch, BarLength, BarLength, Hand.Left)));
            notes.AddRange(low ? new[] { 24, 26, 28 }.Select(pitch => Note($"{prefix}low{pitch}", pitch, BarLength, BarLength, Hand.Left)) : []);
            return SourceMusic.Of(notes, score.Meter, score.End, score.Tempos, score.Keys);
        }

        SourceMusic notes = Music("n", low: true, static _ => [72, 74, 76, 77], 48);
        SourceMusic heard = Music("h", low: false, static bar => bar == 1 ? [84, 86, 88, 89] : [72, 74, 76, 77], 43);
        return Draft.Start("d_1", "Предложение", At, "pl_1", Variant("v_1", notes, 1, 4)).With(Variant("v_2", heard, 1, 4, notes: "perf_1"), DraftDrop.Variant, "pl_1");
    }

    [Fact]
    public void The_same_place_and_steps_give_the_same_techniques()
    {
        Draft draft = Drafted(OctavesAndBass());

        Assert.Equal(Describe(draft.TechniquesOf("pl_1")), Describe(draft.TechniquesOf("pl_1")));
    }

    private static string Describe(IReadOnlyList<PlaceTechnique> techniques) =>
        string.Join(";", techniques.Select(static technique => $"{technique.Number}:{technique.Kind}:{technique.Confidence}:{string.Join(",", technique.Occurrences.Select(static occurrence => $"{occurrence.Bar}/{string.Join("+", occurrence.Removed)}/{string.Join("+", occurrence.Added)}"))}"));
}