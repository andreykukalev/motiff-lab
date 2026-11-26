using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Projects.Drafts;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Projects.Drafts.DraftFixtures;

namespace MotiffLab.MusicCore.Tests.Projects.Drafts;

/// <summary>A приём carried into another place (version 1.4, Andrei's answer of 2026-11-26): a variant music-core builds for that place.</summary>
public class TechniqueCarryTests
{
    private static readonly DateTimeOffset Later = At.AddMinutes(5);

    /// <summary>The place of eight bars with the octaves of bars 2 and 6 as a variant, then four bars of another tune in G major as a second place.</summary>
    private static Draft Two(int fifths = 1, Func<int, int[]>? melody = null) =>
        Drafted(Octaves()).With(Variant("v_3", Score(bars: 4, melody: melody ?? (static _ => [67, 71, 74, 79]), fifths: fifths, prefix: "g"), 1, 4, notes: "n_other"), DraftDrop.Place, "pl_2");

    [Fact]
    public void An_octave_carried_into_another_place_is_a_variant_of_it_with_that_place_s_notes_an_octave_up_in_every_bar()
    {
        (Draft carried, IReadOnlyList<int> bars) = TechniqueCarry.Put(Two(), "pl_1", 1, null, "pl_2", Later, Words.English);

        Assert.Equal([1, 2, 3, 4], bars);
        DraftVariant variant = carried.FindPlace("pl_2")!.Variants[1];
        Assert.Equal(("v_0004", "v_3"), (variant.Id, carried.FindPlace("pl_2")!.Base));
        Assert.Equal(MotiffLab.MusicCore.Pieces.NotesProvenance.Variation, variant.Provenance.Op);
        Assert.Equal("""{"carry":"octave","place":"pl_1","variant":"v_2","bar":2}""", variant.Provenance.ParamsJson);
        Assert.Equal(TechniqueKind.Octave, TechniqueCarry.KindOf(variant));
        Assert.Equal("Перенесённый приём: октава", DraftLabels.Variant(variant, Words.Russian));
        Note[] right = [.. variant.Music.Notes.Where(static note => note.Hand?.Value == Hand.Right && note.Span.Onset.Value < BarLength)];
        Assert.Equal([79, 83, 86, 91], right.Select(static note => note.Pitch.MidiNumber));
        Assert.All(right, static note => Assert.Equal((Provenance.Inferred, Provenance.Inferred), (note.Attribution.Provenance, note.Hand!.Attribution.Provenance)));
        Assert.All(variant.Music.Notes.Where(static note => note.Hand?.Value == Hand.Left), static note => Assert.Equal(48, note.Pitch.MidiNumber));
        // The player takes it as any variant's приём: an octave in every bar of the place.
        PlaceTechnique octave = Assert.Single(carried.TechniquesOf("pl_2"));
        Assert.Equal((TechniqueKind.Octave, "v_0004"), (octave.Kind, octave.Variant));
        Assert.Equal([1, 2, 3, 4], octave.Occurrences.Select(static occurrence => occurrence.Bar));
        Assert.Contains(DraftTakes.Take(carried, "pl_2", 1, null, Later).ResultOf("pl_2").Music.Notes, static note => note.Pitch.MidiNumber == 91);
    }

    [Fact]
    public void A_note_put_in_follows_the_base_note_by_steps_of_the_other_place_s_key()
    {
        // The variant adds a third below each quarter of bar 3 (72 74 76 77 over 69 71 72 74 in C major).
        SourceMusic filled = Score(prefix: "h");
        filled = filled with { Notes = [.. filled.Notes, .. new[] { 69, 71, 72, 74 }.Select((pitch, beat) => Note($"f{beat}", pitch, (2 * BarLength) + (beat * Quarter), Quarter))] };
        Draft draft = Drafted(filled).With(Variant("v_3", Score(bars: 2, melody: static _ => [71, 72, 74, 76], fifths: 1, prefix: "g"), 1, 2, notes: "n_other"), DraftDrop.Place, "pl_2");
        PlaceTechnique fill = Assert.Single(draft.TechniquesOf("pl_1"));

        (Draft carried, _) = TechniqueCarry.Put(draft, "pl_1", fill.Number, null, "pl_2", Later, Words.English);

        // In G major two steps below B, C, D, E are G, A, B, C (by semitones they would be G♯, A, A♯, C♯).
        Assert.Equal([67, 69, 71, 72], carried.FindPlace("pl_2")!.Variants[1].Music.Notes.Where(static note => note.Id.StartsWith("c_", StringComparison.Ordinal) && note.Span.Onset.Value < BarLength).Select(static note => note.Pitch.MidiNumber));
    }

