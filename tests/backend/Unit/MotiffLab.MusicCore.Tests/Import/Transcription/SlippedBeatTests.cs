using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Import.Transcription;

/// <summary>
/// Issue #455: two bars of an even eighth-note arpeggio written as 16ths, dots and ties, because the beat slipped half a
/// beat while the playing went on evenly. Synthetic playing; the recording it was found in stays out of the repository.
/// </summary>
public class SlippedBeatTests
{
    private const long Eighth = 244_000;

    /// <summary>The pitch of the turns' second 16th, left out when the eighths are counted.</summary>
    private const int Turn = 93;

    /// <summary>
    /// Bars of 4/4 in even eighths, both hands above middle C so no bass keeps the beat, a low note on each beat and a high
    /// one between, with a turn of two 16ths at the end of every bar; from eighth <paramref name="held"/> on, the
    /// player holds back by <paramref name="by"/> microseconds an eighth for two eighths and goes on at the same speed.
    /// </summary>
    private static List<(long Onset, long Length, int Pitch)> Arpeggio(int bars, int held, long by)
    {
        var notes = new List<(long, long, int)>();
        long late = 0;
        for (int i = 0; i < bars * 8; i++)
        {
            late += i == held || i == held + 1 ? by : 0;
            long at = 1_000_000 + (i * Eighth) + late;
            notes.Add((at, Eighth, i % 2 == 0 ? 64 + (i / 8 % 3) : 71));
            notes.Add((at, Eighth, i % 2 == 0 ? 81 : 86 + (i / 8 % 2)));
            if (i % 8 == 7)
            {
                notes.Add((at + (Eighth / 2), Eighth / 2, Turn));
            }
        }

        return notes;
    }

    /// <summary>How many of the arpeggio's onsets fall, on the grid, more than a fifth of a 16th from a beat of its bar (two 16ths here).</summary>
    private static int OffTheBeat(ImportedPiece barred) => barred.Notes
        .Where(note => note.Pitch.MidiNumber != Turn)
        .Select(note => note.Span.Onset.Value - barred.Meter.BarAt(note.Span.Onset).Span.Onset.Value)
        .Count(inBar => Math.Min(inBar % 240, 240 - (inBar % 240)) > 24);

    [Fact]
    public void A_beat_the_player_held_back_from_is_found_again_and_every_eighth_stays_on_a_beat()
    {
        // 62 ms late at the 140th eighth: just past the reach of the beat, as in the recording.
        ImportedPiece piece = BarGridTests.Import(LivePlaying.Heard(Arpeggio(40, 140, 62_000)));
        BarGrid grid = BarGrid.Find(piece.Midi!)!;
        Assert.Equal(2, grid.StepsPerBeat);
        Assert.Equal(TimeSignature.CommonTime, grid.Signature);
        // Only the eighth the player held back on, in both hands, is off its beat; before, every eighth from it on to the
        // next bar line found again was, half a beat away.
        Assert.InRange(OffTheBeat(grid.Apply(piece)), 0, 2);
        Assert.Equal(grid, BarGrid.Find(piece.Midi!));
    }

    [Fact]
    public void Live_the_bars_of_a_bass_on_every_sixth_eighth_are_divided_into_eighths()
    {
        // A section with a bass on every quarter, a pause of 3 s, then one in 3/4 with the bass only on the downbeat.
        var notes = new List<(long, long, int)>();
        long at = 1_000_000;
        for (int i = 0; i < 64; i++, at += 250_000)
        {
            notes.Add((at, 250_000, i % 2 == 0 ? 48 : 67));
            notes.Add((at, 250_000, 76 + (i % 3)));
        }

        at += 3_000_000;
        for (int i = 0; i < 144; i++, at += 250_000)
        {
            notes.Add((at, 250_000, i % 6 == 0 ? 43 : 62 + (i % 6)));
            notes.Add((at, 250_000, 79 + (i % 2)));
        }

        ImportedPiece piece = BarGridTests.Import(LivePlaying.Heard(notes));
        GridFound found = BarGrid.FindLive(piece.Midi!, GridSearch.Live);

        Assert.Equal(2, found.Sections);
        Assert.Equal(2, found.Grid!.StepsPerBeat);
        // The grid's 16th is the playing's eighth here: every onset on one.
        Assert.Equal(0, found.Grid.Apply(piece).Notes.Count(note => Math.Min(note.Span.Onset.Value % 120, 120 - (note.Span.Onset.Value % 120)) > 24));
    }
}