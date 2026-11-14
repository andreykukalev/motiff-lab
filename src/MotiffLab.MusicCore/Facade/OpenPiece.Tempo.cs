using MotiffLab.MusicCore.Playback;

namespace MotiffLab.MusicCore.Facade;

/// <summary>The tempo the player sets for the piece (version 0.5.4, #511 section 17, within contract 12, a call added).</summary>
internal sealed partial class OpenPiece
{
    /// <summary>
    /// "perMinute": the piece's tempo in quarter notes a minute, 20 to 300, such as "♩ = 92" read from the printed notes;
    /// null or left out plays the piece as it states again. The project keeps it ("tempo"); the answer is "tempo" as
    /// <c>playback</c> answers it, and the next <c>playback</c> and <c>notation</c> play and mark it.
    /// </summary>
    private string SetTempo(Request request)
    {
        int? perMinute = request.Int("perMinute");
        if (perMinute is { } given && !PlayerTempo.IsValid(given))
        {
            throw FacadeException.BadRequest($"\"perMinute\" is {PlayerTempo.Min} to {PlayerTempo.Max} quarter notes a minute, or null.");
        }

        Project = Project with { PlayerTempo = perMinute };
        Piece = Piece with { PlayerTempo = perMinute };
        // The character facts speak of the tempo.
        _understood = null;
        PlaybackScore score = PlaybackScore.FromPiece(Piece);
        return Answer.Write(json => Answer.PlaybackTempoAnswer(json, score));
    }
}