    [Fact]
    public void The_same_draft_gives_the_same_notes_and_a_second_carry_is_refused()
    {
        (Draft once, _) = TechniqueCarry.Put(Two(), "pl_1", 1, null, "pl_2", Later, Words.English);
        (Draft again, _) = TechniqueCarry.Put(Two(), "pl_1", 1, null, "pl_2", Later, Words.English);

        Assert.Equal(once.FindPlace("pl_2")!.Variants[1].Music.Notes, again.FindPlace("pl_2")!.Variants[1].Music.Notes);
        Assert.Equal("Этот приём уже перенесён в это место.", Assert.Throws<InvalidOperationException>(() => TechniqueCarry.Put(once, "pl_1", 1, null, "pl_2", Later, Words.Russian)).Message);
    }

    [Fact]
    public void Targets_are_the_other_places_with_the_bars_the_shape_fits()
    {
        Draft draft = Two().With(Variant("v_9", Score(bars: 2, melody: static _ => [72, 74, 76], prefix: "t"), 1, 2, notes: "n_three"), DraftDrop.Place, "pl_3");
        MeterMap waltz = new([new MeterChange(Tick.Zero, new TimeSignature(3, 4))]);
        SourceMusic threes = Score(bars: 2, prefix: "w");
        draft = draft.With(Variant("v_8", SourceMusic.Of(threes.Notes, waltz, new Tick(8 * Quarter), threes.Tempos, threes.Keys), 1, 2, notes: "n_waltz"), DraftDrop.Place, "pl_4");

        IReadOnlyList<(DraftPlace Place, IReadOnlyList<int> Bars)> targets = TechniqueCarry.Targets(draft, "pl_1", 1, null, Words.English);

        Assert.Equal(["pl_2", "pl_3", "pl_4"], targets.Select(static target => target.Place.Id));
        Assert.Equal([1, 2, 3, 4], targets[0].Bars);
        // Three quarters in a bar of 4/4: the fourth quarter of the shape is not there.
        Assert.Empty(targets[1].Bars);
        // Bars of 3/4 are not of the приём's length.
        Assert.Empty(targets[2].Bars);
        Assert.Throws<InvalidOperationException>(() => TechniqueCarry.Put(draft, "pl_1", 1, null, "pl_4", Later, Words.English));
    }

    [Fact]
    public void Bars_put_in_into_itself_into_a_bridge_and_an_unknown_приём_are_refused()
    {
        Draft inserted = Drafted(WithABarAdded(4)).With(Variant("v_3", Score(bars: 4, prefix: "g"), 1, 4, notes: "n_other"), DraftDrop.Place, "pl_2");
        Draft bridged = DraftBridges.Put(Two(), "pl_2", At);

        Assert.Throws<InvalidOperationException>(() => TechniqueCarry.Targets(inserted, "pl_1", 1, null, Words.English));
        Assert.Throws<ArgumentException>(() => TechniqueCarry.Put(Two(), "pl_1", 1, null, "pl_1", Later, Words.English));
        Assert.Throws<InvalidOperationException>(() => TechniqueCarry.Put(bridged, "pl_1", 1, null, bridged.Places[1].Id, Later, Words.English));
        Assert.DoesNotContain(TechniqueCarry.Targets(bridged, "pl_1", 1, null, Words.English), static target => target.Place.Bridge);
        Assert.Throws<KeyNotFoundException>(() => TechniqueCarry.Targets(Two(), "pl_1", 9, null, Words.English));
        Assert.Throws<KeyNotFoundException>(() => TechniqueCarry.Targets(Two(), "pl_1", 1, 3, Words.English));
        Assert.Throws<KeyNotFoundException>(() => TechniqueCarry.Put(Two(), "pl_1", 1, null, "pl_9", Later, Words.English));
    }
}