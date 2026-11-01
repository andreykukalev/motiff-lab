using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Invitations;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Invitations;

/// <summary>The invitation catalogue: facts to invitations (#79).</summary>
public class InvitationCatalogueTests
{
    private const long Sixteenth = 120;
    private const long Eighth = 240;
    private const long Quarter = 480;
    private const long Bar = 4 * Quarter;

    private static readonly KeySignature s_cMajor = new(Tick.Zero, 0, KeyMode.Major);
    private static readonly KeySignature s_aMinor = new(Tick.Zero, 0, KeyMode.Minor);
    private static readonly CharacterContext s_character = new([new PlaybackTempo(Tick.Zero, 500_000)], TempoAssumed: false, FromNotation: false);

    private static Note Note(string id, int pitch, long onset, long duration, int track = 0) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = track,
        Attribution = Attribution.Imported,
    };

    /// <summary>Quarter notes in 4/4, one array per bar; a 0 pitch is a rest.</summary>
    private static Note[] Quarters(params int[][] bars) =>
        [.. bars.SelectMany((pitches, bar) => pitches
            .Select((pitch, beat) => (pitch, beat))
            .Where(entry => entry.pitch > 0)
            .Select(entry => Note($"b{bar + 1}.{entry.beat + 1}", entry.pitch, (bar * Bar) + (entry.beat * Quarter), Quarter)))];

    /// <summary>
    /// The opening of Für Elise in 3/8 with its 16th pickup, bars 1–8: the pilot piece. The right
    /// hand is track 0 and the left hand track 1; the pickup and bars 1–2 come back exactly from the
    /// end of bar 4.
    /// </summary>
    private static (Note[] Notes, MeterMap Meter) FurElise()
    {
        var meter = new MeterMap([new MeterChange(Tick.Zero, new TimeSignature(3, 8))], 2 * Sixteenth);
        var notes = new List<Note>();
        long at = 0;
        void Right(params int[] pitches)
        {
            foreach (int pitch in pitches)
            {
                notes.Add(Note($"r{notes.Count}", pitch, at, Sixteenth));
                at += Sixteenth;
            }
        }

        // A bar of an 8th in the right hand over a rising left-hand arpeggio, then three right-hand 16ths.
        void Arpeggio(int melody, int[] left, int[] right)
        {
            notes.Add(Note($"r{notes.Count}", melody, at, Eighth));
            for (int i = 0; i < left.Length; i++)
            {
                notes.Add(Note($"l{notes.Count}", left[i], at + (i * Sixteenth), Sixteenth, track: 1));
            }

            at += 3 * Sixteenth;
            Right(right);
        }

        void Opening()
        {
            Right(76, 75);
            Right(76, 75, 76, 71, 74, 72);
            Arpeggio(69, [45, 52, 57], [60, 64, 69]);
        }

        Opening();
        Arpeggio(71, [40, 52, 56], [64, 68, 71]);
        Arpeggio(72, [45, 52, 57], [64]);
        Opening();
        Arpeggio(71, [40, 52, 56], [64, 72, 71]);
        notes.Add(Note($"r{notes.Count}", 69, at, Quarter));
        notes.Add(Note($"l{notes.Count}", 45, at, Quarter, track: 1));
        return ([.. notes], meter);
    }

    private static InvitationContext Context(Note[] notes, MeterMap meter, long from, long to, KeySignature? key = null, SelectionFilter? filter = null)
    {
        MotifSelection selection = MotifSelector.Select(notes, meter, TickSpan.FromBounds(new Tick(from), new Tick(to)), filter ?? SelectionFilter.All);
        return new InvitationContext(
            notes,
            meter,
            selection,
            MotifAnalyzer.Analyze(notes, meter, selection, key, phrases: []),
            StructureAnalyzer.Analyze(notes, meter, selection, []),
            MotifAnalyzer.AnalyzeCharacter(notes, meter, selection, key, s_character));
    }

    private static Invitation? Find(IReadOnlyList<Invitation> invitations, string ruleId) =>
        invitations.SingleOrDefault(invitation => invitation.RuleId == ruleId);

    [Fact]
    public void The_pilot_piece_gets_its_listening_invitations()
    {
        (Note[] notes, MeterMap meter) = FurElise();
        IReadOnlyList<Invitation> invitations = InvitationCatalogue.Invite(Context(notes, meter, 0, meter.GetBar(2).Span.End.Value, s_aMinor));

        Assert.Equal(["repeat.exact", "rhythm.running", "rhythm.pickup", "melody.mirror"], invitations.Select(static invitation => invitation.RuleId));
        Assert.All(invitations.Take(3), static invitation => Assert.Equal(ActionAvailability.AvailableNow, invitation.Availability));
    }

    [Fact]
    public void An_exact_repeat_compares_the_stretch_from_each_appearance()
    {
        (Note[] notes, MeterMap meter) = FurElise();
        Invitation invitation = Find(InvitationCatalogue.Invite(Context(notes, meter, 0, meter.GetBar(2).Span.End.Value, s_aMinor)), "repeat.exact")!;

        Assert.Equal(InvitationKind.Noticing, invitation.Kind);
        Assert.Equal(["structure.recurrences"], invitation.FactIds);
        Assert.Equal("The motif's line in the pickup and bars 1–2 comes back exactly in bars 4–6. Where does the music stop being the same?", invitation.Question);
        ListeningAction action = Assert.IsType<ListeningAction>(invitation.Action);
        Assert.Equal(ListeningKind.Compare, action.Kind);
        Assert.Equal([(0, 3), (4, 7)], action.Passages.Select(static passage => (passage.FirstBar, passage.LastBar)));
        Assert.Equal(meter.GetBar(4).Span.Onset, action.Passages[1].Span.Onset);
        Assert.Equal(meter.GetBar(7).Span.End, action.Passages[1].Span.End);
    }

    [Fact]
    public void An_exact_repeat_in_quarters_compares_up_to_the_repeat()
    {
        Note[] song = Quarters([60, 62, 64, 60], [65, 64, 62, 60], [60, 62, 64, 60], [67, 65, 64, 62]);
        Invitation invitation = Find(InvitationCatalogue.Invite(Context(song, MeterMap.Default, 0, Bar, s_cMajor)), "repeat.exact")!;

        Assert.Equal("The motif's line in bar 1 comes back exactly in bar 3. Where does the music stop being the same?", invitation.Question);
        Assert.Equal([(1, 2), (3, 4)], ((ListeningAction)invitation.Action).Passages.Select(static passage => (passage.FirstBar, passage.LastBar)));
    }

    [Fact]
    public void Running_16ths_are_followed_slower_through_the_phrase()
    {
        (Note[] notes, MeterMap meter) = FurElise();
        Invitation invitation = Find(InvitationCatalogue.Invite(Context(notes, meter, 0, meter.GetBar(2).Span.End.Value, s_aMinor)), "rhythm.running")!;

        Assert.Equal(["rhythm.grid", "character.density"], invitation.FactIds);
        Assert.Equal("A note starts every 16th through the pickup and bars 1–2, with no gap. Played slower, does the movement ever stop before the end of bar 7?", invitation.Question);
        ListeningAction action = Assert.IsType<ListeningAction>(invitation.Action);
        Assert.Equal(ListeningKind.Slow, action.Kind);
        Assert.Equal(InvitationCatalogue.SlowTempoPercent, action.TempoPercent);
        Assert.Equal((0, 7), (action.Passages[0].FirstBar, action.Passages[0].LastBar));
    }

    [Fact]
    public void Notes_with_gaps_or_off_the_grid_are_not_running_notes()
    {
        (Note[] notes, MeterMap meter) = FurElise();
        long end = meter.GetBar(2).Span.End.Value;
        Note[] played = [.. notes.Select(note => note.Id == "r3" ? note with { Span = new TickSpan(note.Span.Onset.Offset(7), note.Span.Duration) } : note)];

        Assert.Null(Find(InvitationCatalogue.Invite(Context(notes, meter, 0, end, s_aMinor, SelectionFilter.ForTrack(0))), "rhythm.running"));
        Assert.Null(Find(InvitationCatalogue.Invite(Context(played, meter, 0, end, s_aMinor)), "rhythm.running"));
    }

    [Fact]
    public void A_pickup_loops_the_opening_and_the_next_bar()
    {
        (Note[] notes, MeterMap meter) = FurElise();
        Invitation invitation = Find(InvitationCatalogue.Invite(Context(notes, meter, 0, meter.GetBar(2).Span.End.Value, s_aMinor)), "rhythm.pickup")!;

        Assert.Equal(["rhythm.grid"], invitation.FactIds);
        Assert.Equal("The motif starts in the pickup, before the bar line of bar 1. How many of its notes do you need to recognise it?", invitation.Question);
        ListeningAction action = Assert.IsType<ListeningAction>(invitation.Action);
        Assert.Equal(ListeningKind.Loop, action.Kind);
        Assert.Equal((0, 1), (action.Passages[0].FirstBar, action.Passages[0].LastBar));
    }

    [Fact]
    public void A_motif_that_starts_after_the_downbeat_gets_the_pickup_question()
    {
        Note[] song = Quarters([0, 0, 64, 65], [67, 65, 64, 62]);
        Invitation invitation = Find(InvitationCatalogue.Invite(Context(song, MeterMap.Default, 2 * Quarter, 2 * Bar, s_cMajor)), "rhythm.pickup")!;

        Assert.Equal("The motif starts on beat 3 of bar 1, before the bar line of bar 2. How many of its notes do you need to recognise it?", invitation.Question);
        Assert.Null(Find(InvitationCatalogue.Invite(Context(song, MeterMap.Default, Bar, 2 * Bar, s_cMajor)), "rhythm.pickup"));
    }

    [Fact]
    public void An_even_melody_in_quarters_offers_a_dotted_rhythm()
    {
        Note[] song = Quarters([60, 62, 64, 65], [67, 65, 64, 62]);
        Invitation invitation = Find(InvitationCatalogue.Invite(Context(song, MeterMap.Default, 0, Bar, s_cMajor)), "rhythm.even")!;

        Assert.Equal(InvitationKind.Experiment, invitation.Kind);
        Assert.Equal(["rhythm.durations", "melody.contour"], invitation.FactIds);
        Assert.Equal("The melody moves in even quarters. Is the tune in its pitches or in its even flow?", invitation.Question);
        Assert.Equal(new OperationAction("V1", "dotted", new RhythmReshapeParameters(RhythmReshapeKind.Dotted), "keep-pitch-order"), invitation.Action);
        Assert.Equal(ActionAvailability.AvailableNow, invitation.Availability);
    }

    [Fact]
    public void The_pilot_s_even_16ths_offer_a_dotted_rhythm_but_not_once_the_melody_holds_an_8th()
    {
        (Note[] notes, MeterMap meter) = FurElise();
        Invitation invitation = Find(InvitationCatalogue.Invite(Context(notes, meter, 0, meter.GetBar(1).Span.End.Value, s_aMinor)), "rhythm.even")!;

        Assert.Null(Find(InvitationCatalogue.Invite(Context(notes, meter, 0, meter.GetBar(2).Span.End.Value, s_aMinor)), "rhythm.even"));
        Assert.Equal(new OperationAction("V1", "dotted", new RhythmReshapeParameters(RhythmReshapeKind.Dotted), "keep-pitch-order"), invitation.Action);
        Assert.Equal("The melody moves in even 16ths. Is the tune in its pitches or in its even flow?", invitation.Question);
    }

    [Fact]
    public void A_melody_that_moves_both_ways_offers_a_mirror()
    {
        Note[] song = Quarters([60, 62, 64, 62], [60, 0, 0, 0]);
        Invitation invitation = Find(InvitationCatalogue.Invite(Context(song, MeterMap.Default, 0, 2 * Bar, s_cMajor)), "melody.mirror")!;

        Assert.Equal(["melody.contour"], invitation.FactIds);
        Assert.Equal("The melody moves up twice and down twice. Does the motif survive moving the other way?", invitation.Question);
        Assert.Equal(new OperationAction("V10", "mirror the whole motif", new ChangeDirectionParameters(Diatonic: true), "keep-rhythm"), invitation.Action);
        Assert.Equal(ActionAvailability.AvailableNow, invitation.Availability);
    }

    [Fact]
    public void A_transposed_repeat_offers_a_sequence_in_its_direction()
    {
        Note[] song = Quarters([60, 62, 64, 60], [60, 62, 64, 60], [64, 65, 67, 67], [65, 67, 69, 65]);
        Invitation invitation = Find(InvitationCatalogue.Invite(Context(song, MeterMap.Default, 0, Bar, s_cMajor)), "repeat.transposed")!;

        Assert.Equal(["structure.recurrences", "key"], invitation.FactIds);
        Assert.Equal("The motif comes back a perfect 4th higher in bar 4. What does it sound like one scale step higher, straight after itself?", invitation.Question);
        Assert.Equal(new OperationAction("V4", "1 scale step higher, once", new MelodicSequenceParameters(1), "keep-rhythm"), invitation.Action);
        Assert.Equal(ActionAvailability.AvailableNow, invitation.Availability);
    }

    [Fact]
    public void A_motif_above_middle_C_offers_an_octave_lower_and_needs_a_Should_item()
    {
        Note[] song = Quarters([72, 74, 76, 72]);
        Invitation invitation = Find(InvitationCatalogue.Invite(Context(song, MeterMap.Default, 0, Bar, s_cMajor)), "character.register")!;

        Assert.Equal(["character.register"], invitation.FactIds);
        Assert.Equal("Every note of the motif is at or above middle C. What stays the same if it moves an octave lower?", invitation.Question);
        Assert.Equal(new OperationAction("V3", "octave shift down", null, "keep-melody"), invitation.Action);
        Assert.Equal(ActionAvailability.NeedsShouldItem, invitation.Availability);
        Assert.Equal("Voicing and register is a Should item of Stage 1; chord detection (S1-A3) is in.", ActionStatus.Note(invitation.Action));
    }

    [Fact]
    public void A_motif_below_middle_C_offers_an_octave_higher()
    {
        Invitation invitation = Find(InvitationCatalogue.Invite(Context(Quarters([48, 50, 52, 48]), MeterMap.Default, 0, Bar, s_cMajor)), "character.register")!;

        Assert.Equal("Every note of the motif is below middle C. What stays the same if it moves an octave higher?", invitation.Question);
        Assert.Equal("octave shift up", ((OperationAction)invitation.Action).Option);
    }

    [Fact]
    public void A_motif_with_nothing_confident_gets_no_invitations()
    {
        // Two played notes either side of middle C, off every grid: too short to repeat or mirror, and uncertain in rhythm.
        Note[] played = [Note("a", 59, 7, 200), Note("b", 61, 251, 200)];

        Assert.Empty(InvitationCatalogue.Invite(Context(played, MeterMap.Default, 0, Bar)));
    }

    [Fact]
    public void An_uncertain_fact_silences_the_rules_that_rest_on_it()
    {
        Note[] song = Quarters([0, 0, 64, 65], [67, 65, 64, 62]);
        Note[] played = [.. song.Select(note => note.Id == "b2.2" ? note with { Span = new TickSpan(note.Span.Onset.Offset(9), note.Span.Duration) } : note)];
        InvitationContext context = Context(played, MeterMap.Default, 2 * Quarter, 2 * Bar, s_cMajor);

        Assert.Equal(FactCertainty.Uncertain, context.Motif.Facts.Single(fact => fact.Id == "rhythm.grid").Certainty);
        Assert.Null(Find(InvitationCatalogue.Invite(context), "rhythm.pickup"));
    }

    [Fact]
    public void Inviting_is_deterministic()
    {
        (Note[] notes, MeterMap meter) = FurElise();
        InvitationContext context = Context(notes, meter, 0, meter.GetBar(2).Span.End.Value, s_aMinor);

        Assert.Equal(InvitationCatalogue.Invite(context), InvitationCatalogue.Invite(context));
        Assert.Equal(InvitationCatalogue.Invite(context), InvitationCatalogue.Invite(context with { }));
    }

    [Fact]
    public void Every_invitation_cites_facts_of_the_motif_and_a_known_preset()
    {
        (Note[] notes, MeterMap meter) = FurElise();
        Note[][] pieces = [Quarters([60, 62, 64, 65], [67, 65, 64, 62]), Quarters([72, 74, 76, 72]), Quarters([60, 62, 64, 60], [60, 62, 64, 60], [64, 65, 67, 67], [65, 67, 69, 65])];
        InvitationContext[] contexts = [Context(notes, meter, 0, meter.GetBar(2).Span.End.Value, s_aMinor), .. pieces.Select(piece => Context(piece, MeterMap.Default, 0, Bar, s_cMajor))];

        foreach (InvitationContext context in contexts)
        {
            string[] facts = [.. context.Motif.Facts.Concat(context.Structure.Facts).Concat(context.Character).Select(static fact => fact.Id)];
            foreach (Invitation invitation in InvitationCatalogue.Invite(context))
            {
                Assert.NotEmpty(invitation.FactIds);
                Assert.All(invitation.FactIds, id => Assert.Contains(id, facts));
                if (invitation.Action is OperationAction operation)
                {
                    KeepPreset preset = Assert.IsType<KeepPreset>(KeepTable.FindPreset(operation.KeepPresetId));
                    Assert.Contains(operation.OperationId, preset.Operations);
                }
            }
        }
    }

    [Fact]
    public void Availability_comes_from_one_table_covering_the_catalogue()
    {
        Assert.All(["V1", "V13", "V7", "V8", "V2", "V12", "V4", "V9", "V10", "V11", "V14", "V15"], id => Assert.Equal(ActionAvailability.AvailableNow, ActionStatus.Of(id)));
        Assert.All(["V3", "V6"], id => Assert.Equal(ActionAvailability.NeedsShouldItem, ActionStatus.Of(id)));
        Assert.Equal(ActionAvailability.LaterStage, ActionStatus.Of("V5"));
        Assert.All(KeepTable.Rows, row => Assert.Contains(row.OperationId, ActionStatus.OperationIds));
        Assert.Equal(ActionAvailability.AvailableNow, ActionStatus.Of(new ListeningAction(ListeningKind.Loop, [])));
        Assert.Throws<ArgumentException>(() => ActionStatus.Of("V99"));
    }
}