using System.Xml.Linq;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Notation;

public class NotationGeneratorTests
{
    private static readonly XNamespace s_mei = "http://www.music-encoding.org/ns/mei";
    private static readonly XNamespace s_xml = XNamespace.Xml;

    private static Note Note(string id, int pitch, long onset, long duration, int track = 0, Hand? hand = null) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = track,
        Hand = hand is { } value ? new Attributed<Hand>(value, Attribution.Imported) : null,
        Attribution = Attribution.Imported,
    };

    private static MeterMap Meter(int numerator, int denominator, long pickup = 0) =>
        new([new MeterChange(Tick.Zero, new TimeSignature(numerator, denominator))], pickup);

    private static (GeneratedNotation Result, XDocument Mei) Generate(IEnumerable<Note> notes, MeterMap? meter = null, long end = 1920, params KeyChange[] keys)
    {
        GeneratedNotation result = NotationGenerator.Generate(notes, meter ?? MeterMap.Default, keys, new Tick(end));
        return (result, XDocument.Parse(result.Mei));
    }

    private static XElement[] Measures(XDocument mei) => [.. mei.Descendants(s_mei + "measure")];

    private static XElement ById(XDocument mei, string id) =>
        Assert.Single(mei.Descendants(), element => (string?)element.Attribute(s_xml + "id") == id);

    /// <summary>Written length of an element in ticks, from @dur and @dots.</summary>
    private static long Ticks(XElement element)
    {
        long ticks = 1920 / int.Parse((string)element.Attribute("dur")!);
        int dots = (int?)element.Attribute("dots") ?? 0;
        return ticks + (dots == 1 ? ticks / 2 : 0);
    }

    [Fact]
    public void A_whole_bar_note_is_a_whole_note_with_the_shared_id()
    {
        (GeneratedNotation result, XDocument mei) = Generate([Note("n_0001", 60, 0, 1920)]);

        XElement note = ById(mei, "n_0001");
        Assert.Equal(("c", "4", "1"), ((string)note.Attribute("pname")!, (string)note.Attribute("oct")!, (string)note.Attribute("dur")!));
        Assert.Single(Measures(mei));
        Assert.Equal((1, 0, 0), (result.NoteCount, result.ApproximatedNoteCount, result.ShortenedNoteCount));
        NotationStaff staff = Assert.Single(result.Staves);
        Assert.Equal(new NotationStaff(1, 0, Clef.Treble, null, false), staff);
    }

    [Fact]
    public void Score_definition_carries_meter_key_and_clef()
    {
        (_, XDocument mei) = Generate([Note("a", 48, 0, 1440)], Meter(3, 4), 1440, new KeyChange(Tick.Zero, -3));

        XElement scoreDef = mei.Descendants(s_mei + "scoreDef").First();
        Assert.Equal(("3", "4", "3f"), ((string)scoreDef.Attribute("meter.count")!, (string)scoreDef.Attribute("meter.unit")!, (string)scoreDef.Attribute("keysig")!));
        XElement staffDef = Assert.Single(mei.Descendants(s_mei + "staffDef"));
        Assert.Equal(("F", "4"), ((string)staffDef.Attribute("clef.shape")!, (string)staffDef.Attribute("clef.line")!));
    }

    [Fact]
    public void Empty_complete_bars_get_a_measure_rest()
    {
        (_, XDocument mei) = Generate([Note("a", 60, 0, 480)], end: 3840);

        XElement[] measures = Measures(mei);
        Assert.Equal(2, measures.Length);
        Assert.Single(measures[1].Descendants(s_mei + "mRest"));
        Assert.Equal(["4", "4", "2"], measures[0].Descendants().Where(e => e.Name == s_mei + "note" || e.Name == s_mei + "rest").Select(e => (string)e.Attribute("dur")!));
    }

    [Fact]
    public void A_pickup_is_an_incomplete_bar_zero_counted_from_the_end_of_a_full_bar()
    {
        // One-beat pickup in 3/4, then a bar of a dotted half.
        (_, XDocument mei) = Generate([Note("a", 67, 0, 480), Note("b", 72, 480, 1440)], Meter(3, 4, pickup: 480), 1920);

        XElement[] measures = Measures(mei);
        Assert.Equal(("0", "false"), ((string)measures[0].Attribute("n")!, (string)measures[0].Attribute("metcon")!));
        Assert.Equal("4", (string)ById(mei, "a").Attribute("dur")!);
        Assert.Empty(measures[0].Descendants(s_mei + "rest"));
        Assert.Equal(("1", null), ((string)measures[1].Attribute("n")!, (string?)measures[1].Attribute("metcon")));
        Assert.Equal(("2", "1"), ((string)ById(mei, "b").Attribute("dur")!, (string)ById(mei, "b").Attribute("dots")!));
    }

    [Fact]
    public void Notes_across_a_bar_line_are_tied_and_the_continuation_gets_a_suffix()
    {
        (_, XDocument mei) = Generate([Note("n_0001", 62, 1440, 960)], end: 3840);

        XElement first = ById(mei, "n_0001");
        XElement second = ById(mei, "n_0001-2");
        Assert.Equal(("4", "i"), ((string)first.Attribute("dur")!, (string)first.Attribute("tie")!));
        Assert.Equal(("4", "t"), ((string)second.Attribute("dur")!, (string)second.Attribute("tie")!));
        Assert.NotSame(first.Ancestors(s_mei + "measure").Single(), second.Ancestors(s_mei + "measure").Single());
    }

    [Fact]
    public void A_note_that_does_not_start_on_the_beat_is_split_at_the_beat()
    {
        // A half note from beat 1½ is written eighth, quarter, eighth, tied.
        (_, XDocument mei) = Generate([Note("a", 60, 240, 960)]);

        Assert.Equal(["8", "4", "8"], new[] { "a", "a-2", "a-3" }.Select(id => (string)ById(mei, id).Attribute("dur")!));
        Assert.Equal(["i", "m", "t"], new[] { "a", "a-2", "a-3" }.Select(id => (string)ById(mei, id).Attribute("tie")!));
    }

    [Fact]
    public void Compound_meters_take_dotted_quarters_on_the_second_pulse()
    {
        (_, XDocument mei) = Generate([Note("a", 60, 0, 720), Note("b", 62, 720, 720)], Meter(6, 8), 1440);

        Assert.All(new[] { "a", "b" }, id => Assert.Equal(("4", "1"), ((string)ById(mei, id).Attribute("dur")!, (string)ById(mei, id).Attribute("dots")!)));
    }

    [Fact]
    public void Eighths_are_beamed_within_the_beat()
    {
        Note[] notes = [.. Enumerable.Range(0, 8).Select(i => Note($"e{i}", 60 + i, i * 240, 240))];
        (_, XDocument mei) = Generate(notes);

        XElement[] beams = [.. mei.Descendants(s_mei + "beam")];
        Assert.Equal(4, beams.Length);
        Assert.All(beams, beam => Assert.Equal(2, beam.Elements(s_mei + "note").Count()));
    }

    [Fact]
    public void Eighths_in_six_eight_are_beamed_in_threes()
    {
        Note[] notes = [.. Enumerable.Range(0, 6).Select(i => Note($"e{i}", 60, i * 240, 240))];
        (_, XDocument mei) = Generate(notes, Meter(6, 8), 1440);

        Assert.Equal([3, 3], mei.Descendants(s_mei + "beam").Select(beam => beam.Elements().Count()));
    }

    [Fact]
    public void Notes_that_start_and_end_together_are_one_chord()
    {
        (_, XDocument mei) = Generate([Note("n1", 64, 0, 1920), Note("n2", 60, 0, 1920), Note("n3", 67, 0, 1920)]);

        XElement chord = Assert.Single(mei.Descendants(s_mei + "chord"));
        Assert.Equal("c_n2", (string)chord.Attribute(s_xml + "id")!);
        Assert.Equal(["n2", "n1", "n3"], chord.Elements(s_mei + "note").Select(note => (string)note.Attribute(s_xml + "id")!));
    }

    [Fact]
    public void A_held_note_under_moving_notes_gets_its_own_voice_below()
    {
        Note[] notes = [Note("held", 55, 0, 1920), .. Enumerable.Range(0, 4).Select(i => Note($"m{i}", 67 + i, i * 480, 480))];
        (GeneratedNotation result, XDocument mei) = Generate(notes);

        XElement[] layers = [.. mei.Descendants(s_mei + "layer")];
        Assert.Equal(2, layers.Length);
        Assert.Equal(4, layers[0].Elements(s_mei + "note").Count());
        Assert.Equal("held", (string)layers[1].Element(s_mei + "note")!.Attribute(s_xml + "id")!);
        Assert.Equal(0, result.ShortenedNoteCount);
    }

    [Fact]
    public void More_than_four_overlapping_voices_shorten_a_note_and_count_it()
    {
        // Five notes starting a quarter apart, each lasting the whole bar from its start.
        Note[] notes = [.. Enumerable.Range(0, 5).Select(i => Note($"v{i}", 60 + (i * 2), i * 240, 1920 - (i * 240)))];
        (GeneratedNotation result, XDocument mei) = Generate(notes);

        Assert.Equal(1, result.ShortenedNoteCount);
        Assert.Equal(4, mei.Descendants(s_mei + "layer").Count());
    }

    [Fact]
    public void Lower_voices_use_spaces_not_rests()
    {
        Note[] notes = [Note("top", 72, 0, 1920), Note("low", 60, 960, 960)];
        (_, XDocument mei) = Generate(notes);

        XElement lower = mei.Descendants(s_mei + "layer").Single(layer => (string)layer.Attribute("n")! == "2");
        Assert.Equal(["space", "note"], lower.Elements().Select(element => element.Name.LocalName));
    }

    [Fact]
    public void Off_grid_notes_are_shown_at_the_nearest_32nd_and_counted()
    {
        (GeneratedNotation result, XDocument mei) = Generate([Note("a", 60, 10, 470), Note("b", 62, 480, 480), Note("c", 64, 960, 5)]);

        Assert.Equal(2, result.ApproximatedNoteCount);
        Assert.Equal("4", (string)ById(mei, "a").Attribute("dur")!);
        Assert.Equal("32", (string)ById(mei, "c").Attribute("dur")!);
    }

    [Fact]
    public void Accidentals_are_written_once_per_bar_and_again_after_the_bar_line()
    {
        Note[] notes = [Note("a", 61, 0, 480), Note("b", 61, 480, 480), Note("c", 60, 960, 960), Note("d", 61, 1920, 1920)];
        (_, XDocument mei) = Generate(notes, end: 3840);

        Assert.Equal(("c", "s", null), Spelling(ById(mei, "a")));
        Assert.Equal(("c", null, "s"), Spelling(ById(mei, "b")));
        Assert.Equal(("c", "n", null), Spelling(ById(mei, "c")));
        Assert.Equal(("c", "s", null), Spelling(ById(mei, "d")));
    }

    [Fact]
    public void Pitches_are_spelled_from_the_key_signature()
    {
        Note[] notes = [Note("fs", 66, 0, 480), Note("f", 65, 480, 480), Note("bf", 70, 960, 480), Note("gs", 68, 1440, 480)];

        (_, XDocument sharp) = Generate(notes, keys: new KeyChange(Tick.Zero, 2));
        Assert.Equal(("f", null, "s"), Spelling(ById(sharp, "fs")));
        Assert.Equal(("f", "n", null), Spelling(ById(sharp, "f")));
        Assert.Equal(("a", "s", null), Spelling(ById(sharp, "bf")));
        Assert.Equal(("g", "s", null), Spelling(ById(sharp, "gs")));

        (_, XDocument flat) = Generate(notes, keys: new KeyChange(Tick.Zero, -1));
        Assert.Equal(("g", "f", null), Spelling(ById(flat, "fs")));
        Assert.Equal(("b", null, "f"), Spelling(ById(flat, "bf")));
        Assert.Equal(("a", "f", null), Spelling(ById(flat, "gs")));
    }

    [Fact]
    public void Spelling_keeps_the_written_octave_for_c_flat_and_b_sharp()
    {
        Assert.Equal(new SpelledPitch(0, -1, 4), PitchSpeller.Spell(59, -7));
        Assert.Equal(new SpelledPitch(6, 1, 3), PitchSpeller.Spell(60, 7));
        Assert.Equal(new SpelledPitch(4, 0, 4), PitchSpeller.Spell(67, 7));
    }

    [Fact]
    public void Tied_continuations_do_not_repeat_the_accidental()
    {
        (_, XDocument mei) = Generate([Note("a", 61, 1440, 960)], end: 3840);

        Assert.Equal(("c", "s", null), Spelling(ById(mei, "a")));
        Assert.Equal(("c", null, "s"), Spelling(ById(mei, "a-2")));
    }

    [Fact]
    public void Key_and_meter_changes_are_written_before_their_bar()
    {
        var meter = new MeterMap([new MeterChange(Tick.Zero, TimeSignature.CommonTime), new MeterChange(new Tick(1920), new TimeSignature(3, 4))]);
        (_, XDocument mei) = Generate([Note("a", 60, 0, 1920), Note("b", 62, 1920, 1440)], meter, 3360, new KeyChange(new Tick(1920), 1));

        XElement change = Assert.Single(mei.Descendants(s_mei + "section").Single().Elements(s_mei + "scoreDef"));
        Assert.Equal(("3", "4", "1s"), ((string)change.Attribute("meter.count")!, (string)change.Attribute("meter.unit")!, (string)change.Attribute("keysig")!));
        Assert.Equal("measure", ((XElement)change.NextNode!).Name.LocalName);
        Assert.Equal("2", (string)((XElement)change.NextNode!).Attribute("n")!);
    }

    [Fact]
    public void A_single_wide_track_is_split_at_middle_c_and_marked_inferred()
    {
        (GeneratedNotation result, XDocument mei) = Generate([Note("low", 43, 0, 1920), Note("mid", 60, 0, 1920), Note("high", 76, 0, 1920)]);

        Assert.Equal([new NotationStaff(1, 0, Clef.Treble, Hand.Right, true), new NotationStaff(2, 0, Clef.Bass, Hand.Left, true)], result.Staves);
        Assert.Equal("1", (string)ById(mei, "c_mid").Ancestors(s_mei + "staff").Single().Attribute("n")!);
        Assert.Equal("2", (string)ById(mei, "low").Ancestors(s_mei + "staff").Single().Attribute("n")!);
        XElement brace = mei.Descendants(s_mei + "staffGrp").Single(group => group.Attribute("symbol") is not null);
        Assert.Equal("brace", (string)brace.Attribute("symbol")!);
    }

    [Fact]
    public void Imported_hands_decide_the_staff_whatever_the_pitch()
    {
        (GeneratedNotation result, XDocument mei) = Generate([Note("r", 55, 0, 1920, hand: Hand.Right), Note("l", 64, 0, 1920, hand: Hand.Left)]);

        Assert.All(result.Staves, staff => Assert.False(staff.IsSplitInferred));
        Assert.Equal("1", (string)ById(mei, "r").Ancestors(s_mei + "staff").Single().Attribute("n")!);
        Assert.Equal("2", (string)ById(mei, "l").Ancestors(s_mei + "staff").Single().Attribute("n")!);
    }

    [Fact]
    public void Several_tracks_get_one_staff_each_with_a_clef_for_their_range()
    {
        (GeneratedNotation result, _) = Generate([Note("a", 72, 0, 1920, track: 0), Note("b", 40, 0, 1920, track: 1), Note("c", 76, 0, 1920, track: 1)]);

        Assert.Equal([(0, Clef.Treble), (1, Clef.Treble)], result.Staves.Select(staff => (staff.Track, staff.Clef)));
        (GeneratedNotation bass, _) = Generate([Note("a", 72, 0, 1920, track: 0), Note("b", 40, 0, 1920, track: 1)]);
        Assert.Equal(Clef.Bass, bass.Staves[1].Clef);
    }

    [Fact]
    public void Generation_is_deterministic_whatever_the_input_order()
    {
        Note[] notes = [Note("a", 60, 0, 480), Note("b", 64, 0, 480), Note("c", 67, 480, 1440), Note("d", 48, 0, 1920)];

        string first = Generate(notes).Result.Mei;
        string reversed = Generate(notes.Reverse()).Result.Mei;
        Assert.Equal(first, reversed);
    }

    [Fact]
    public void An_empty_piece_is_one_bar_of_rest()
    {
        (GeneratedNotation result, XDocument mei) = Generate([], end: 0);

        Assert.Single(Measures(mei));
        Assert.Single(mei.Descendants(s_mei + "mRest"));
        Assert.Single(result.Staves);
    }

    public static TheoryData<string> Fixtures() =>
    [
        "Midi/Fixtures/two-hands-type1.mid",
        "Midi/Fixtures/piano-single-track.mid",
        "Midi/Fixtures/performance-type1.mid",
        "Midi/Fixtures/performance-click.mid",
        "Midi/Fixtures/free-tempo.mid",
        "Midi/Fixtures/no-time-signature.mid",
        "Midi/Fixtures/large-5000-notes.mid",
        "MusicXml/Fixtures/grand-staff.musicxml",
        "MusicXml/Fixtures/importer-grand-staff.musicxml",
        "MusicXml/Fixtures/ties-triplets-pickup.musicxml",
        "MusicXml/Fixtures/septuplets.musicxml",
        "MusicXml/Fixtures/importer-two-parts.musicxml",
        "MusicXml/Fixtures/phrases-repeats.musicxml",
    ];

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Every_layer_of_every_bar_fills_the_bar_and_every_note_is_written(string fixture)
    {
        byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", fixture));
        (GeneratedNotation result, IReadOnlyList<Note> notes, MeterMap meter) = fixture.EndsWith(".mid", StringComparison.Ordinal)
            ? Midi(MidiImporter.Import(data))
            : MusicXml(MusicXmlImporter.Import(data));
        XDocument mei = XDocument.Parse(result.Mei);

        foreach (XElement measure in Measures(mei))
        {
            long barTicks = meter.GetBar(int.Parse((string)measure.Attribute("n")!)).Span.Duration;
            foreach (XElement layer in measure.Descendants(s_mei + "layer"))
            {
                if (layer.Element(s_mei + "mRest") is not null)
                {
                    continue;
                }

                long filled = layer.Descendants()
                    .Where(element => element.Attribute("dur") is not null && element.Parent!.Name != s_mei + "chord")
                    .Sum(Ticks);
                Assert.Equal(barTicks, filled);
            }
        }

        HashSet<string> written = [.. mei.Descendants(s_mei + "note").Select(note => (string)note.Attribute(s_xml + "id")!)];
        Assert.All(notes, note => Assert.Contains(note.Id, written));
        Assert.Equal(notes.Count, result.NoteCount);

        static (GeneratedNotation, IReadOnlyList<Note>, MeterMap) Midi(MidiImport import) => (NotationGenerator.FromMidi(import), import.Notes, import.Meter);

        static (GeneratedNotation, IReadOnlyList<Note>, MeterMap) MusicXml(MusicXmlImport import) => (NotationGenerator.FromMusicXml(import), import.Notes, import.Meter);
    }

    [Fact]
    public void Held_gaps_write_a_note_heard_in_a_recording_up_to_the_next_one()
    {
        // A quarter let go a 16th early, then a quarter with a quarter's rest after it, then the last note.
        Note[] notes = [Note("n_0001", 72, 0, 360), Note("n_0002", 74, 480, 480), Note("n_0003", 76, 1440, 480)];

        GeneratedNotation held = NotationGenerator.Generate(notes, MeterMap.Default, [], new Tick(1920), asHeard: true);
        GeneratedNotation asPlayed = NotationGenerator.Generate(notes, MeterMap.Default, [], new Tick(1920));

        XDocument mei = XDocument.Parse(held.Mei);
        Assert.Equal(1, held.LengthenedNoteCount);
        Assert.Equal("4", (string)ById(mei, "n_0001").Attribute("dur")!);
        Assert.Equal("4", (string)ById(mei, "n_0002").Attribute("dur")!);
        Assert.Equal(0, asPlayed.LengthenedNoteCount);
        Assert.Equal("8", (string)ById(XDocument.Parse(asPlayed.Mei), "n_0001").Attribute("dur")!);
    }

    [Fact]
    public void Held_gaps_close_only_on_the_same_staff()
    {
        // A short bass note is followed by a treble note: the bass staff has no next onset to hold to.
        Note[] notes = [Note("n_0001", 40, 0, 240), Note("n_0002", 76, 300, 240), Note("n_0003", 80, 1440, 240)];

        GeneratedNotation held = NotationGenerator.Generate(notes, MeterMap.Default, [], new Tick(1920), asHeard: true);

        Assert.Equal(0, held.LengthenedNoteCount);
    }

    [Fact]
    public void Written_for_reading_a_chord_has_one_length_and_the_pedal_is_cut_at_the_next_onset()
    {
        // A chord whose notes are let go at different times, then a note held by the pedal past the next one.
        Note[] notes = [Note("n_0001", 72, 0, 450), Note("n_0002", 76, 10, 300), Note("n_0003", 79, 480, 1200), Note("n_0004", 81, 960, 960)];

        GeneratedNotation held = NotationGenerator.Generate(notes, MeterMap.Default, [], new Tick(1920), asHeard: true);

        XDocument mei = XDocument.Parse(held.Mei);
        XElement chord = Assert.Single(mei.Descendants(s_mei + "chord"));
        Assert.Equal("4", (string)chord.Attribute("dur")!);
        Assert.Equal(["n_0001", "n_0002"], chord.Elements(s_mei + "note").Select(note => (string)note.Attribute(s_xml + "id")!).Order());
        Assert.Equal("4", (string)ById(mei, "n_0003").Attribute("dur")!);
        Assert.Equal(1, held.CutNoteCount);
        Assert.Single(mei.Descendants(s_mei + "layer"), layer => layer.Parent!.Attribute("n")!.Value == "1");
    }

    [Fact]
    public void Written_for_reading_onsets_go_to_the_nearest_16th_and_a_very_short_note_before_the_next_is_a_grace_note()
    {
        // A grace note played 50 ticks before a note that starts a little late.
        Note[] notes = [Note("n_0001", 74, 450, 40), Note("n_0002", 72, 500, 400), Note("n_0003", 71, 960, 960)];

        GeneratedNotation held = NotationGenerator.Generate(notes, MeterMap.Default, [], new Tick(1920), asHeard: true);

        XDocument mei = XDocument.Parse(held.Mei);
        Assert.Equal(1, held.GraceNoteCount);
        Assert.Equal("acc", (string)ById(mei, "n_0001").Attribute("grace")!);
        Assert.Equal("4", (string)ById(mei, "n_0002").Attribute("dur")!);
        Assert.True(held.WrittenAsHeard);
    }

    [Fact]
    public void Written_as_played_the_score_marks_dynamics_accents_and_tempo_words()
    {
        // Six bars of quarters: soft with one loud note in bar 1, loud from bar 3, and the last two bars slower.
        Note[] notes = [.. Enumerable.Range(0, 24).Select(i => Note($"n_{i + 1:D4}", 72 + (i % 5), i * 480L, 480) with
        {
            Velocity = i == 1 ? 110 : i < 8 ? 40 : 90,
        })];
        PlaybackTempo[] tempos = [new(Tick.Zero, 500_000), new(new Tick(4 * 1920), 700_000)];

        GeneratedNotation played = NotationGenerator.Generate(notes, MeterMap.Default, [], new Tick(6 * 1920), playedTempos: tempos);

        XDocument mei = XDocument.Parse(played.Mei);
        XElement[] measures = Measures(mei);
        Assert.Equal("p", (string)Assert.Single(measures[0].Elements(s_mei + "dynam")));
        Assert.Equal("f", (string)Assert.Single(measures[2].Elements(s_mei + "dynam")));
        Assert.Equal(2, played.DynamicCount);
        Assert.Equal("acc", (string)Assert.Single(mei.Descendants(s_mei + "artic")).Attribute("artic")!);
        Assert.Equal("n_0002", (string)Assert.Single(mei.Descendants(s_mei + "artic")).Parent!.Attribute(s_xml + "id")!);
        Assert.Equal("rit.", Assert.Single(mei.Descendants(s_mei + "dir")).Value);
        Assert.Single(measures[4].Elements(s_mei + "dir"));
        Assert.False(played.WrittenAsHeard);
    }

    [Fact]
    public void Without_the_played_tempos_nothing_is_marked()
    {
        Note[] notes = [.. Enumerable.Range(0, 8).Select(i => Note($"n_{i + 1:D4}", 72, i * 480L, 480) with { Velocity = i == 1 ? 110 : 40 })];

        (GeneratedNotation result, XDocument mei) = Generate(notes, end: 3840);

        Assert.Empty(mei.Descendants(s_mei + "dynam"));
        Assert.Empty(mei.Descendants(s_mei + "artic"));
        Assert.Equal(0, result.AccentCount);
    }

    private static (string Name, string? Accid, string? Gestural) Spelling(XElement note) =>
        ((string)note.Attribute("pname")!, (string?)note.Attribute("accid"), (string?)note.Attribute("accid.ges"));
}