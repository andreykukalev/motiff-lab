using System.Globalization;
using System.Text;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Playback;
using MotiffLab.Web.Workspace;

namespace MotiffLab.Web.Pages;

/// <summary>
/// A recording as a source (decision record 0024, scenario 2; mockups 06, 07 and 13c): the notes heard in it are engraved
/// in bars of their own, and while it is shown the recording's own sound plays in place of the notes, at its own speed.
/// A strip above the pill shows the recording's waveform with the selection and the playhead on it. The sound is kept on
/// this device only, never in the project file, so on another device the player picks the file again to hear it.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>How much of the recording the strip shows at once, in milliseconds.</summary>
    private const long WaveWindow = 30_000;

    /// <summary>Loudness bars a second, as the engine gives them.</summary>
    private const int PeaksPerSecond = 20;

    private IJSObjectReference? _recordingStore;

    private readonly Dictionary<string, (byte[] Midi, SourceMusic Music, RecordingClock? Clock)> _recordingClocks = new(StringComparer.Ordinal);

    /// <summary>The recording shown and heard: its version, its clock and its sound; null when the source shown is not a recording, or its sound is not here.</summary>
    private (string Id, RecordingClock Clock, RecordingSound Sound)? _recordingShown;

    /// <summary>A recording shown whose sound is not on this device: the strip offers to pick the file again.</summary>
    private string? _recordingMissing;

    private long _waveStart;

    private (string Id, long Start, string Path)? _wavePath;

    private InputFile? _recordingPicker;

    private SavedPerformance? RecordingVersion(string id) =>
        _project?.FindPerformance(id) is { Source: PerformanceSource.Recording } version ? version : null;

    /// <summary>Where the notes of a recording source are heard in it; null for any other source.</summary>
    private RecordingClock? ClockOf(string id)
    {
        if (RecordingVersion(id) is not { } version || Work?.Source(id) is not { } music)
        {
            return null;
        }

        if (_recordingClocks.TryGetValue(id, out var made) && ReferenceEquals(made.Midi, version.Midi) && ReferenceEquals(made.Music, music))
        {
            return made.Clock;
        }

        RecordingClock? clock = RecordingClock.Of(version, music);
        _recordingClocks[id] = (version.Midi, music, clock);
        return clock;
    }

    private async Task<IJSObjectReference> RecordingStoreAsync() =>
        _recordingStore ??= await JS.InvokeAsync<IJSObjectReference>("import", "./js/storage/recording-store.js");

    private string RecordingKey(string versionId) => $"{_project?.Id}/{versionId}";

    /// <summary>A recording just heard became a version: its sound is kept on this device under the version, to be heard in the comparison.</summary>
    private async Task KeepRecordingAsync(string versionId)
    {
        try
        {
            IJSObjectReference store = await RecordingStoreAsync();
            await store.InvokeAsync<bool>("keepHeld", RecordingKey(versionId));
        }
        catch (JSException)
        {
            // The sound is a convenience: the version works without it, and the strip offers to pick the file again.
        }
    }

    /// <summary>
    /// The page now shows <see cref="_assemblyShown"/>: a recording's sound is loaded and heard in place of its notes, or,
    /// for any other source, the notes are heard again.
    /// </summary>
    private async Task SoundShownAsync()
    {
        string id = _assemblyShown;
        _recordingMissing = null;
        if (ClockOf(id) is not { } clock)
        {
            _recordingShown = null;
            return;
        }

        RecordingSound? sound = await Player.LoadRecordingAsync(RecordingKey(id), clock.Ticks, clock.Milliseconds);
        if (sound is null)
        {
            _recordingShown = null;
            _recordingMissing = id;
            return;
        }

        _recordingShown = (id, clock, sound);
        await Player.HearRecordingAsync(true);
    }

    /// <summary>The file of a recording picked again on this device: its sound is kept and heard.</summary>
    private async Task PickRecordingAgainAsync(InputFileChangeEventArgs args)
    {
        _ = args;
        if (_recordingMissing is not { } id || _recordingPicker?.Element is not { } input)
        {
            return;
        }

        IJSObjectReference store = await RecordingStoreAsync();
        if (!await store.InvokeAsync<bool>("keepPicked", input, RecordingKey(id)))
        {
            _marksStatus = Text["recording.notDecoded"];
            return;
        }

        if (_assemblyShown == id)
        {
            await SoundShownAsync();
        }
    }

    /// <summary>The recording's time at a place of its notes shown, in milliseconds.</summary>
    private long? RecordingAt(Tick at) => _recordingShown is { } shown && shown.Id == _assemblyShown ? shown.Clock.MillisecondsAt(at) : null;

    /// <summary>«0:41–0:45»: the selection's stretch of the recording; null when the page shows no recording.</summary>
    private string? PickTimes => AssemblyPick is { } pick && RecordingAt(pick.Onset) is { } from && RecordingAt(pick.End) is { } to
        ? $"{Clock(from)}–{Clock(to)}"
        : null;

    /// <summary>«0:41» for a time in milliseconds.</summary>
    private static string Clock(long milliseconds)
    {
        long seconds = Math.Max(0, milliseconds) / 1000;
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
    }

    /// <summary>Whether the strip shows: a recording is on the page, heard, or wanting its file.</summary>
    private bool WaveShown => _assembling && (_recordingShown?.Id == _assemblyShown || _recordingMissing == _assemblyShown);

    /// <summary>
    /// Where the strip starts: a little before the selection, or the stretch the playhead is in, moving only when what it
    /// follows leaves it.
    /// </summary>
    private long WaveStart
    {
        get
        {
            if (_recordingShown is not { } shown)
            {
                return 0;
            }

            long length = shown.Sound.Milliseconds;
            long latest = Math.Max(0, length - WaveWindow);
            long? follow = AssemblyPick is { } pick && Player.State == PlaybackState.Stopped ? RecordingAt(pick.Onset) : RecordingAt(Player.Position);
            if (follow is { } at && (at < _waveStart || at >= _waveStart + WaveWindow))
            {
                _waveStart = Math.Clamp(at - (WaveWindow / 6), 0, latest);
            }

            return Math.Clamp(_waveStart, 0, latest);
        }
    }

    /// <summary>The waveform in the strip's window as one SVG path of vertical strokes, made again only when the window moves.</summary>
    private string WavePath
    {
        get
        {
            if (_recordingShown is not { } shown)
            {
                return "";
            }

            long start = WaveStart;
            if (_wavePath is { } made && made.Id == shown.Id && made.Start == start)
            {
                return made.Path;
            }

            IReadOnlyList<double> peaks = shown.Sound.Peaks;
            int first = (int)(start * PeaksPerSecond / 1000);
            int count = (int)(WaveWindow * PeaksPerSecond / 1000);
            var path = new StringBuilder();
            for (int i = 0; i < count && first + i < peaks.Count; i++)
            {
                double half = Math.Max(0.4, peaks[first + i] * 19);
                path.Append(CultureInfo.InvariantCulture, $"M{i + 0.5:0.#} {20 - half:0.#}V{20 + half:0.#}");
            }

            _wavePath = (shown.Id, start, path.ToString());
            return _wavePath.Value.Path;
        }
    }

    /// <summary>A time's place across the strip, as a CSS percentage.</summary>
    private string WavePercent(long milliseconds) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Clamp((milliseconds - WaveStart) * 100.0 / WaveWindow, 0, 100):0.##}%");

    /// <summary>The selection on the strip: where it starts and how wide it is, in CSS percentages; null without one in the window.</summary>
    private (string Left, string Width)? WavePick
    {
        get
        {
            if (AssemblyPick is not { } pick || RecordingAt(pick.Onset) is not { } from || RecordingAt(pick.End) is not { } to)
            {
                return null;
            }

            long start = WaveStart;
            if (to <= start || from >= start + WaveWindow)
            {
                return null;
            }

            double left = Math.Clamp((from - start) * 100.0 / WaveWindow, 0, 100);
            double right = Math.Clamp((to - start) * 100.0 / WaveWindow, 0, 100);
            return (string.Create(CultureInfo.InvariantCulture, $"{left:0.##}%"), string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0.5, right - left):0.##}%"));
        }
    }

    /// <summary>The marks under the strip, every ten seconds.</summary>
    private IEnumerable<long> WaveMarks
    {
        get
        {
            long start = WaveStart;
            for (long at = (start + 9_999) / 10_000 * 10_000; at < start + WaveWindow; at += 10_000)
            {
                yield return at;
            }
        }
    }

    /// <summary>A place tapped or moved to on the strip: playing goes on from the notes heard there.</summary>
    private async Task SeekRecordingAsync(ChangeEventArgs args)
    {
        if (_recordingShown is not { } shown || !long.TryParse(args.Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long milliseconds))
        {
            return;
        }

        await Player.SeekAsync(shown.Clock.TickAt(milliseconds));
    }

    /// <summary>«Запись» in the carry sheet: the fragment as the recording plays it, round, on the recording's page; «Призрак» goes back.</summary>
    private async Task HearCarriedRecordingAsync()
    {
        if (_carry is not { } draft)
        {
            return;
        }

        if (Player.State != PlaybackState.Stopped && _assemblyShown == draft.SourceId)
        {
            await Player.PauseAsync();
            return;
        }

        await ShowSourceAsync(draft.SourceId);
        _assemblyLoop = true;
        await Player.SetLoopAsync(draft.From);
        await Player.SeekAsync(draft.From.Onset);
        await Player.PlayAsync();
    }

    /// <summary>Whether a carry from this source can be heard as the recording: its sound is on this device.</summary>
    private bool CarryHeardAsRecording => _carry is { } draft && RecordingVersion(draft.SourceId) is not null && _recordingMissing != draft.SourceId;

    /// <summary>Where the own sound of the piece open is (version 0.5, a piece split from a recording).</summary>
    private PieceSoundPlace _ownSound;

    /// <summary>Whether the piece's own sound is being loaded or kept.</summary>
    private bool _ownSoundBusy;

    private static string OwnSoundKeyOf(string projectId) => $"{projectId}/{PieceParts.SoundId}";

    /// <summary>Whether the piece's own sound plays in place of its notes: the score is on the page, not the assembly.</summary>
    private bool HearingOwnSound => _ownSound == PieceSoundPlace.Here && Player.HearingRecording && !_assembling;

    /// <summary>A piece opened: whether it is a piece of a recording, and whether its own sound is kept on this device.</summary>
    private async Task ReadOwnSoundAsync()
    {
        _ownSound = PieceSoundPlace.None;
        if (!PiecesOn || _project is not { Transcription: not null, PartOf.Milliseconds: not null } project)
        {
            return;
        }

        _ownSound = PieceSoundPlace.Elsewhere;
        try
        {
            IJSObjectReference store = await RecordingStoreAsync();
            if (await store.InvokeAsync<bool>("hasRecording", OwnSoundKeyOf(project.Id)))
            {
                _ownSound = PieceSoundPlace.Here;
            }
        }
        catch (JSException)
        {
            // Then the file is picked again.
        }
    }

    /// <summary>
    /// «Звук записи» or «Ноты»: the piece's own sound plays in place of its notes, at its own speed, its notes heard where
    /// music-core's clock of the recording says they were played (<see cref="HeardClock.Pairs"/>); or the notes again.
    /// </summary>
    private async Task HearOwnSoundAsync(bool on)
    {
        if (!on)
        {
            if (Player.HearingRecording)
            {
                await Player.HearRecordingAsync(false);
            }

            return;
        }

        if (_assembling || _project is not { } project || HeardClockOf() is not { } clock || _sectionSource is not { } piece)
        {
            return;
        }

        _ownSoundBusy = true;
        try
        {
            (IReadOnlyList<long> ticks, IReadOnlyList<long> milliseconds) = clock.Pairs(piece);
            if (await Player.LoadRecordingAsync(OwnSoundKeyOf(project.Id), ticks, milliseconds) is null)
            {
                _ownSound = PieceSoundPlace.Elsewhere;
                return;
            }

            await Player.HearRecordingAsync(true);
        }
        finally
        {
            _ownSoundBusy = false;
        }
    }

    /// <summary>
    /// The recording's file picked again: the piece's stretch of it is cut and kept on this device, when it is the piece's
    /// sound (by the fingerprint its file gives, else by its length within a second). A piece without a description of its
    /// sound gets one, saved with it.
    /// </summary>
    private async Task PickOwnSoundAsync(ElementReference input)
    {
        if (_project is not { PartOf.Milliseconds: { } stretch } project)
        {
            return;
        }

        PieceOriginal? described = project.FindOriginal(PieceParts.SoundId);
        _ownSoundBusy = true;
        try
        {
            IJSObjectReference store = await RecordingStoreAsync();
            CutSound? cut = await store.InvokeAsync<CutSound?>("keepPickedStretch", input, OwnSoundKeyOf(project.Id), stretch.From, stretch.To,
                described?.Sha256, described?.DurationMs ?? (stretch.To - stretch.From));
            if (cut is null)
            {
                _marksStatus = Text["parts.soundNotSame"];
                return;
            }

            _ownSound = PieceSoundPlace.Here;
            if (described is null)
            {
                await SaveMarksAsync(piece => PieceParts.WithSound(piece, cut.Bytes, cut.Milliseconds, cut.Sha256, HoldingDevice.Web, DateTimeOffset.Now), Text["parts.soundKept"]);
            }
            else
            {
                _marksStatus = Text["parts.soundKept"];
            }
        }
        catch (JSException)
        {
            _marksStatus = Text["recording.notDecoded"];
        }
        finally
        {
            _ownSoundBusy = false;
        }
    }
}