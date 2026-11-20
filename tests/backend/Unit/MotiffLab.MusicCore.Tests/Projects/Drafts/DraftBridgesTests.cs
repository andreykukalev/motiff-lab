using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Projects.Drafts;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Projects.Drafts.DraftFixtures;

namespace MotiffLab.MusicCore.Tests.Projects.Drafts;

/// <summary>Bridges at a seam (decision record 0034, section 5; the plan's answer 2): candidates built from harmony where the keys differ, put as a small place between, chosen or removed.</summary>
public class DraftBridgesTests
{
    /// <summary>Bars 1–4 in C major, then four bars of another fragment in <paramref name="fifths"/> (G major by default), its notes moved by <paramref name="semitones"/>.</summary>
    private static Draft Collage(int fifths = 1, int semitones = 0, TimeSignature? meter = null)
    {
        SourceMusic other = Score(fifths: fifths, perMinute: 120, prefix: "o");
        other = other with { Notes = [.. other.Notes.Select(note => note with { Pitch = new Pitch(note.Pitch.MidiNumber + semitones) })] };
        if (meter is { } signature)
        {
            other = SourceMusic.Of(other.Notes, new MeterMap([new MeterChange(Tick.Zero, signature)]), other.End, other.Tempos, other.Keys);
        }

        return Draft.Start("d_1", "Коллаж", At, "pl_1", Variant("v_1", Score(), 1, 4))
            .With(Variant("v_2", other, 1, 4, notes: "n_other"), DraftDrop.Place, "pl_2");
    }

    private static IReadOnlyList<BridgeCandidate> Candidates(Draft draft) => DraftBridges.Of(DraftSeams.Of(draft)[0]);

    /// <summary>What is struck at <paramref name="onset"/>: the left hand's pitches, then the right hand's pitch classes in order, «50 | 0 6 9».</summary>
    private static string Struck(SourceMusic music, long onset)
    {
        Note[] struck = [.. music.Notes.Where(note => note.Span.Onset.Value == onset)];
        return $"{string.Join(' ', struck.Where(note => note.Hand!.Value == Hand.Left).Select(note => note.Pitch.MidiNumber))} | {string.Join(' ', struck.Where(note => note.Hand!.Value == Hand.Right).Select(note => note.Pitch.PitchClass).Order())}";
    }

    [Fact]
    public void Where_the_keys_are_the_same_no_bridge_is_offered()
    {
        Assert.Empty(Candidates(Collage(fifths: 0)));
        Assert.Throws<InvalidOperationException>(() => DraftBridges.Put(Collage(fifths: 0), "pl_2", At));
    }

    [Fact]
    public void Three_candidates_of_one_bar_in_the_meter_tempo_and_key_after_the_seam()
    {
        IReadOnlyList<BridgeCandidate> candidates = Candidates(Collage());

        Assert.Equal([BridgeKind.Dominant, BridgeKind.CommonChord, BridgeKind.BassWalk], candidates.Select(static candidate => candidate.Kind));
        Assert.All(candidates, candidate =>
        {
            Assert.Equal(new AssemblyBar(new TickSpan(Tick.Zero, BarLength), TimeSignature.CommonTime), Assert.Single(candidate.Music.Bars));
            Assert.Equal(500_000, Assert.Single(candidate.Music.Tempos).MicrosecondsPerQuarter);
            Assert.Equal((1, KeyMode.Major), (Assert.Single(candidate.Music.Keys).Fifths, candidate.Music.Keys[0].Mode));
            Assert.All(candidate.Music.Notes, note => Assert.Equal((Provenance.Inferred, Provenance.Inferred), (note.Attribution.Provenance, note.Hand!.Attribution.Provenance)));
            Assert.True(candidate.Music.Notes.Max(static note => note.Span.End.Value) == BarLength);
        });
    }

    [Fact]
    public void The_dominant_is_V7_of_the_new_key_the_common_chord_one_of_both_keys_then_V7()
    {
        IReadOnlyList<BridgeCandidate> candidates = Candidates(Collage());

        // D7 in G major: D in the bass, F♯ A C over it.
        Assert.Equal("50 | 0 6 9", Struck(candidates[0].Music, 0));
        ExplorerChord dominant = HarmonyExplorer.Chords(new MusicalKey(1, KeyMode.Major)).Single(static chord => chord.Id == "V7");
        Assert.Equal(ChordReference.Spell(dominant.Root, dominant.Type).Select(static tone => tone.PitchClass).Order(), candidates[0].Music.Notes.Select(static note => note.Pitch.PitchClass).Distinct().Order());
        // C major is IV in G major and I in C major, over the first two beats; then D7.
        Assert.Equal("48 | 0 4 7", Struck(candidates[1].Music, 0));
        Assert.Equal("50 | 0 6 9", Struck(candidates[1].Music, 2 * Quarter));
        IReadOnlyList<DraftVariant> put = Bridge(Collage()).Variants;
        Assert.Equal("C (IV), аккорд обеих тональностей, затем D7 (V7) в тональности соль мажор", DraftBridges.Explanation(put[1], Words.Russian));
        Assert.Equal("D7 (V7) in G major", DraftBridges.Explanation(put[0], Words.English));
    }

