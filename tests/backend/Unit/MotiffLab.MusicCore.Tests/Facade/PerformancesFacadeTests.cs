using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Facade.FacadeCalls;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>
/// The versions compared with a piece through the facade (0022, contract 4), where they meet the rest of the open piece:
/// what the file keeps of them, the notes and the selection over the bars taken. The comparison itself:
/// MusicCoreFacadeTests and PerformancesSessionTests.
/// </summary>
public class PerformancesFacadeTests
{
    private const string FurElise = "fur-elise-original.musicxml";

    /// <summary>Für Elise open as piece 1 with the version "perf_1", which plays the three highest notes of bar 5 otherwise (<paramref name="replaced"/>: in their place, else beside them).</summary>
    private static MusicCoreFacade WithVersion(bool replaced)
    {
        var facade = new MusicCoreFacade();
        Assert.False(Json(facade.Open(FurElise, Fixture(FurElise), """{"id": "p_fur"}""")).TryGetProperty("error", out _));
        ImportedPiece piece = PieceImporter.Import(FurElise, Fixture(FurElise)).Piece!;
        TickSpan bar5 = piece.Meter.GetBar(5).Span;
        Note[] melody = [.. piece.Notes.Where(note => bar5.Contains(note.Span.Onset)).OrderByDescending(note => note.Pitch.MidiNumber).Take(3)];
        Note[] played = [.. piece.Notes.Where(note => !replaced || !melody.Contains(note)), .. melody.Select((note, i) => note with { Id = $"x{i}", Pitch = new Pitch(note.Pitch.MidiNumber + (i * 7) - 3) })];
        Call(facade, "performance.add", $$$"""{"name": "Live", "midi": "{{{Convert.ToBase64String(MidiExporter.Export(played, [], piece.Meter, "performance"))}}}"}""");
        return facade;
    }

    /// <summary>The project file <paramref name="facade"/> saves, and whether it opens again.</summary>
    private static byte[] SavedAndOpened(MusicCoreFacade facade)
    {
        byte[] saved = System.Text.Encoding.UTF8.GetBytes(facade.Call(1, "project.save"));
        Assert.Equal(1, Json(new MusicCoreFacade().Open("fur-elise" + ProjectFile.Extension, saved)).GetProperty("piece").GetInt32());
        return saved;
    }

    [Fact]
    public void A_bar_a_version_is_not_compared_in_is_never_dismissed_so_the_piece_still_opens()
    {
        MusicCoreFacade facade = WithVersion(replaced: false);

        Assert.Equal("badRequest", Error(facade, "performance.dismiss", """{"id": "perf_1", "bar": -2}"""));
        Assert.Equal("badRequest", Error(facade, "performance.dismiss", """{"id": "perf_1", "bar": 9999}"""));
        Assert.Equal(1, Call(facade, "performance.dismiss", """{"id": "perf_1", "bar": 5}""").GetProperty("performance").GetProperty("dismissedBars").GetInt32());

        Assert.Equal([5], Assert.Single(ProjectFile.Read(SavedAndOpened(facade)).Performances).Dismissed);
    }

    [Fact]
    public void A_version_removed_takes_the_bars_taken_from_it_out_of_the_open_notes()
    {
        MusicCoreFacade facade = WithVersion(replaced: false);
        int printed = Call(facade, "piece").GetProperty("noteCount").GetInt32();
        Call(facade, "performance.take", """{"id": "perf_1", "bars": [5]}""");
        Assert.Equal(printed + 3, Call(facade, "piece").GetProperty("noteCount").GetInt32());

        Call(facade, "sources.notes.remove", """{"id": "perf_1"}""");
        Assert.Equal(printed, Call(facade, "piece").GetProperty("noteCount").GetInt32());
        Assert.DoesNotContain("perf-perf_1", Call(facade, "roll").GetRawText(), StringComparison.Ordinal);
    }
}