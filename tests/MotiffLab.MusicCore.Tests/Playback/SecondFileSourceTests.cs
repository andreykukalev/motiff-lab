using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Playback;

/// <summary>A second file as the variation source, on the original's timeline (S1-W5).</summary>
public class SecondFileSourceTests
{
    private static ImportedPiece Piece(string folder, string name) =>
        PieceImporter.Import(name, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", folder, "Fixtures", name))).Piece!;

    [Fact]
    public void The_same_file_matches_the_original_exactly()
    {
        ImportedPiece piece = Piece("Midi", "two-hands-type1.mid");
        PlaybackScore original = PlaybackScore.FromPiece(piece);

        SecondFileSource second = SecondFileSource.Align(original, piece);

        Assert.Empty(second.Differences);
        Assert.Same(original, second.Timeline);
        Assert.Equal(original.Notes, second.Notes.Notes);
        Assert.Equal(original.Tempos, second.Notes.Tempos);
    }

    [Fact]
    public void A_file_with_other_tempo_bars_or_length_plays_on_the_original_timeline_and_says_so()
    {
        ImportedPiece scale = Piece("Midi", "scale-type0.mid");
        PlaybackScore shorter = PlaybackScore.Create([], [new PlaybackTempo(Tick.Zero, 600_000)], MeterMap.Default, new Tick(1920));

        SecondFileSource second = SecondFileSource.Align(shorter, scale);

        Assert.Equal(new Tick(15 * 480), second.Timeline.End);
        Assert.Equal(shorter.Tempos, second.Notes.Tempos);
        Assert.Equal(15, second.Notes.Notes.Count);
        Assert.Equal(["It runs past the end of the original, so playback now runs to its end."], second.Differences);

        SecondFileSource onWaltz = SecondFileSource.Align(PlaybackScore.FromPiece(Piece("Midi", "two-hands-type1.mid")), scale);
        Assert.Contains(onWaltz.Differences, text => text.StartsWith("It sets a different tempo", StringComparison.Ordinal));
        Assert.Contains(onWaltz.Differences, text => text.StartsWith("Its time signature or pickup differs", StringComparison.Ordinal));
    }

    [Fact]
    public void Extending_keeps_notes_and_tempo_and_adds_bars_and_beats()
    {
        PlaybackScore original = PlaybackScore.FromPiece(Piece("Midi", "scale-type0.mid"));

        PlaybackScore extended = original.ExtendedTo(new Tick(original.End.Value + 1920));

        Assert.Equal(original.Notes, extended.Notes);
        Assert.Equal(original.Tempos, extended.Tempos);
        Assert.Equal(original.Beats.Count + 4, extended.Beats.Count);
        Assert.Same(original, original.ExtendedTo(Tick.Zero));
    }
}