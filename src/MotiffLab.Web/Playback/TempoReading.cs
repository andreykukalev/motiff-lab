using MotiffLab.MusicCore.Playback;

namespace MotiffLab.Web.Playback;

/// <summary>What the tempo control reads (version 0.5.4, #511 section 17; version 0.5.5, #521 section 12).</summary>
/// <param name="Played">The tempo at the start as played: the player's when they set one, else the piece's own.</param>
/// <param name="Piece">The tempo at the start as the piece states it, before the player's.</param>
/// <param name="CanSet">Whether the player can set the piece's tempo: the piece has a project to keep it.</param>
/// <param name="Beat">The note the tempo is read and typed in: a dotted quarter in 6/8 and the other compound meters.</param>
public sealed record TempoReading(PlaybackTempo Played, PlaybackTempo Piece, bool CanSet, TempoBeat Beat = TempoBeat.Quarter)
{
    /// <summary>The beat's note as a metronome mark writes it: "♩", or "♩." for a dotted quarter.</summary>
    public string Note => Beat == TempoBeat.DottedQuarter ? "♩." : "♩";

    /// <summary>Beats a minute at the start as played, exact.</summary>
    public double PerMinute => (Beat == TempoBeat.DottedQuarter ? 40_000_000.0 : 60_000_000.0) / Played.MicrosecondsPerQuarter;

    /// <summary>
    /// The piece tempo, the one source of truth the score's mark shows: "♩ = 92" from the notes, the player or the default of
    /// 80, "♩ ≈ 92" from a recording, "♩ ?" for a recording's own clock. The playback pace never changes it.
    /// </summary>
    public string PieceText => Played.Source switch
    {
        TempoSource.Assumed => $"{Note} ?",
        TempoSource.Inferred => $"{Note} ≈ {PlayedPerMinute}",
        _ => $"{Note} = {PlayedPerMinute}",
    };

    /// <summary>The pill: the piece tempo, with the playback pace beside it when it is not 100 % ("♩ = 92 · 80%").</summary>
    public string PillText(int percent, Localization.UiText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return percent == 100 ? PieceText : text.Format("tempoControl.reading", PieceText, percent);
    }

    /// <summary>Beats a minute heard at <paramref name="percent"/>, rounded: the piece tempo × the pace (120 at 80 % is 96).</summary>
    public int HeardAt(int percent) => (int)Math.Round(PerMinute * percent / 100, MidpointRounding.AwayFromZero);

    /// <summary>The speed heard as the pace section says it: "♩ 96", "♩ ≈ 96" from a recording, null for a recording's own clock.</summary>
    public string? HeardText(int percent) => Played.Source switch
    {
        TempoSource.Assumed => null,
        TempoSource.Inferred => $"{Note} ≈ {HeardAt(percent)}",
        _ => $"{Note} {HeardAt(percent)}",
    };

    /// <summary>The tempo at the start as played, in beats a minute.</summary>
    public int PlayedPerMinute => Beat.PerMinute(Played.MicrosecondsPerQuarter);

    /// <summary>The piece's own tempo at the start, in beats a minute.</summary>
    public int PiecePerMinute => Beat.PerMinute(Piece.MicrosecondsPerQuarter);

    /// <summary>The player's tempo for the piece in beats a minute, or null.</summary>
    public int? Player => Played.Source == TempoSource.Player ? PlayedPerMinute : null;

    /// <summary>The piece's own tempo it goes back to when the player's is cleared, in beats a minute; null when the player set none.</summary>
    public int? Own => Player is null ? null : PiecePerMinute;

    /// <summary>Whether <paramref name="perMinute"/> beats a minute is a tempo the player can set (20 to 300 quarter notes).</summary>
    public bool CanSetTo(int perMinute) => perMinute is > 0 and <= PlayerTempo.Max && PlayerTempo.IsValid(Beat.QuarterNotes(perMinute));

    /// <summary>The words under the piece tempo: the music's own, a default assumed (said so), from a recording, or not known yet.</summary>
    public string CaptionKey => Played.Source switch
    {
        TempoSource.Default => "tempoControl.pieceDefault",
        TempoSource.Inferred => "tempoControl.pieceInferred",
        TempoSource.Assumed => "tempoControl.pieceUnknown",
        _ => "tempoControl.pieceCaption",
    };
}