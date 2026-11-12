using System.Text;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Import.Transcription;

/// <summary>Where a recording's bars read better in an odd signature (version 0.5, «Смена размера в записи», a preview).</summary>
public class OddMetersTests
{
    /// <summary>
    /// Bars of <paramref name="size"/> 16ths from the 16th <paramref name="start"/>, a melody in 16ths over a bass on each
    /// beat of the bar's grouping (<paramref name="beats"/>, in 16ths from the downbeat), the lowest on the downbeat.
    /// </summary>
    private static IEnumerable<(int, int, int)> Bars(int start, int count, int size, int[] beats)
    {
        int[] melody = [72, 74, 76, 77, 79, 77, 76, 74, 72, 71, 72, 74];
        for (int bar = 0; bar < count; bar++)
        {
            int at = start + (bar * size);
            foreach (int beat in beats)
            {
                yield return (at + beat, 3, beat == 0 ? 36 : 43);
            }

            for (int i = 0; i < size; i++)
            {
                yield return (at + i, 1, melody[i % melody.Length]);
            }
        }
    }

    private static readonly int[] s_common = [0, 4, 8, 12];

    /// <summary>7/8 as 2 + 2 + 3 and 5/8 as 2 + 3.</summary>
    private static readonly int[] s_seven = [0, 4, 8];

    private static readonly int[] s_five = [0, 4];

    /// <summary>A 16th of 0.16 s, which the grid reads as one: at 0.125 s it reads two as one, and its beats are the playing's quarters.</summary>
    private const long Sixteenth = 160_000;

    private static (BarGrid Grid, IReadOnlyList<MeterStretch> Odd) Heard(IEnumerable<(int, int, int)> notes)
    {
        ImportedPiece piece = BarGridTests.Import(LivePlaying.Heard(LivePlaying.At(1_000_000, Sixteenth, notes)));
        BarGrid grid = BarGrid.Find(piece.Midi!)!;
        return (grid, grid.OddMeters(piece.Midi!));
    }

    [Fact]
    public void Bars_of_seven_eighths_after_a_waltz_are_found_where_they_start_and_to_the_end()
    {
        (BarGrid grid, IReadOnlyList<MeterStretch> odd) = Heard([.. Bars(0, 16, 12, s_seven), .. Bars(192, 16, 14, s_seven)]);

        // The waltz is read in bars of 4/4 by its bass on every beat; the 7/8 is found from the change to the end.
        Assert.Equal(TimeSignature.CommonTime, grid.Signature);
        Assert.Equal(new MeterStretch(192, 192 + (16 * 14), new TimeSignature(7, 8)), Assert.Single(odd));
    }

    [Fact]
    public void A_march_and_a_waltz_have_no_odd_bars()
    {
        Assert.Empty(Heard(Bars(0, 24, 16, s_common)).Odd);
        Assert.Empty(Heard(Bars(0, 24, 12, s_seven)).Odd);
    }

    [Fact]
    public void A_piece_in_five_or_seven_eighths_is_found_throughout_and_the_same_every_time()
    {
        (BarGrid grid, IReadOnlyList<MeterStretch> odd) = Heard(Bars(0, 24, 10, s_five));
        MeterStretch five = Assert.Single(odd);
        Assert.Equal((new TimeSignature(5, 8), grid.BarStarts[0]), (five.Signature, five.From));
        Assert.Equal(odd, Heard(Bars(0, 24, 10, s_five)).Odd);

        (BarGrid sevens, IReadOnlyList<MeterStretch> seven) = Heard(Bars(0, 24, 14, s_seven));
        Assert.Equal((new TimeSignature(7, 8), sevens.BarStarts[0]), (Assert.Single(seven).Signature, seven[0].From));
    }

    [Fact]
    public void A_transcriptions_odd_bars_are_named_by_the_bars_of_the_piece_as_it_opens_and_nothing_changes()
    {
        byte[] midi = LivePlaying.Heard(LivePlaying.At(1_000_000, Sixteenth, [.. Bars(0, 16, 12, s_seven), .. Bars(192, 16, 14, s_seven)]));
        var facade = new MusicCoreFacade();
        _ = facade.Open("Recital.m4a", [], $$$"""{"id": "p_odd", "now": "2026-11-12T12:00:00Z", "transcription": {"engine": "transkun", "milliseconds": 60000, "parts": [{"index": 0, "midi": "{{{Convert.ToBase64String(midi)}}}"}]}}""");
        string saved = facade.Call(1, "project.save");
        Project project = ProjectFile.Read(Encoding.UTF8.GetBytes(saved));
        ImportedPiece opened = OpenedProject.Open(project).Piece;

        // Bars of 4/4 on the grid: the change at its 192nd 16th starts bar 13, and the stretch runs to the last bar.
        Assert.Equal(new OddBars(13, opened.Meter.BarAt(new Tick(opened.End.Value - 1)).Number, new TimeSignature(7, 8)), Assert.Single(LiveBars.OddMeters(project, opened)));
        Assert.Equal(saved, Encoding.UTF8.GetString(ProjectFile.Write(project)));
        Assert.Empty(LiveBars.OddMeters(project with { Transcription = project.Transcription! with { Bars = null } }, opened));
    }
}