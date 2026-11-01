using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Motifs;

public class MotifTests
{
    private const long Beat = 480;
    private const long Bar = 4 * Beat;

    private static Note Note(string id, int pitch, long onset, long duration = Beat, int track = 0, int? voice = null, Hand? hand = null) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = track,
        Voice = voice,
        Hand = hand is { } value ? new Attributed<Hand>(value, Attribution.Imported) : null,
        Attribution = Attribution.Imported,
        Source = new TestLocation(),
    };

    private static MotifSelection Range(IEnumerable<Note> notes, long from, long to, SelectionFilter? filter = null) =>
        MotifSelector.Select(notes, MeterMap.Default, TickSpan.FromBounds(new Tick(from), new Tick(to)), filter ?? SelectionFilter.All);

    private static Motif Create(IReadOnlyList<Note> notes, MotifSelection selection, MotifSelectionKind kind = MotifSelectionKind.Range) =>
        Motif.Create("m_0001", 1, selection, kind, MotifMode.Discovery, notes);

    [Fact]
    public void A_motif_holds_ids_range_filter_name_source_and_mode()
    {
        Note[] notes = [Note("a", 60, 4 * Bar), Note("b", 62, (4 * Bar) + Beat), Note("c", 64, 5 * Bar, track: 1), Note("d", 65, 7 * Bar)];
        MotifSelection selection = Range(notes, 4 * Bar, 6 * Bar, SelectionFilter.ForTrack(0));

        Motif motif = Motif.Create("m_0007", 3, selection, MotifSelectionKind.Range, MotifMode.Guided, notes);

        Assert.Equal("m_0007", motif.Id);
        Assert.Equal("Motif 3 · bars 5–6", motif.Name);
        Assert.Equal(["a", "b"], motif.NoteIds);
        Assert.Equal(TickSpan.FromBounds(new Tick(4 * Bar), new Tick(6 * Bar)), motif.Span);
        Assert.Equal(SelectionFilter.ForTrack(0), motif.Filter);
        Assert.Equal(MotifMode.Guided, motif.Mode);
        Assert.Equal(MotifSelectionKind.Range, motif.Kind);

        // Show in piece (S1-M4): bars 5–6, and the first note's place in the file.
        Assert.Equal(new MotifSourceReference(motif.Span, 5, 6, notes[0].Source), motif.Source);
    }

    [Fact]
    public void The_player_can_rename_a_motif_and_change_its_mode()
    {
        Note[] notes = [Note("a", 60, 0), Note("b", 62, Beat)];
        Motif motif = Create(notes, Range(notes, 0, Bar));

        Motif renamed = motif with { Name = "  Opening call  ", Mode = MotifMode.Analysis };

        Assert.Equal("Motif 1 · bar 1", motif.Name);
        Assert.Equal(("Opening call", MotifMode.Analysis), (renamed.Name, renamed.Mode));
        Assert.Throws<ArgumentException>(() => motif with { Name = " " });
    }

    [Fact]
    public void Default_names_follow_the_n3_copy()
    {
        Assert.Equal("Motif 1 · bars 5–6", Motif.DefaultName(1, 5, 6));
        Assert.Equal("Motif 2 · bar 0", Motif.DefaultName(2, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Motif.DefaultName(0, 1, 1));
    }

    [Fact]
    public void An_empty_selection_cannot_become_a_motif()
    {
        Note[] notes = [Note("a", 60, 0)];
        MotifSelection empty = Range(notes, Bar, 2 * Bar);

        Assert.Throws<ArgumentException>(() => Create(notes, empty));
    }

    [Fact]
    public void Selecting_notes_allows_two_to_thirty_two_and_snaps_the_range_to_beats()
    {
        Note[] notes = [.. Enumerable.Range(0, 40).Select(i => Note($"n{i:D2}", 60 + (i % 12), (Bar / 2) + 120 + (i * 120), 120))];

        MotifSelection two = MotifSelector.SelectNotes(notes, MeterMap.Default, ["n02", "n00"]);
        Assert.Equal(["n00", "n02"], two.NoteIds);

        // n00 starts at 1080 (in beat 3 of bar 1) and n02 ends at 1440: the range is [960, 1440).
        Assert.Equal(TickSpan.FromBounds(new Tick(960), new Tick(1440)), two.Span);
        Assert.Equal((1, 1), (two.FirstBar, two.LastBar));
        Assert.Equal(SelectionFilter.All, two.Filter);

        Assert.Equal(32, MotifSelector.SelectNotes(notes, MeterMap.Default, notes.Take(32).Select(note => note.Id)).NoteIds.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => MotifSelector.SelectNotes(notes, MeterMap.Default, ["n01"]));
        Assert.Throws<ArgumentOutOfRangeException>(() => MotifSelector.SelectNotes(notes, MeterMap.Default, notes.Take(33).Select(note => note.Id)));
        Assert.Throws<ArgumentException>(() => MotifSelector.SelectNotes(notes, MeterMap.Default, ["n01", "n01"]));
        Assert.Throws<ArgumentException>(() => MotifSelector.SelectNotes(notes, MeterMap.Default, ["n01", "missing"]));
    }

    [Fact]
    public void Selected_notes_span_at_most_the_bars_a_selection_holds()
    {
        Note[] notes = [Note("a", 60, 0), Note("b", 62, 31 * Bar), Note("c", 64, 32 * Bar)];

        Assert.Equal((1, 32), (MotifSelector.SelectNotes(notes, MeterMap.Default, ["a", "b"]).FirstBar, MotifSelector.SelectNotes(notes, MeterMap.Default, ["a", "b"]).LastBar));
        Assert.Throws<ArgumentOutOfRangeException>(() => MotifSelector.SelectNotes(notes, MeterMap.Default, ["a", "c"]));
    }

    [Fact]
    public void A_motif_of_single_notes_keeps_the_note_count_rule()
    {
        Note[] notes = [Note("a", 60, 0), Note("b", 62, Beat)];
        MotifSelection range = Range(notes, 0, Bar);
        MotifSelection one = range with { NoteIds = ["a"] };

        Assert.Equal(MotifSelectionKind.Notes, Create(notes, MotifSelector.SelectNotes(notes, MeterMap.Default, ["a", "b"]), MotifSelectionKind.Notes).Kind);
        Assert.Throws<ArgumentException>(() => Create(notes, one, MotifSelectionKind.Notes));
        Assert.Single(Create(notes, one).NoteIds); // a range may hold a single note
    }

    [Fact]
    public void A_voice_filter_keeps_one_notated_voice_of_one_track()
    {
        Note[] notes =
        [
            Note("melody", 72, 0, track: 0, voice: 1),
            Note("inner", 64, 0, track: 0, voice: 2),
            Note("other", 72, 0, track: 1, voice: 1),
            Note("midi", 60, 0, track: 0),
        ];

        Assert.Equal(["melody"], Range(notes, 0, Bar, SelectionFilter.ForVoice(0, 1)).NoteIds);
        Assert.Equal(["inner"], Range(notes, 0, Bar, SelectionFilter.ForVoice(0, 2)).NoteIds);
        Assert.Throws<ArgumentOutOfRangeException>(() => SelectionFilter.ForVoice(0, 0));
    }

    [Fact]
    public void A_single_line_is_all_melody_as_a_fact()
    {
        Note[] notes = [Note("a", 60, 0), Note("b", 64, Beat), Note("c", 62, 2 * Beat)];

        MotifVoiceSplit split = MotifVoices.Split(notes, Create(notes, Range(notes, 0, Bar)));

        Assert.Equal(["a", "b", "c"], split.Melody);
        Assert.Empty(split.Others);
        Assert.Equal(Attribution.Imported, split.Attribution);
    }

    [Fact]
    public void The_melody_is_the_top_line_and_accompaniment_under_a_held_note_is_not_melody()
    {
        Note[] notes =
        [
            Note("m1", 76, 0, 2 * Beat), // held over the next beat
            Note("bass", 48, 0, Bar),
            Note("inner", 64, Beat), // starts under the held E5
            Note("m2", 74, 2 * Beat),
            Note("chord", 67, 2 * Beat),
            Note("m3", 72, 3 * Beat),
        ];

        MotifVoiceSplit split = MotifVoices.Split(notes, Create(notes, Range(notes, 0, Bar)));

        Assert.Equal(["m1", "m2", "m3"], split.Melody);
        Assert.Equal(["bass", "inner", "chord"], split.Others);
        Assert.Equal(Attribution.Inferred(MotifVoices.SplitConfidence), split.Attribution);
    }

    [Fact]
    public void With_hands_the_melody_comes_from_the_right_hand()
    {
        Note[] notes =
        [
            Note("r1", 72, 0, Beat, hand: Hand.Right),
            Note("l1", 48, 0, Bar, hand: Hand.Left),
            Note("l2", 55, Beat, Beat, hand: Hand.Left), // the right hand rests here
            Note("r2", 74, 2 * Beat, Beat, hand: Hand.Right),
        ];

        MotifVoiceSplit split = MotifVoices.Split(notes, Create(notes, Range(notes, 0, Bar)));

        Assert.Equal(["r1", "r2"], split.Melody);
        Assert.Equal(["l1", "l2"], split.Others);
    }

    [Fact]
    public void A_grand_staff_motif_splits_into_melody_and_accompaniment()
    {
        MusicXmlImport score = MusicXmlImporter.Import(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "grand-staff.musicxml")));
        MotifSelection selection = MotifSelector.Select(score.Notes, score.Meter, MotifSelector.Bars(score.Meter, 1, 2, score.End), SelectionFilter.All);
        Motif motif = Motif.Create("m_0001", 1, selection, MotifSelectionKind.Range, MotifMode.Discovery, score.Notes);

        MotifVoiceSplit split = MotifVoices.Split(score.Notes, motif);
        MotifVoiceSplit again = MotifVoices.Split(score.Notes.Reverse(), motif);

        Assert.Equal(motif.NoteIds.Order(StringComparer.Ordinal), split.Melody.Concat(split.Others).Order(StringComparer.Ordinal));
        Assert.All(split.Melody, id => Assert.Equal(Hand.Right, score.Notes.Single(note => note.Id == id).Hand!.Value));
        Assert.Contains(split.Others, id => score.Notes.Single(note => note.Id == id).Hand!.Value == Hand.Left);
        Assert.IsType<MusicXmlSourceLocation>(motif.Source.FirstNote);
        Assert.Equal(split.Melody, again.Melody);
        Assert.Equal(split.Others, again.Others);
    }

    /// <summary>Stands in for a file location, so the source reference has something to carry.</summary>
    private sealed record TestLocation : SourceLocation;
}