using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Projects.Drafts;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Projects.Drafts.DraftFixtures;

namespace MotiffLab.MusicCore.Tests.Projects.Drafts;

/// <summary>A draft's seams (decision record 0034, section 5): what differs at the boundary of two places, and what is offered there, nothing changed by itself.</summary>
public class DraftSeamsTests
{
    /// <summary>Bars 1–4 in C major at 90 a minute, then bars 1–4 of another fragment in D major at 120.</summary>
    private static Draft Collage(SourceMusic? second = null) =>
        Draft.Start("d_1", "Коллаж", At, "pl_1", Variant("v_1", Score(), 1, 4))
            .With(Variant("v_2", second ?? Score(fifths: 2, perMinute: 120, prefix: "o"), 1, 4, notes: "n_other"), DraftDrop.Place, "pl_2");

    [Fact]
    public void A_seam_states_the_key_meter_and_tempo_on_each_side_and_offers_the_key_and_tempo_before_it()
    {
        DraftSeam seam = Assert.Single(DraftSeams.Of(Collage()));

        Assert.Equal(("pl_1", "pl_2", new Tick(4 * BarLength)), (seam.Before.Place, seam.After.Place, seam.At));
        Assert.Equal((0, 2), (seam.Before.Key!.Fifths, seam.After.Key!.Fifths));
        Assert.Equal((90, 120, TempoSource.Assumed), (seam.Before.PerMinute, seam.After.PerMinute, seam.After.Tempo.Source));
        Assert.Equal((true, false, true), (seam.KeyChanges, seam.MeterChanges, seam.TempoChanges));
        Assert.Equal(-2, seam.Semitones);
        Assert.Equal((0, KeyMode.Major), (seam.Transposed!.Fifths, seam.Transposed.Mode));
        Assert.Equal(90, seam.Tempo);
        Assert.Equal(TickSpan.FromBounds(new Tick(3 * BarLength), new Tick(5 * BarLength)), seam.Listen);
        Assert.False(seam.Left);
    }

    [Fact]
    public void The_place_transposed_and_set_to_the_tempo_before_it_leaves_nothing_to_offer()
    {
        Draft draft = Collage();
        DraftSeam seam = DraftSeams.Of(draft)[0];

        draft = DraftSeams.Tempo(DraftSeams.Transpose(draft, "pl_2", seam.Semitones!.Value, At), "pl_2", seam.Tempo!.Value, At);

        DraftSeam after = DraftSeams.Of(draft)[0];
        Assert.Equal((0, KeyMode.Major), (after.After.Key!.Fifths, after.After.Key.Mode));
        Assert.Equal((90, TempoSource.Player), (after.After.PerMinute, after.After.Tempo.Source));
        Assert.Equal((false, false, null, null), (after.KeyChanges, after.TempoChanges, after.Semitones, after.Tempo));
        Assert.Equal(74 - 2, draft.ResultOf("pl_2").Music.Notes.Single(note => note.Id == "v_2.o0r1").Pitch.MidiNumber);
    }

    [Fact]
    public void Kept_as_it_is_the_seam_is_remembered_until_undone()
    {
        Draft draft = DraftSeams.Leave(Collage(), "pl_2", At);

        Assert.True(DraftSeams.Of(draft)[0].Left);
        Assert.Throws<InvalidOperationException>(() => DraftSeams.Leave(draft, "pl_2", At));
        Assert.Throws<ArgumentException>(() => DraftSeams.Leave(draft, "pl_1", At));
        Assert.Throws<KeyNotFoundException>(() => DraftSeams.Leave(draft, "pl_9", At));
        Assert.False(DraftSeams.Of(draft.Undone())[0].Left);
    }

    [Fact]
    public void A_side_whose_source_states_no_key_states_none_and_no_transposition_is_offered()
    {
        SourceMusic keyless = Score(prefix: "o") with { Keys = [] };

        DraftSeam seam = DraftSeams.Of(Collage(keyless))[0];

        Assert.Null(seam.After.Key);
        Assert.Equal((false, null, null), (seam.KeyChanges, seam.Semitones, seam.Transposed));
    }

    [Fact]
    public void Another_meter_is_a_change_of_meter()
    {
        SourceMusic waltz = Score(prefix: "o");
        MeterMap meter = new([new MeterChange(Tick.Zero, new TimeSignature(3, 4))]);
        waltz = SourceMusic.Of(waltz.Notes, meter, waltz.End, waltz.Tempos, waltz.Keys);

        DraftSeam seam = DraftSeams.Of(Collage(waltz))[0];

        Assert.Equal((new TimeSignature(4, 4), new TimeSignature(3, 4), true), (seam.Before.Meter, seam.After.Meter, seam.MeterChanges));
    }

    [Fact]
    public void A_side_states_its_tempo_with_the_source_it_was_saved_with_and_a_place_with_no_tempo_plays_at_the_default()
    {
        SourceMusic heard = Score(prefix: "o") with { Tempos = [new PlaybackTempo(Tick.Zero, 500_000, TempoSource.Inferred)] };
        Draft draft = Draft.Start("d_1", "Запись", At, "pl_1", Variant("v_1", Score() with { Tempos = [] }, 1, 4)).With(Variant("v_2", heard, 1, 4), DraftDrop.Place, "pl_2");

        DraftSeam seam = DraftSeams.Of(draft)[0];

        Assert.Equal((PlaybackTempo.PieceDefaultPerMinute, TempoSource.Default), (seam.Before.PerMinute, seam.Before.Tempo.Source));
        Assert.Equal((120, TempoSource.Inferred), (seam.After.PerMinute, seam.After.Tempo.Source));
        Assert.Equal(PlaybackTempo.PieceDefaultPerMinute, seam.Tempo);
    }

    [Fact]
    public void A_draft_of_one_place_has_no_seam_and_a_transposition_of_none_is_refused()
    {
        Draft draft = Draft.Start("d_1", "x", At, "pl_1", Variant("v_1", Score(), 1, 4));

        Assert.Empty(DraftSeams.Of(draft));
        Assert.Throws<ArgumentOutOfRangeException>(() => DraftSeams.Transpose(draft, "pl_1", 0, At));
        Assert.Throws<ArgumentOutOfRangeException>(() => DraftSeams.Tempo(draft, "pl_1", 400, At));
        Assert.Throws<KeyNotFoundException>(() => DraftSeams.Transpose(draft, "pl_9", 2, At));
    }
}