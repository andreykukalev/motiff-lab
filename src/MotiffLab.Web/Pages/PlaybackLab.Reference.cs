using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The Reference inside a piece (an experiment, Settings · Experimental): Circle in the bar opens the circle of fifths over
/// the score, with the key the piece starts in marked (music-core's key lane, assumed where it is read from the notes). A
/// scale or chord of a key's card is heard once on its own, from where the selection starts or where playback stands, as
/// Hear the scale is; the piece's notes come back after it.
/// </summary>
public partial class PlaybackLab
{
    private const string ReferenceSetting = "experimental.reference";

    private bool _referenceOpen;

    private DnaKey? _referencePieceKey;

    private bool ReferenceOn => SettingsStore.IsOn(ReferenceSetting);

    private void OpenReference()
    {
        ClosePopover();
        _referencePieceKey = _notes is { } notes && _meter is { } meter && _score is { } score ? PieceDna.OpeningKey(notes, meter, _keys, score.End) : null;
        _referenceOpen = true;
    }

    private async Task HearReferenceAsync(ReferenceSound sound)
    {
        if (_scaleHeard)
        {
            await StopScaleAsync();
        }

        if (_score is null)
        {
            return;
        }

        Tick at = _selection?.Span.Onset ?? Player.Position;
        long length = (sound.Pitches.Count + 1) * CanonicalTime.TicksPerQuarter;
        // Near the end of the piece the notes start early enough to end with it.
        at = at.Value + length > _score.End.Value ? new Tick(Math.Max(0, _score.End.Value - length)) : at;
        await HearAloneAsync(KeyReference.Notes(sound, at));
    }
}