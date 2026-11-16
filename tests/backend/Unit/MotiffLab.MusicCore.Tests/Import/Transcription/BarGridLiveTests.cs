using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Import.Transcription;

/// <summary>The beat of a live performance found afresh in each section (version 0.5, «Темп живого исполнения»).</summary>
public class BarGridLiveTests
{
    /// <summary>Pieces played one after the other, each at its own 16th, each followed by a pause in microseconds; with when each starts.</summary>
    private static (ImportedPiece Piece, long[] Starts) Concert(params (long Sixteenth, IEnumerable<(int, int, int)> Notes, long PauseAfter)[] pieces)
    {
        var notes = new List<(long, long, int)>();
        var starts = new List<long>();
        long at = 1_000_000;
        foreach ((long sixteenth, IEnumerable<(int, int, int)> played, long pause) in pieces)
        {
            starts.Add(at);
            notes.AddRange(LivePlaying.At(at, sixteenth, played));
            at += (LivePlaying.Length(played) * sixteenth) + pause;
        }

        return (BarGridTests.Import(LivePlaying.Heard(notes)), [.. starts]);
    }

    /// <summary>The typical 16th of the grid's beats from <paramref name="from"/> to <paramref name="to"/>, microseconds of the recording.</summary>
    private static long SixteenthBetween(BarGrid grid, long from, long to)
    {
        long origin = grid.Origin.Value * 500_000 / 480;
        long[] beats = [.. grid.Beats.Where(beat => beat + origin >= from && beat + origin < to)];
        long[] gaps = [.. beats.Zip(beats.Skip(1), (a, b) => b - a).Order()];
        return gaps[gaps.Length / 2] / grid.StepsPerBeat;
    }

    /// <summary>Whether every note of <paramref name="pitch"/> starts a bar once the grid is applied.</summary>
    private static bool OnDownbeats(BarGrid grid, ImportedPiece piece, int pitch)
    {
        ImportedPiece barred = grid.Apply(piece);
        return barred.Notes.Where(note => note.Pitch.MidiNumber == pitch)
            .All(note => Math.Abs(note.Span.Onset.Value - barred.Meter.BarAt(note.Span.Onset).Span.Onset.Value) <= 60
                || Math.Abs(note.Span.Onset.Value - barred.Meter.BarAt(note.Span.Onset).Span.End.Value) <= 60);
    }

    [Fact]
    public void Two_pieces_at_their_own_tempos_after_a_pause_keep_their_bars()
    {
        (ImportedPiece piece, long[] starts) = Concert((150_000, BarGridTests.Waltz(), 3_000_000), (220_000, BarGridTests.Waltz(), 0));

        GridFound found = BarGrid.FindLive(piece.Midi!, GridSearch.Live);

        Assert.Equal(2, found.Sections);
        BarGrid grid = found.Grid!;
        Assert.Equal(new TimeSignature(3, 4), grid.Signature);
        Assert.InRange(SixteenthBetween(grid, starts[0], starts[1] - 3_000_000), 145_000, 155_000);
        Assert.InRange(SixteenthBetween(grid, starts[1], long.MaxValue), 213_000, 227_000);
        Assert.True(OnDownbeats(grid, piece, 48));
    }

    [Fact]
    public void A_beat_that_sticks_at_the_edge_of_the_tempo_is_found_again()
    {
        // Chords on every beat, the bass lowest on every fourth, slowing from 0.3 s to 0.7 s a beat without a pause, then
        // steady: past 8/5 of where it started.
        var notes = new List<(long, long, int)>();
        var chords = new List<long>();
        long at = 1_000_000;
        for (int k = 0; k < 96; k++)
        {
            long beat = Math.Min(700_000, 300_000 + (6_250L * k));
            notes.Add((at, beat, k % 4 == 0 ? 36 : 48));
            notes.Add((at, beat, 64));
            chords.Add(at);
            at += beat;
        }

        ImportedPiece piece = BarGridTests.Import(LivePlaying.Heard(notes));
        GridFound found = BarGrid.FindLive(piece.Midi!, GridSearch.Live);

        Assert.Equal(2, found.Sections);
        long[] late = [.. chords.Skip(56)];
        Assert.InRange(OnBeats(found.Grid!, late), 85, 100);

        // The single grid's beat stays within 8/5 of where it started, but since issue #455 it finds the chords again each
        // time they slip off it.
        Assert.InRange(OnBeats(BarGrid.Find(piece.Midi!)!, late), 85, 100);
    }

