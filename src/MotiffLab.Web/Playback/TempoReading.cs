using MotiffLab.MusicCore.Playback;

namespace MotiffLab.Web.Playback;

/// <summary>What the tempo control reads (version 0.5.4, #511 section 17).</summary>
/// <param name="Played">The tempo at the start as played: the player's when they set one.</param>
/// <param name="Piece">The tempo at the start as the piece states it, before the player's.</param>
/// <param name="CanSet">Whether the player can set the piece's tempo: the piece has a project to keep it.</param>
/// <param name="Beat">The note the tempo is read and typed in: a dotted quarter in 6/8 and the other compound meters.</param>
public sealed record TempoReading(PlaybackTempo Played, PlaybackTempo Piece, bool CanSet, TempoBeat Beat = TempoBeat.Quarter)
{
    /// <summary>The beat's note as a metronome mark writes it: "♩", or "♩." for a dotted quarter.</summary>
    public string Note => Beat == TempoBeat.DottedQuarter ? "♩." : "♩";

    /// <summary>Beats a minute at the start as played, exact.</summary>
    public double PerMinute => (Beat == TempoBeat.DottedQuarter ? 40_000_000.0 : 60_000_000.0) / Played.MicrosecondsPerQuarter;

    /// <summary>Beats a minute heard at <paramref name="percent"/>, rounded: the "♩ 83" beside "90 %".</summary>
    public int HeardAt(int percent) => (int)Math.Round(PerMinute * percent / 100, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The pill's beats: "♩ 83" from the notes or the player, "♩ ≈ 83" from a recording, and "♩ ?" when the tempo was not
    /// recognised, so a default is never read as the piece's tempo.
    /// </summary>
    public string HeardText(int percent) => Played.Source switch
    {
        TempoSource.Assumed => $"{Note} ?",
        TempoSource.Inferred => $"{Note} ≈ {HeardAt(percent)}",
        _ => $"{Note} {HeardAt(percent)}",
    };

    /// <summary>The percentage that plays <paramref name="perMinute"/> beats a minute, to the nearest whole one (not clamped).</summary>
    public int PercentFor(int perMinute) => (int)Math.Round(perMinute * 100 / PerMinute, MidpointRounding.AwayFromZero);

    /// <summary>The tempo at the start as played, in beats a minute.</summary>
    public int PlayedPerMinute => Beat.PerMinute(Played.MicrosecondsPerQuarter);

    /// <summary>The piece's own tempo at the start, in beats a minute.</summary>
    public int PiecePerMinute => Beat.PerMinute(Piece.MicrosecondsPerQuarter);

    /// <summary>The player's tempo for the piece in beats a minute, or null.</summary>
    public int? Player => Played.Source == TempoSource.Player ? PlayedPerMinute : null;

    /// <summary>Whether <paramref name="perMinute"/> beats a minute is a tempo the player can set (20 to 300 quarter notes).</summary>
    public bool CanSetTo(int perMinute) => perMinute is > 0 and <= PlayerTempo.Max && PlayerTempo.IsValid(Beat.QuarterNotes(perMinute));
}