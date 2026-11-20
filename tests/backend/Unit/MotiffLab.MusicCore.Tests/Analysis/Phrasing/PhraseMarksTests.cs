using MotiffLab.MusicCore.Analysis.Phrasing;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis.Phrasing;

/// <summary>
/// The phrase marks' edits on the iPad (the phrases spec, «Engine» and «Player's boundaries»): a mark dragged as one step,
/// a start set, taken away and put back, which found boundary a moved one replaced, how many are the player's own, what
/// an edit changed, and the player's boundaries kept as they are when the notes are read again.
/// </summary>
public class PhraseMarksTests
{
    private const long Quarter = 480;
    private const long Bar = 1920;

    /// <summary>Five bars of 4/4 with boundaries found at bar 3 beat 2 (4320: a slur after a rest) and bar 5 (7680: a slur and a repeat sign).</summary>
    private static readonly MusicXmlImport s_score = MusicXmlImporter.Import(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "phrases-repeats.musicxml")));

    private static readonly Tick s_end = new(5 * Bar);

    private static PhraseMap Phrases(PhraseEdits? edits = null, PhraseMarks? marks = null) =>
        PhraseMap.Suggest(s_score.Notes, s_score.Meter, marks ?? s_score.PhraseMarks, s_end, edits);

    private static long[] Starts(PhraseMap map) => [.. map.Boundaries.Select(static boundary => boundary.At.Value)];

    private static Tick T(long value) => new(value);

    [Fact]
    public void A_mark_dragged_is_one_edit_that_remembers_the_found_boundary_it_replaced()
    {
        PhraseMap found = Phrases();
        PhraseMap moved = found.With(found.Move(T(4320), T(3840)));

        Assert.Equal(new PhraseEdits([T(3840)], [T(4320)]), moved.Edits);
        Assert.Equal([3840L, 7680L], Starts(moved));
        Assert.Equal(Provenance.UserEdited, moved.Boundaries[0].Attribution.Provenance);
        Assert.Equal(T(4320), moved.Replaced[T(3840)]);
        Assert.Equal(1, moved.OwnCount);

        // Dragged back onto the found place, or put back, it is the found boundary again, not one of the player's.
        Assert.Equal(PhraseEdits.None, moved.Move(T(3840), T(4320)));
        Assert.Equal(PhraseEdits.None, moved.PutBack(T(3840)));
        Assert.Equal(0, found.OwnCount);
    }

    [Fact]
    public void An_edit_says_which_phrases_changed_and_the_phrase_that_covers_most_of_each_now()
    {
        PhraseMap found = Phrases();
        PhraseMap moved = found.With(found.Move(T(4320), T(3840)));

        (Phrase Before, Phrase After)[] changed = [.. moved.ChangedFrom(found)];

        Assert.Equal([(1, 1), (2, 2)], changed.Select(static change => (change.Before.Number, change.After.Number)));
        Assert.Equal(TickSpan.FromBounds(Tick.Zero, T(4320)), changed[0].Before.Span);
        Assert.Equal(TickSpan.FromBounds(Tick.Zero, T(3840)), changed[0].After.Span);
        Assert.Empty(found.ChangedFrom(found));
    }

    [Fact]
    public void A_start_is_set_taken_away_and_put_back_and_the_player_s_own_are_counted()
    {
        PhraseMap found = Phrases();

        PhraseMap started = found.With(found.StartAt(T(Bar)));
        Assert.Equal([Bar, 4320L, 7680L], Starts(started));
        Assert.Empty(started.Replaced);
        Assert.Equal(1, started.OwnCount);
        // A start the player set where none was found goes when it is put back.
        Assert.Equal(PhraseEdits.None, started.PutBack(T(Bar)));

        PhraseMap removed = found.With(found.Remove(T(7680)));
        Assert.Equal([4320L], Starts(removed));
        Assert.Equal(1, removed.OwnCount);

        // Two marks dragged, each paired with the found boundary it left.
        PhraseMap twice = found.With(found.Move(T(4320), T(3840)));
        twice = twice.With(twice.Move(T(7680), T(7680 + Quarter)));
        Assert.Equal(T(4320), twice.Replaced[T(3840)]);
        Assert.Equal(T(7680), twice.Replaced[T(7680 + Quarter)]);
        Assert.Equal(2, twice.OwnCount);

        Assert.Throws<ArgumentOutOfRangeException>(() => found.Remove(T(Bar)));
        Assert.Throws<ArgumentOutOfRangeException>(() => found.Move(T(Bar), T(3840)));
    }

    [Fact]
    public void A_start_set_where_one_was_found_leaves_it_found_and_one_confirmed_is_put_back_as_found()
    {
        PhraseMap found = Phrases();

        // «Начать фразу здесь» on a note that already starts a phrase changes nothing: no boundary of the player's.
        Assert.Equal(PhraseEdits.None, found.StartAt(T(4320)));

        // A start the player confirmed where one was found (saved before) is put back by dropping it, the found one kept.
        PhraseMap confirmed = Phrases(new PhraseEdits([T(4320)], []));
        Assert.Equal(Provenance.UserConfirmed, confirmed.Boundaries[0].Attribution.Provenance);
        Assert.Equal(1, confirmed.OwnCount);
        Assert.Equal(PhraseEdits.None, confirmed.PutBack(T(4320)));
        Assert.Equal([4320L, 7680L], Starts(confirmed.With(confirmed.PutBack(T(4320)))));

        // A found start is not the player's to put back.
        Assert.Throws<ArgumentOutOfRangeException>(() => found.PutBack(T(4320)));
    }

    [Fact]
    public void A_mark_dragged_to_the_start_of_the_piece_is_taken_away()
    {
        PhraseMap found = Phrases();

        PhraseMap moved = found.With(found.Move(T(4320), Tick.Zero));

        Assert.Equal(new PhraseEdits([], [T(4320)]), moved.Edits);
        Assert.Equal([7680L], Starts(moved));
    }

    [Fact]
    public void Read_again_from_other_notes_or_marks_only_the_found_boundaries_change_and_the_player_s_stay_where_they_are()
    {
        PhraseMap found = Phrases();
        PhraseMap edited = found.With(found.Move(T(4320), T(3840)));
        edited = edited.With(edited.StartAt(T(6 * Quarter)));

        // The same edits over the notes read without their slurs and repeat signs: other boundaries are found.
        PhraseMap again = Phrases(edited.Edits, PhraseMarks.None);

        Assert.NotEqual(found.Found.Select(static boundary => boundary.At), again.Found.Select(static boundary => boundary.At));
        Assert.Equal(edited.Edits, again.Edits);
        Assert.Contains(again.Boundaries, boundary => boundary.At == T(3840) && boundary.Attribution.Provenance == Provenance.UserEdited);
        Assert.Contains(again.Boundaries, boundary => boundary.At == T(6 * Quarter) && boundary.Attribution.Provenance == Provenance.UserEdited);
    }

    [Fact]
    public void Why_here_names_the_close_before_a_found_boundary_first_in_both_languages()
    {
        PhraseBoundary rest = new(T(4320), [PhraseEvidence.Rest], Attribution.Detected(0.6));
        PhraseBoundary own = new(T(4320), [], Attribution.UserEdited);

        Assert.Equal("after a full close and after a rest of a beat or more", PhraseMap.Describe(rest, Words.English, CloseKind.Full));
        Assert.Equal("после половинной каденции и после паузы в долю или больше", PhraseMap.Describe(rest, Words.Russian, CloseKind.Half));
        Assert.Equal("after a rest of a beat or more", PhraseMap.Describe(rest, Words.English, CloseKind.None));
        Assert.Equal("set by you", PhraseMap.Describe(own, Words.English, CloseKind.Full));
    }

    [Fact]
    public void A_start_snaps_to_the_nearest_note_and_to_the_earlier_of_two_as_near()
    {
        Note[] notes = [Note("a", 0), Note("b", Quarter), Note("c", 1200)];
        PhraseLevels levels = PhraseLevels.Suggest(notes, MeterMap.Default, PhraseMarks.None, T(Bar), null);

        Assert.Equal(T(0), levels.NoteAt(T(Quarter / 2)));
        Assert.Equal(T(Quarter), levels.NoteAt(T((Quarter / 2) + 1)));
        Assert.Equal(T(Quarter), levels.NoteAt(T(Quarter)));
        Assert.Equal(T(1200), levels.NoteAt(T(Bar)));
    }

    [Fact]
    public void A_start_set_from_a_selection_takes_the_note_at_or_after_it_never_one_before()
    {
        Note[] notes = [Note("a", 0), Note("b", 2 * Quarter), Note("c", Bar + (3 * Quarter))];
        PhraseLevels levels = PhraseLevels.Suggest(notes, MeterMap.Default, PhraseMarks.None, T(2 * Bar), null);

        Assert.Equal(T(2 * Quarter), levels.NoteFrom(T(2 * Quarter)));
        // A bar that starts with a rest gives its first note, not the nearer one in the bar before.
        Assert.Equal(T(2 * Quarter), levels.NoteAt(T(Bar)));
        Assert.Equal(T(Bar + (3 * Quarter)), levels.NoteFrom(T(Bar)));
        Assert.Equal(T(Bar + (3 * Quarter) + 1), levels.NoteFrom(T(Bar + (3 * Quarter) + 1)));
    }

    private static Note Note(string id, long onset) => new()
    {
        Id = id,
        Pitch = new Pitch(60),
        Span = new TickSpan(T(onset), Quarter / 2),
        Velocity = 80,
        Track = 0,
        Attribution = Attribution.Imported,
    };
}