    /// <summary>The percentage of <paramref name="onsets"/>, microseconds of the recording, that fall within 30 ms of a beat of the grid.</summary>
    private static int OnBeats(BarGrid grid, long[] onsets)
    {
        long origin = grid.Origin.Value * 500_000 / 480;
        long[] beats = [.. grid.Beats.Select(beat => beat + origin)];
        return 100 * onsets.Count(onset => beats.Any(beat => Math.Abs(onset - beat) < 30_000)) / onsets.Length;
    }

    [Fact]
    public void A_piece_with_no_pause_and_a_steady_tempo_is_one_section_as_before()
    {
        foreach (MidiImport import in new[] { BarGridTests.Import(LivePlaying.Heard(LivePlaying.At(1_000_000, 150_000, BarGridTests.Waltz()))).Midi!, BarGridTests.Import(LivePlaying.Heard(LivePlaying.At(1_000_000, 166_667, BarGridTests.March()))).Midi! })
        {
            GridFound found = BarGrid.FindLive(import, GridSearch.Live);

            Assert.Equal(1, found.Sections);
            Assert.Equal(BarGrid.Find(import), found.Grid);
            Assert.Null(found.HintFits);
        }
    }

    [Fact]
    public void A_starting_point_given_starts_a_section_there()
    {
        (ImportedPiece piece, long[] starts) = Concert((150_000, BarGridTests.Waltz(), 1_000_000), (150_000, BarGridTests.Waltz(), 0));
        Assert.Equal(1, BarGrid.FindLive(piece.Midi!, GridSearch.Live).Sections);

        GridFound found = BarGrid.FindLive(piece.Midi!, GridSearch.Live with { Starts = [starts[1]] });

        Assert.Equal(2, found.Sections);
        Assert.True(OnDownbeats(found.Grid!, piece, 48));
    }

    [Fact]
    public void A_scores_signature_is_taken_only_when_the_recording_supports_it()
    {
        MidiImport waltz = BarGridTests.Import(LivePlaying.Heard(LivePlaying.At(1_000_000, 150_000, BarGridTests.Waltz()))).Midi!;

        GridFound fits = BarGrid.FindLive(waltz, GridSearch.Live with { Hint = new GridHint(new TimeSignature(3, 4), 100) });
        GridFound not = BarGrid.FindLive(waltz, GridSearch.Live with { Hint = new GridHint(TimeSignature.CommonTime, null) });

        Assert.True(fits.HintFits);
        Assert.Equal(new TimeSignature(3, 4), fits.Grid!.Signature);
        Assert.False(not.HintFits);
        Assert.Equal(new TimeSignature(3, 4), not.Grid!.Signature);
    }

    [Fact]
    public void Too_few_notes_give_no_grid_and_finding_is_deterministic()
    {
        MidiImport few = BarGridTests.Import(LivePlaying.Heard(LivePlaying.At(1_000_000, 150_000, BarGridTests.Waltz().Take(8)))).Midi!;
        (ImportedPiece piece, _) = Concert((150_000, BarGridTests.Waltz(), 3_000_000), (220_000, BarGridTests.Waltz(), 0));

        Assert.Equal(GridFound.None, BarGrid.FindLive(few, GridSearch.Live));
        Assert.Equal(BarGrid.FindLive(piece.Midi!, GridSearch.Live), BarGrid.FindLive(piece.Midi!, GridSearch.Live));
        Assert.Equal(BarGrid.FindLive(piece.Midi!, GridSearch.Live).Grid, BarGrid.Find(piece.Midi!, search: GridSearch.Live));
    }
}