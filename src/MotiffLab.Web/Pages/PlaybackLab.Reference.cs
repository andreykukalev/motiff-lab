using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Reference;
using MotiffLab.Web.Reference.Handbook;

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

    /// <summary>Where «Справочник»'s music playing was placed; null when none plays.</summary>
    private Tick? _handbookAt;

    private bool ReferenceOn => SettingsStore.IsOn(ReferenceSetting) || HandbookOn;

    /// <summary>«Справочник» (version 0.5, experimental.harmony): the Reference with chords and scales and the harmony explorer.</summary>
    private bool HandbookOn => SettingsStore.IsOn(ReferenceButton.HarmonySetting);

    private void OpenReference()
    {
        ClosePopover();
        _referencePieceKey = _notes is { } notes && _meter is { } meter && _score is { } score ? PieceDna.OpeningKey(notes, meter, _keys, score.End) : null;
        _referenceOpen = true;
    }

    /// <summary>
    /// «Справочник»'s music over the piece's timeline from where the selection starts or playback stands, at its own tempo
    /// (stretched to the piece's tempo there), once or looped; the piece's notes come back when it stops.
    /// </summary>
    private async Task<ReferencePlacement?> PlayHandbookAsync(ReferencePlay play)
    {
        if (_score is not { } score)
        {
            return null;
        }

        // Music in place of the Reference's music playing takes its place, so a change heard while it plays does not move
        // it along the piece; it sounds on without a stop between.
        Tick at = _selection?.Span.Onset ?? (_scaleHeard && _handbookAt is { } before ? before : Player.Position);
        long length = play.Music.LengthOn(TempoAt(score, at));
        // Near the end of the piece the music starts early enough to end with it.
        at = at.Value + length > score.End.Value ? new Tick(Math.Max(0, score.End.Value - length)) : at;
        int tempo = TempoAt(score, at);
        PlacedMusic placed = play.Music.Place(at, tempo);
        _handbookAt = at;
        await HearAloneAsync(placed.Notes, placed.Span, once: !play.Loop);
        return new ReferencePlacement(at.Value, play.Music.MicrosecondsPerQuarter, tempo);
    }

    private static int TempoAt(PlaybackScore score, Tick at) =>
        score.Tempos.LastOrDefault(tempo => tempo.At <= at) is { MicrosecondsPerQuarter: > 0 } tempo ? tempo.MicrosecondsPerQuarter : PlaybackTempo.DefaultMicrosecondsPerQuarter;

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
        long length = sound.Length;
        // Near the end of the piece the notes start early enough to end with it.
        at = at.Value + length > _score.End.Value ? new Tick(Math.Max(0, _score.End.Value - length)) : at;
        await HearAloneAsync(KeyReference.Notes(sound, at));
    }
}