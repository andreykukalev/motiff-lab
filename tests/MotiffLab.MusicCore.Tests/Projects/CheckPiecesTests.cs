using System.Text.Json;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// Check by pieces (version 0.5): the flags of a score of several pieces grouped by the pieces layer, counted in bars, and
/// «Выглядит верно» for a whole piece resolving every flagged bar in it in one change, which the bars it changed undo.
/// </summary>
public class CheckPiecesTests
{
    private const long Bar = 4 * CanonicalTime.TicksPerQuarter;

    private static readonly DateTimeOffset s_now = new(2026, 11, 12, 17, 0, 0, TimeSpan.Zero);

    private static readonly byte[] s_source = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "importer-grand-staff.musicxml"));

    private static readonly Tick s_end = new(60 * Bar);

    private static CheckFlag Flag(int bar, CheckReason reason = CheckReason.BarTooLong, bool resolved = false) => new(bar, CheckSource.Transcription, reason, resolved);

    /// <summary>Three pieces from bars 1, 21 and 41; flags on bars 3 (two reasons), 7 (resolved), 25 and 50.</summary>
    private static Project Concert(bool layer = true)
    {
        Project project = Project.Create("p_concert", "concert.mid", s_source, s_now) with
        {
            Check = [Flag(3), Flag(3, CheckReason.GraceNotes), Flag(7, resolved: true), Flag(25), Flag(50)],
        };
        return layer
            ? PieceLayer.Save(project, [new PieceStart("Prelude", 1), new PieceStart("Fugue", 21), new PieceStart("Coda", 41)], MeterMap.Default, s_end, Words.English, s_now)
            : project;
    }

    [Fact]
    public void Each_piece_of_the_layer_counts_its_flagged_bars_to_check_and_in_all()
    {
        IReadOnlyList<CheckPiece> pieces = CheckPieces.Of(Concert(), MeterMap.Default);

        Assert.Equal(
            [new CheckPiece("Prelude", 1, 20, 1, 2), new CheckPiece("Fugue", 21, 40, 1, 1), new CheckPiece("Coda", 41, 60, 1, 1)],
            pieces);
        Assert.Empty(CheckPieces.Of(Concert(layer: false), MeterMap.Default));
        Assert.Empty(CheckPieces.Of(Concert() with { Check = [] }, MeterMap.Default));
    }

    [Fact]
    public void Looks_right_for_a_piece_resolves_every_flag_in_its_bars_and_its_bars_undo_it()
    {
        Project project = Concert();
        IReadOnlyList<int> bars = CheckPieces.OpenBars(project, 1, 20);
        Assert.Equal([3], bars);

        Project resolved = CheckPieces.Resolve(project, 1, 20);

        Assert.All(resolved.Check!.Where(flag => flag.Bar <= 20), flag => Assert.True(flag.Resolved));
        Assert.Equal([25, 50], resolved.OpenChecks.Select(flag => flag.Bar));
        Assert.Equal(0, CheckPieces.Of(resolved, MeterMap.Default)[0].Open);
        Assert.Same(resolved, CheckPieces.Resolve(resolved, 1, 20));

        Project undone = CheckPieces.Resolve(resolved, bars, resolved: false);
        Assert.Equal(project.Check, undone.Check);
        Assert.Throws<ArgumentOutOfRangeException>(() => CheckPieces.Resolve(project, 20, 1));
    }

    [Fact]
    public void A_scans_bars_resolved_for_a_piece_are_confirmed_with_them()
    {
        Project project = Concert() with { Scan = ScanOrigin.Read("homr 0.7.0") };

        Project resolved = CheckPieces.Resolve(project, 21, 60);

        Assert.Equal([25, 50], resolved.Scan!.ConfirmedBars);
    }

    [Fact]
    public void The_facade_answers_check_with_its_pieces_and_resolves_a_piece_by_its_bars()
    {
        Project project = Concert();
        MeterMap meter = OpenedProject.Open(project).Piece.Meter;
        CheckPiece second = CheckPieces.Of(project, meter)[1];
        var facade = new MusicCoreFacade();
        Assert.Equal(1, JsonDocument.Parse(facade.Open("concert.motiff.json", ProjectFile.Write(project))).RootElement.GetProperty("piece").GetInt32());

        JsonElement[] pieces = [.. JsonDocument.Parse(facade.Call(1, "check")).RootElement.GetProperty("pieces").EnumerateArray()];
        Assert.Equal(3, pieces.Length);
        Assert.Equal("Fugue", pieces[1].GetProperty("title").GetString());
        Assert.Equal(
            (second.FirstBar, second.LastBar, second.Open, second.Total),
            (pieces[1].GetProperty("firstBar").GetInt32(), pieces[1].GetProperty("lastBar").GetInt32(), pieces[1].GetProperty("open").GetInt32(), pieces[1].GetProperty("total").GetInt32()));

        string bars = JsonSerializer.Serialize(new { bars = new { first = second.FirstBar, last = second.LastBar } });
        JsonElement after = JsonDocument.Parse(facade.Call(1, "check.resolve", bars)).RootElement;
        Assert.Equal(CheckPieces.Resolve(project, second.FirstBar, second.LastBar).OpenChecks.Count, after.GetProperty("open").GetInt32());
        Assert.Equal(0, after.GetProperty("pieces")[1].GetProperty("open").GetInt32());
        Assert.Equal("badRequest", JsonDocument.Parse(facade.Call(1, "check.resolve", """{"bars": {"first": 9, "last": 2}}""")).RootElement.GetProperty("error").GetString());
        Assert.Equal("badRequest", JsonDocument.Parse(facade.Call(1, "check.resolve", "{}")).RootElement.GetProperty("error").GetString());
    }
}