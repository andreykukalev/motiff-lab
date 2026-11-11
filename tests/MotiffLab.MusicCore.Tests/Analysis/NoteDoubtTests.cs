using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>Doubt about recognised notes reaches the chords, keys and facts built on them (0027 section 12, #352).</summary>
public class NoteDoubtTests
{
    private static readonly MusicalKey s_cMajor = new(0, KeyMode.Major);

    /// <summary>The cadence of <see cref="HarmonyAnalyzerTests.Cadence"/> as heard in a recording: every note detected.</summary>
    private static Note[] Heard() => [.. HarmonyAnalyzerTests.Cadence().Select(note => note with { Attribution = Attribution.Detected(0.4) })];

    private static MotifSelection All(Note[] notes) =>
        MotifSelector.Select(notes, MeterMap.Default, TickSpan.FromBounds(Tick.Zero, new Tick(notes.Max(note => note.Span.End.Value))), SelectionFilter.All);

    private static int BarOf(Note note) => MeterMap.Default.BarAt(note.Span.Onset).Number;

    [Fact]
    public void Recognised_notes_in_a_bar_with_an_open_flag_are_doubtful_and_others_are_not()
    {
        Note[] notes = Heard();
        int second = BarOf(notes.Single(note => note.Id == "f1"));

        NoteDoubt doubt = NoteDoubt.Of(notes, MeterMap.Default, [second]);

        Assert.Equal(["f1", "f2", "f3", "m2"], doubt.NoteIds);
        Assert.Equal([second], doubt.Bars);
    }

    [Fact]
    public void Imported_fixed_and_confirmed_notes_are_never_doubtful()
    {
        Note[] notes = HarmonyAnalyzerTests.Cadence();
        int[] every = [.. notes.Select(BarOf).Distinct()];
        Assert.True(NoteDoubt.Of(notes, MeterMap.Default, every).IsEmpty);

        Note[] checkedNotes = [.. Heard().Select(note => note.Id switch
        {
            "f1" => note with { Attribution = Attribution.UserEdited },
            "f2" => note with { Attribution = Attribution.UserConfirmed },
            _ => note,
        })];
        Assert.Equal(["f3", "m2"], NoteDoubt.Of(checkedNotes, MeterMap.Default, [BarOf(checkedNotes[4])]).NoteIds);
    }

    [Fact]
    public void A_chord_on_doubtful_notes_is_uncertain_and_says_where_the_others_are_as_read()
    {
        Note[] notes = Heard();
        MotifSelection selection = All(notes);
        int second = BarOf(notes.Single(note => note.Id == "f1"));
        HarmonyAnalysis read = HarmonyAnalyzer.Analyze(notes, MeterMap.Default, selection, s_cMajor);

        HarmonyAnalysis doubted = NoteDoubt.Of(notes, MeterMap.Default, [second]).On(read, selection);

        Assert.Equal(["C", "F/C", "G7/B", "C"], doubted.Chords.Select(chord => chord.Symbol(s_cMajor)));
        ChordSpan f = doubted.Chords[1];
        Assert.True(f.IsUncertain);
        Assert.Equal($"Rests on recognised notes still to check in bar {second}.", f.Doubt);
        Assert.All(new[] { doubted.Chords[0], doubted.Chords[2], doubted.Chords[3] }, chord => Assert.Null(chord.Doubt));
        // The reading itself does not change: the same chords, confidences and alternatives.
        Assert.Equal(read.Chords.Select(chord => (chord.Span, chord.Chord, chord.Confidence)), doubted.Chords.Select(chord => (chord.Span, chord.Chord, chord.Confidence)));
    }

    [Fact]
    public void A_fact_on_doubtful_notes_turns_uncertain_and_says_so_and_a_fact_without_them_is_unchanged()
    {
        Note[] notes = Heard();
        MotifSelection selection = All(notes);
        int second = BarOf(notes.Single(note => note.Id == "f1"));
        HarmonyAnalysis read = HarmonyAnalyzer.Analyze(notes, MeterMap.Default, selection, s_cMajor);

        HarmonyAnalysis doubted = NoteDoubt.Of(notes, MeterMap.Default, [second]).On(read, selection);

        MotifFact chords = doubted.Facts.Single(fact => fact.Id == "harmony.chords");
        Assert.Equal(FactCertainty.Uncertain, chords.Certainty);
        Assert.Equal($"Rests on recognised notes still to check in bar {second}.", chords.Doubt);
        Assert.Equal(read.Facts.Single(fact => fact.Id == "harmony.chords").Text, chords.Text);

        // A fact whose places name only other bars rests on no doubtful note.
        var other = new MotifFact("test.place", FactLens.Harmony, FactCertainty.Inferred, "A place in the last bar.", "Measured.")
        {
            Places = [FactPlace.Of("bar 4", notes.Where(note => note.Id is "e1" or "e2"))],
        };
        Assert.Same(other, NoteDoubt.Of(notes, MeterMap.Default, [second]).On(other, selection.NoteIds));
    }

    [Fact]
    public void A_key_read_from_doubtful_notes_is_uncertain_but_a_key_the_file_states_is_not()
    {
        Note[] notes = Heard();
        MotifSelection selection = All(notes);
        NoteDoubt doubt = NoteDoubt.Of(notes, MeterMap.Default, [BarOf(notes[0])]);

        Assert.Equal(FactCertainty.Uncertain, doubt.On(new KeyReading(s_cMajor, FactCertainty.Inferred, null), selection)!.Certainty);
        Assert.Equal(FactCertainty.Detected, doubt.On(new KeyReading(s_cMajor, FactCertainty.Detected, null), selection)!.Certainty);
        MotifAnalysis motif = doubt.On(MotifAnalyzer.Analyze(notes, MeterMap.Default, selection, null), selection);
        Assert.Equal(FactCertainty.Uncertain, motif.Key!.Certainty);
    }

    [Fact]
    public void Resolving_the_flag_clears_the_doubt()
    {
        Note[] notes = Heard();
        MotifSelection selection = All(notes);
        HarmonyAnalysis read = HarmonyAnalyzer.Analyze(notes, MeterMap.Default, selection, s_cMajor);

        // "Looks right": the bar has no open flag any more.
        NoteDoubt resolved = NoteDoubt.Of(notes, MeterMap.Default, []);

        Assert.True(resolved.IsEmpty);
        Assert.Same(read, resolved.On(read, selection));
        // Every doubtful note of the chord fixed: it rests on no note to check.
        Note[] fixedNotes = [.. notes.Select(note => note.Id is "f1" or "f2" or "f3" or "m2" ? note with { Attribution = Attribution.UserEdited } : note)];
        HarmonyAnalysis again = NoteDoubt.Of(fixedNotes, MeterMap.Default, [BarOf(notes[4])]).On(HarmonyAnalyzer.Analyze(fixedNotes, MeterMap.Default, selection, s_cMajor), selection);
        Assert.All(again.Chords, chord => Assert.Null(chord.Doubt));
    }

    [Fact]
    public void The_doubt_is_deterministic_and_said_in_russian()
    {
        Note[] notes = Heard();
        MotifSelection selection = All(notes);
        int[] bars = [BarOf(notes[0]), BarOf(notes[4])];
        HarmonyAnalysis read = HarmonyAnalyzer.Analyze(notes, MeterMap.Default, selection, s_cMajor, Words.Russian);

        HarmonyAnalysis once = NoteDoubt.Of(notes, MeterMap.Default, bars, Words.Russian).On(read, selection);
        HarmonyAnalysis twice = NoteDoubt.Of([.. notes.Reverse()], MeterMap.Default, [.. bars.Reverse()], Words.Russian).On(read, selection);

        Assert.Equal(once.Chords, twice.Chords);
        Assert.Equal(once.Facts, twice.Facts);
        Assert.Equal($"Опирается на распознанные ноты, которые ещё нужно проверить: такты {bars[0]} и {bars[1]}.", once.Facts.Single(fact => fact.Id == "harmony.chords").Doubt);
    }

    [Fact]
    public void Piece_dna_draws_a_chord_on_doubtful_notes_as_uncertain()
    {
        Note[] notes = Heard();
        int second = BarOf(notes.Single(note => note.Id == "f1"));
        PieceDna dna = PieceDna.Build(notes, MeterMap.Default, [], new Tick(notes.Max(note => note.Span.End.Value)), null, null);

        PieceDna doubted = NoteDoubt.Of(notes, MeterMap.Default, [second]).On(dna);

        Assert.NotEmpty(doubted.Harmony);
        Assert.All(doubted.Harmony, chord =>
        {
            Assert.Equal(chord.Bar == second, chord.Doubt is not null);
            Assert.Equal(dna.Harmony.Single(read => read.Bar == chord.Bar).Clear && chord.Bar != second, chord.Clear);
        });
        Assert.Same(dna, NoteDoubt.None.On(dna));
    }
}