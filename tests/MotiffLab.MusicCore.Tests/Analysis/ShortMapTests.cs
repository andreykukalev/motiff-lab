using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>The Short level (decision record 0018, section 3): phrases divided into units of 1–4 bars where the notes give a reason, else for length.</summary>
public class ShortMapTests
{
    private const long Quarter = 480;
    private const long Bar = 4 * Quarter;

    private static Note Note(string id, int pitch, long onset, long duration) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = 0,
        Attribution = Attribution.Imported,
    };

    /// <summary>Twelve bars of rising quarter notes in one phrase; the last note of bar 6 ends half a beat early.</summary>
    private static Note[] TwelveBars(bool restBeforeBar7 = true) =>
        [.. Enumerable.Range(0, 48).Select(i => Note($"n{i}", 48 + i, i * Quarter, restBeforeBar7 && i == 23 ? Quarter / 2 : Quarter))];

    private static (PhraseMap Phrases, ShortMap Shorts) Map(Note[] notes, int bars, PhraseEdits? edits = null)
    {
        PhraseMap phrases = PhraseMap.Suggest(notes, MeterMap.Default, PhraseMarks.None, new Tick(bars * Bar));
        return (phrases, ShortMap.Suggest(phrases, notes, MeterMap.Default, edits));
    }

    private static (int, int)[] Bars(ShortMap map) => [.. map.Units.Select(unit => (unit.FirstBar, unit.LastBar))];

    [Fact]
    public void A_long_phrase_is_cut_where_the_notes_rest_first_then_at_the_middle_for_length()
    {
        (PhraseMap phrases, ShortMap map) = Map(TwelveBars(), 12);

        Assert.Single(phrases.Phrases);
        Assert.Equal([(1, 3), (4, 6), (7, 9), (10, 12)], Bars(map));
        Assert.All(map.Units, unit => Assert.True(unit.BarCount <= ShortMap.MaxBars));
        Assert.Equal([ShortEvidence.PhraseStart, ShortEvidence.Length, ShortEvidence.Rest, ShortEvidence.Length], map.Units.Select(unit => unit.Start.Evidence));
        Assert.Equal(ShortMap.RestConfidence, map.Units[2].Start.Attribution.Confidence);
        Assert.Equal(Provenance.Inferred, map.Units[2].Start.Attribution.Provenance);
        Assert.Equal(ShortMap.LengthConfidence, map.Units[1].Start.Attribution.Confidence);
        Assert.Equal([1, 2, 3, 4], map.Units.Select(unit => unit.Number));
        Assert.All(map.Units, unit => Assert.Same(phrases.Phrases[0], unit.Phrase));
        Assert.Equal(3, map.Found.Count);
    }

    [Fact]
    public void A_phrase_of_four_bars_or_fewer_is_one_unit_that_starts_with_it()
    {
        (PhraseMap phrases, ShortMap map) = Map([.. TwelveBars().Take(16)], 4);

        ShortUnit only = Assert.Single(map.Units);
        Assert.Equal(phrases.Phrases[0].Span, only.Span);
        Assert.Equal(ShortEvidence.PhraseStart, only.Start.Evidence);
        Assert.Equal(Attribution.Imported, only.Start.Attribution);
        Assert.Empty(map.Found);
    }

    [Fact]
    public void Units_never_cross_a_phrase_boundary()
    {
        // Two phrases of six bars each, divided by a beat of rest before bar 7.
        Note[] notes = [.. Enumerable.Range(0, 48).Where(i => i != 23).Select(i => Note($"n{i}", 48 + i, i * Quarter, Quarter))];
        (PhraseMap phrases, ShortMap map) = Map(notes, 12);

        Assert.Equal(2, phrases.Phrases.Count);
        Assert.Equal([(1, 3), (4, 6), (7, 9), (10, 12)], Bars(map));
        Assert.Equal([ShortEvidence.PhraseStart, ShortEvidence.Length, ShortEvidence.PhraseStart, ShortEvidence.Length], map.Units.Select(unit => unit.Start.Evidence));
        Assert.Equal(phrases.Phrases[1].Start!.Attribution, map.Units[2].Start.Attribution);
        Assert.Equal([1, 2], map.Of(phrases.Phrases[0]).Select(unit => unit.Number));
    }

    [Fact]
    public void The_opening_figure_coming_back_marks_a_cut()
    {
        // Eight bars: the figure C D E F of bar 1 comes back at bar 5, and only that figure, so the phrase itself stays whole.
        int[] pitches = [60, 62, 64, 65, 67, 69, 71, 72, 74, 76, 77, 79, 81, 83, 84, 86, 60, 62, 64, 65, 84, 83, 82, 81, 80, 79, 78, 77, 76, 75, 74, 73];
        Note[] notes = [.. pitches.Select((pitch, i) => Note($"n{i}", pitch, i * Quarter, Quarter))];
        (_, ShortMap map) = Map(notes, 8);

        Assert.Equal([(1, 4), (5, 8)], Bars(map));
        Assert.Equal(ShortEvidence.Return, map.Units[1].Start.Evidence);
        Assert.Equal("where the phrase's opening figure comes back", ShortMap.Describe(map.Units[1].Start, Words.English));
        Assert.Equal("там, где возвращается начальная фигура фразы", ShortMap.Describe(map.Units[1].Start, Words.Russian));
    }

    [Fact]
    public void A_held_note_ending_at_the_bar_line_marks_a_cut()
    {
        // Eight bars of quarters, but bar 4 is one whole note that ends at bar 5.
        Note[] notes = [.. Enumerable.Range(0, 32).Where(i => i is < 12 or >= 16).Select(i => Note($"n{i}", 48 + i, i * Quarter, Quarter)), Note("held", 72, 3 * Bar, Bar)];
        (_, ShortMap map) = Map(notes, 8);

        Assert.Equal([(1, 4), (5, 8)], Bars(map));
        Assert.Equal(ShortEvidence.HeldNote, map.Units[1].Start.Evidence);
        Assert.Equal(ShortMap.HeldNoteConfidence, map.Units[1].Start.Attribution.Confidence);
    }

    [Fact]
    public void The_player_cuts_joins_and_makes_units_and_their_cuts_are_marked_theirs()
    {
        (_, ShortMap map) = Map(TwelveBars(), 12);

        ShortMap split = map.With(map.SplitAt(new Tick(Bar)));
        Assert.Equal([(1, 1), (2, 3), (4, 6), (7, 9), (10, 12)], Bars(split));
        Assert.Equal(Attribution.UserEdited, split.Units[1].Start.Attribution);
        Assert.Equal("set by you", ShortMap.Describe(split.Units[1].Start, Words.English));
        Assert.Equal([new Tick(Bar)], split.Edits.Added);

        // Joining takes a suggested cut away, and the long unit that results stays: the player's word stands.
        ShortMap joined = map.With(map.JoinWithNext(map.Units[0]));
        Assert.Equal([(1, 6), (7, 9), (10, 12)], Bars(joined));
        Assert.Equal([new Tick(3 * Bar)], joined.Edits.Removed);

        // Making bars 2–5 one unit: cuts at its edges, none inside; the edge a beat from bar 4's cut does not snap to it.
        ShortMap made = map.With(map.MakeUnit(TickSpan.FromBounds(new Tick(Bar), new Tick(5 * Bar))));
        Assert.Equal([(1, 1), (2, 5), (6, 6), (7, 9), (10, 12)], Bars(made));
        Assert.Equal([new Tick(Bar), new Tick(5 * Bar)], made.Edits.Added);
        Assert.Equal([new Tick(3 * Bar)], made.Edits.Removed);

        // A cut set where the notes suggest one confirms it.
        ShortMap confirmed = map.With(map.SplitAt(new Tick(6 * Bar)));
        Assert.Equal(Bars(map), Bars(confirmed));
        Assert.Equal(Attribution.UserConfirmed, confirmed.Units[2].Start.Attribution);
        Assert.Equal("after half a beat or more of rest, confirmed by you", ShortMap.Describe(confirmed.Units[2].Start, Words.English));
        Assert.Equal(Bars(map), Bars(confirmed.With(PhraseEdits.None)));
    }

    [Fact]
    public void Edits_at_phrase_boundaries_or_outside_the_piece_are_left_out()
    {
        Note[] notes = [.. Enumerable.Range(0, 48).Where(i => i != 23).Select(i => Note($"n{i}", 48 + i, i * Quarter, Quarter))];
        (PhraseMap phrases, ShortMap map) = Map(notes, 12, new PhraseEdits([Tick.Zero, new Tick(6 * Bar), new Tick(99 * Bar)], []));

        Assert.Equal(new Tick(6 * Bar), phrases.Boundaries[0].At);
        Assert.Equal([(1, 3), (4, 6), (7, 9), (10, 12)], Bars(map));
        Assert.Equal(ShortEvidence.PhraseStart, map.Units[2].Start.Evidence);
    }

    [Fact]
    public void The_map_is_deterministic()
    {
        (_, ShortMap first) = Map(TwelveBars(), 12, new PhraseEdits([new Tick(Bar)], [new Tick(3 * Bar)]));
        (_, ShortMap second) = Map([.. TwelveBars().Reverse()], 12, new PhraseEdits([new Tick(Bar)], [new Tick(3 * Bar)]));

        Assert.Equal(Bars(first), Bars(second));
        Assert.Equal(first.Units.Select(unit => (unit.Start.At, unit.Start.Evidence, unit.Start.Attribution)), second.Units.Select(unit => (unit.Start.At, unit.Start.Evidence, unit.Start.Attribution)));
    }
}