    [Fact]
    public void The_bass_walks_by_step_from_the_last_bass_before_the_seam_to_the_dominant()
    {
        // Into A major from a bass on C3: the dominant E nearest it is E3; up the A major scale from C (a raised B), one beat each.
        BridgeCandidate walk = Candidates(Collage(fifths: 3)).Single(static candidate => candidate.Kind == BridgeKind.BassWalk);

        Note[] bass = [.. walk.Music.Notes.Where(note => note.Hand!.Value == Hand.Left)];
        Assert.Equal([48, 49, 50, 52], bass.Select(static note => note.Pitch.MidiNumber));
        Assert.Equal([0, Quarter, 2 * Quarter, 3 * Quarter], bass.Select(static note => note.Span.Onset.Value));
        Assert.Equal(BarLength, bass[^1].Span.End.Value);
        Assert.Equal("52 | 2 8 11", Struck(walk.Music, 3 * Quarter));
        Assert.Equal("Бас шагами к доминанте: 4 ноты, затем E7 (V7) в тональности ля мажор", DraftBridges.Explanation(Bridge(Collage(fifths: 3)).Variants[^1], Words.Russian));
    }

    [Fact]
    public void A_walk_that_does_not_fit_in_beats_goes_in_shorter_notes()
    {
        // In 2/4 the four notes of the walk into A major go in eighths.
        BridgeCandidate walk = Candidates(Collage(fifths: 3, meter: new TimeSignature(2, 4))).Single(static candidate => candidate.Kind == BridgeKind.BassWalk);

        Assert.Equal([0, 240, 480, 720], walk.Music.Notes.Where(note => note.Hand!.Value == Hand.Left).Select(static note => note.Span.Onset.Value));
    }

    [Fact]
    public void Keys_that_share_no_chord_have_two_candidates_never_more_than_three()
    {
        // C major and E major share no major or minor triad.
        Assert.Equal([BridgeKind.Dominant, BridgeKind.BassWalk], Candidates(Collage(fifths: 4)).Select(static candidate => candidate.Kind));
        Assert.All(new[] { -7, -3, 1, 2, 5, 7 }, fifths => Assert.InRange(Candidates(Collage(fifths)).Count, 2, DraftBridges.Most));
    }

    [Fact]
    public void The_register_follows_the_neighbours()
    {
        IReadOnlyList<BridgeCandidate> low = Candidates(Collage()), high = Candidates(Collage(semitones: 24));

        static int Top(BridgeCandidate candidate) => candidate.Music.Notes.Max(static note => note.Pitch.MidiNumber);
        Assert.Equal(12, Top(high[0]) - Top(low[0]));
        Assert.InRange(Top(low[0]), 69, 77);
    }

    [Fact]
    public void The_same_draft_gives_the_same_candidates()
    {
        static string Notes(Draft draft) => string.Join(" ", draft.Places[1].Variants.SelectMany(static variant => variant.Music.Notes)
            .Select(static note => $"{note.Id}:{note.Pitch.MidiNumber}@{note.Span.Onset.Value}+{note.Span.Duration}"));

        Assert.Equal(Notes(DraftBridges.Put(Collage(), "pl_2", At)), Notes(DraftBridges.Put(Collage(), "pl_2", At)));
    }

    [Fact]
    public void Put_between_the_two_places_as_a_bridge_place_its_first_candidate_the_base()
    {
        Draft draft = DraftBridges.Put(Collage(), "pl_2", At);

        Assert.Equal(["pl_1", "pl_0003", "pl_2"], draft.Places.Select(static place => place.Id));
        DraftPlace bridge = draft.Places[1];
        Assert.True(bridge.Bridge);
        Assert.Equal(["v_0003", "v_0004", "v_0005"], bridge.Variants.Select(static variant => variant.Id));
        Assert.Equal("v_0003", bridge.Base);
        Assert.Equal("② Связка · 3 варианта", DraftLabels.Place(bridge, 1, Words.Russian));
        Assert.Equal(["Доминанта", "Через общий аккорд", "Ход баса"], bridge.Variants.Select(variant => DraftLabels.Variant(variant, Words.Russian)));
        Assert.Equal(("variation", BridgeKind.Dominant, "n_other"), (bridge.BaseVariant.Provenance.Op, DraftBridges.KindOf(bridge.BaseVariant), bridge.BaseVariant.From.Notes));
        Assert.Empty(draft.TechniquesOf("pl_0003"));
        Assert.Equal("из нот с PDF и связки", DraftLabels.Caption(draft, Words.Russian));
    }

    [Fact]
    public void A_bridge_place_is_offered_nothing_its_register_unsaid_and_no_bridge_is_offered_next_to_one()
    {
        IReadOnlyList<DraftSeam> seams = DraftSeams.Of(DraftBridges.Put(Collage(semitones: 36), "pl_2", At));

        Assert.True(seams[0].KeyChanges);
        Assert.Equal((null, null, null, 0), (seams[0].Semitones, seams[0].Tempo, seams[0].Octave, seams[0].Register));
        Assert.All(seams, seam => Assert.Empty(DraftBridges.Of(seam)));
        Assert.False(seams[1].KeyChanges);
    }

    [Fact]
    public void Chosen_a_candidate_is_the_base_and_left_the_place_goes_with_its_steps()
    {
        Draft draft = DraftBridges.Put(Collage(), "pl_2", At);
        Draft chosen = DraftSeams.Transpose(draft.WithBase("pl_0003", "v_0005"), "pl_0003", 12, At);

        Assert.Equal(BridgeKind.BassWalk, DraftBridges.KindOf(chosen.FindPlace("pl_0003")!.BaseVariant));
        Draft left = DraftBridges.Leave(chosen, "pl_0003");
        Assert.Equal(["pl_1", "pl_2"], left.Places.Select(static place => place.Id));
        Assert.Empty(left.Steps);
        Assert.Throws<ArgumentException>(() => DraftBridges.Leave(left, "pl_2"));
        Assert.Throws<ArgumentException>(() => DraftBridges.Put(left, "pl_1", At));
    }

    private static DraftPlace Bridge(Draft draft) => DraftBridges.Put(draft, "pl_2", At).Places[1];
}