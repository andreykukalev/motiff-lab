using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>The piece as suggested phrases the player can refine (S1-I5, #18).</summary>
public class PhraseMapTests
{
    private const long Bar = 1920;

    private static MusicXmlImport ImportXml(string name) =>
        MusicXmlImporter.Import(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", name)));

    /// <summary>Five bars of 4/4 with boundaries at bar 3 beat 2 (a slur after a rest) and bar 5 (a slur and a repeat sign).</summary>
    private static PhraseMap Phrases(PhraseEdits? edits = null)
    {
        MusicXmlImport score = ImportXml("phrases-repeats.musicxml");
        return PhraseMap.Suggest(score.Notes, score.Meter, score.PhraseMarks, new Tick(5 * Bar), edits);
    }

    private static (long, long)[] Spans(PhraseMap map) => [.. map.Phrases.Select(phrase => (phrase.Span.Onset.Value, phrase.Span.End.Value))];

    [Fact]
    public void Phrases_cover_the_piece_from_boundary_to_boundary_with_their_evidence()
    {
        PhraseMap map = Phrases();

        Assert.Equal([(0L, 4320L), (4320L, 4 * Bar), (4 * Bar, 5 * Bar)], Spans(map));
        Assert.Equal([1, 2, 3], map.Phrases.Select(phrase => phrase.Number));
        Assert.Equal([(1, 3), (3, 4), (5, 5)], map.Phrases.Select(phrase => (phrase.FirstBar, phrase.LastBar)));
        Assert.Null(map.Phrases[0].Start);
        Assert.Equal([PhraseEvidence.Slur, PhraseEvidence.Rest], map.Phrases[1].Start!.Evidence);
        Assert.Equal(PhraseEvidenceLevel.Enough, map.Evidence);
        Assert.Equal(map.Found, map.Boundaries);
    }

    [Fact]
    public void Without_evidence_the_piece_is_one_phrase_and_none_is_invented()
    {
        MidiImport scale = MidiImporter.Import(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Midi", "Fixtures", "scale-type0.mid")));
        Tick end = scale.Notes.Max(note => note.Span.End);

        PhraseMap map = PhraseMap.Suggest(scale.Notes, scale.Meter, PhraseMarks.None, end);

        Phrase only = Assert.Single(map.Phrases);
        Assert.Equal(TickSpan.FromBounds(Tick.Zero, end), only.Span);
        Assert.Empty(map.Boundaries);
        Assert.Equal(PhraseEvidenceLevel.None, map.Evidence);
    }

    [Fact]
    public void A_stretch_longer_than_a_selection_without_a_boundary_is_partial_evidence()
    {
        // Ten bars of whole notes with a beat of rest only before bar 10.
        Note[] notes = [.. Enumerable.Range(0, 10).Select(bar => new Note
        {
            Id = $"n{bar}",
            Pitch = new Pitch(60),
            Span = new TickSpan(new Tick((bar * Bar) + (bar == 9 ? 480 : 0)), bar == 8 ? Bar - 480 : Bar - (bar == 9 ? 480 : 0)),
            Velocity = 80,
            Track = 0,
            Attribution = Attribution.Imported,
        })];

        PhraseMap map = PhraseMap.Suggest(notes, MeterMap.Default, PhraseMarks.None, new Tick(10 * Bar));

        Assert.Equal(PhraseEvidenceLevel.Partial, map.Evidence);
        Assert.False(map.Phrases[0].FitsSelection);
        // The long phrase is selected up to the most bars a selection holds.
        Assert.Equal(TickSpan.FromBounds(Tick.Zero, new Tick(8 * Bar)), map.Selectable(map.Phrases[0]));
        Assert.Equal(map.Phrases[1].Span, map.Selectable(map.Phrases[1]));
    }

    [Fact]
    public void Making_a_phrase_splits_and_moves_boundaries_and_marks_them_as_the_players()
    {
        PhraseMap map = Phrases();

        // Bar 2 alone: new boundaries at bars 2 and 3. The one at bar 3 beat 2 is a beat away, not less, so it stays.
        PhraseEdits edits = map.MakePhrase(TickSpan.FromBounds(new Tick(Bar), new Tick(2 * Bar)));
        PhraseMap edited = map.With(edits);

        Assert.Equal([(0L, Bar), (Bar, 2 * Bar), (2 * Bar, 4320L), (4320L, 4 * Bar), (4 * Bar, 5 * Bar)], Spans(edited));
        Assert.Equal([new Tick(Bar), new Tick(2 * Bar)], edits.Added);
        Assert.Empty(edits.Removed);
        Assert.Equal(Attribution.UserEdited, edited.Phrases[1].Start!.Attribution);
        Assert.Empty(edited.Phrases[1].Start!.Evidence);
        Assert.Equal(map.Evidence, edited.Evidence);
    }

    [Fact]
    public void Making_a_phrase_takes_away_the_boundaries_inside_it()
    {
        PhraseMap map = Phrases();

        PhraseEdits edits = map.MakePhrase(TickSpan.FromBounds(new Tick(2 * Bar), new Tick(5 * Bar)));
        PhraseMap edited = map.With(edits);

        Assert.Equal([(0L, 2 * Bar), (2 * Bar, 5 * Bar)], Spans(edited));
        Assert.Equal([new Tick(4320), new Tick(4 * Bar)], edits.Removed);
    }

    [Fact]
    public void An_edge_less_than_a_beat_from_a_boundary_moves_to_it_and_confirms_it()
    {
        PhraseMap map = Phrases();

        // A selection snapped to beats from bar 3 beat 1, an eighth before the phrase's first note.
        PhraseEdits edits = map.MakePhrase(TickSpan.FromBounds(new Tick((2 * Bar) + 240), new Tick(4 * Bar)));
        PhraseMap edited = map.With(edits);

        Assert.Equal(Spans(map), Spans(edited));
        Assert.Equal([new Tick(4320), new Tick(4 * Bar)], edits.Added);
        Assert.All(edited.Boundaries, boundary => Assert.Equal(Attribution.UserConfirmed, boundary.Attribution));
        Assert.Equal([PhraseEvidence.Slur, PhraseEvidence.Rest], edited.Phrases[1].Start!.Evidence);
        Assert.Equal("at a slur and after a rest of a beat or more, confirmed by you", PhraseMap.Describe(edited.Phrases[1].Start));
    }

    [Fact]
    public void Joining_takes_away_the_boundary_and_going_back_restores_the_suggestions()
    {
        PhraseMap map = Phrases();

        PhraseMap joined = map.With(map.JoinWithNext(map.Phrases[1]));

        Assert.Equal([(0L, 4320L), (4320L, 5 * Bar)], Spans(joined));
        Assert.Equal([new Tick(4 * Bar)], joined.Edits.Removed);
        Assert.Equal(joined.Edits, joined.JoinWithNext(joined.Phrases[^1]));
        Assert.Equal(Spans(map), Spans(joined.With(PhraseEdits.None)));
    }

    [Fact]
    public void A_removed_boundary_set_again_comes_back_with_its_evidence()
    {
        PhraseMap map = Phrases();
        PhraseMap joined = map.With(map.JoinWithNext(map.Phrases[0]));

        PhraseEdits again = joined.MakePhrase(TickSpan.FromBounds(new Tick(4320), new Tick(4 * Bar)));
        PhraseMap split = joined.With(again);

        Assert.Empty(again.Removed);
        Assert.Equal(Spans(map), Spans(split));
        Assert.Equal([PhraseEvidence.Slur, PhraseEvidence.Rest], split.Phrases[1].Start!.Evidence);
    }

    [Fact]
    public void Edits_outside_the_piece_are_left_out()
    {
        PhraseMap map = Phrases(new PhraseEdits([Tick.Zero, new Tick(9 * Bar)], []));

        Assert.Equal(Spans(Phrases()), Spans(map));
    }

    [Fact]
    public void The_phrase_at_a_position_and_the_phrase_matching_a_span()
    {
        PhraseMap map = Phrases();

        Assert.Equal(2, map.At(new Tick(5000)).Number);
        Assert.Equal(3, map.At(new Tick(99_999)).Number);
        Assert.Equal(1, map.At(Tick.Zero).Number);
        Assert.Equal(2, map.Matching(TickSpan.FromBounds(new Tick(4320), new Tick(4 * Bar)))?.Number);
        Assert.Null(map.Matching(TickSpan.FromBounds(new Tick(Bar), new Tick(2 * Bar))));
        Assert.Equal(1, map.Covering(TickSpan.FromBounds(new Tick(Bar), new Tick(3 * Bar))).Number);
        Assert.Equal(2, map.Covering(TickSpan.FromBounds(new Tick(3 * Bar), new Tick(5 * Bar))).Number);
    }

    [Fact]
    public void A_selection_extends_into_the_next_or_the_previous_phrase()
    {
        PhraseMap map = Phrases();

        PhraseExtension? next = map.ExtendInto(map.Phrases[0].Span, 1);
        Assert.Equal(TickSpan.FromBounds(Tick.Zero, new Tick(4 * Bar)), next?.Span);
        Assert.Equal(2, next?.Into.Number);
        Assert.False(next?.Shortened);

        PhraseExtension? previous = map.ExtendInto(map.Phrases[2].Span, -1);
        Assert.Equal(TickSpan.FromBounds(new Tick(4320), new Tick(5 * Bar)), previous?.Span);
        Assert.Equal(2, previous?.Into.Number);

        // A selection inside a phrase runs on to the end of that phrase.
        Assert.Equal(TickSpan.FromBounds(new Tick(Bar), new Tick(4320)), map.ExtendInto(TickSpan.FromBounds(new Tick(Bar), new Tick(2 * Bar)), 1)?.Span);

        Assert.Null(map.ExtendInto(map.Phrases[2].Span, 1));
        Assert.Null(map.ExtendInto(map.Phrases[0].Span, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.ExtendInto(map.Phrases[0].Span, 2));
    }

    [Fact]
    public void An_extended_selection_stops_at_the_most_bars_a_selection_holds()
    {
        // Ten bars of whole notes with a beat of rest only before bar 10: phrases of bars 1–10 and bar 10.
        Note[] notes = [.. Enumerable.Range(0, 10).Select(bar => new Note
        {
            Id = $"n{bar}",
            Pitch = new Pitch(60),
            Span = new TickSpan(new Tick((bar * Bar) + (bar == 9 ? 480 : 0)), bar == 8 ? Bar - 480 : Bar - (bar == 9 ? 480 : 0)),
            Velocity = 80,
            Track = 0,
            Attribution = Attribution.Imported,
        })];
        PhraseMap map = PhraseMap.Suggest(notes, MeterMap.Default, PhraseMarks.None, new Tick(10 * Bar));

        PhraseExtension? next = map.ExtendInto(TickSpan.FromBounds(Tick.Zero, new Tick(Bar)), 1);
        Assert.Equal(TickSpan.FromBounds(Tick.Zero, new Tick(8 * Bar)), next?.Span);
        Assert.True(next?.Shortened);
        // Already as long as a selection can be: nothing more to add.
        Assert.Null(map.ExtendInto(next!.Span, 1));

        PhraseExtension? previous = map.ExtendInto(map.Phrases[1].Span, -1);
        Assert.Equal(TickSpan.FromBounds(new Tick(2 * Bar), new Tick(10 * Bar)), previous?.Span);
        Assert.True(previous?.Shortened);
    }

    [Fact]
    public void Describe_says_how_a_phrase_start_was_found()
    {
        PhraseMap map = Phrases();

        Assert.Equal("the start of the piece", PhraseMap.Describe(map.Phrases[0].Start));
        Assert.Equal("at a slur and after a rest of a beat or more", PhraseMap.Describe(map.Phrases[1].Start));
        Assert.Equal("at a slur and at a repeat sign", PhraseMap.Describe(map.Phrases[2].Start));
        Assert.Equal("where an earlier passage comes back note for note", PhraseMap.Describe(new PhraseBoundary(new Tick(1), [PhraseEvidence.Repetition], Attribution.Inferred(0.6))));
        Assert.Equal("after a rest of a beat or more", PhraseMap.Describe(new PhraseBoundary(new Tick(1), [PhraseEvidence.Rest], Attribution.Inferred(0.7))));
        Assert.Equal("set by you", PhraseMap.Describe(new PhraseBoundary(new Tick(1), [], Attribution.UserEdited)));
    }

    [Fact]
    public void Describe_says_it_in_russian_too()
    {
        PhraseMap map = Phrases();

        Assert.Equal("с начала пьесы", PhraseMap.Describe(map.Phrases[0].Start, Words.Russian));
        Assert.Equal("по лиге и после паузы в долю или больше", PhraseMap.Describe(map.Phrases[1].Start, Words.Russian));
        Assert.Equal("по лиге, подтверждено вами", PhraseMap.Describe(new PhraseBoundary(new Tick(1), [PhraseEvidence.Slur], Attribution.UserConfirmed), Words.Russian));
        Assert.Equal("там, где вы поставили границу", PhraseMap.Describe(new PhraseBoundary(new Tick(1), [], Attribution.UserEdited), Words.Russian));
        Assert.Equal(PhraseMap.Describe(map.Phrases[2].Start), PhraseMap.Describe(map.Phrases[2].Start, Words.English));
    }

    [Fact]
    public void The_map_is_deterministic()
    {
        PhraseEdits edits = new([new Tick(Bar)], [new Tick(4 * Bar)]);

        Assert.Equal(Spans(Phrases(edits)), Spans(Phrases(edits)));
        Assert.Equal(
            Phrases(edits).Boundaries.Select(b => (b.At, string.Join(",", b.Evidence), b.Attribution)),
            Phrases(edits).Boundaries.Select(b => (b.At, string.Join(",", b.Evidence), b.Attribution)));
    }
}