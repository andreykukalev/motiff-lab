using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Playback;
using MotiffLab.Web.Score;
using MotiffLab.Web.Understand;

namespace MotiffLab.Web.Pages;

/// <summary>
/// Another version of the piece compared with the notes open (decision record 0022, sections 1, 4 and 5): a MIDI file,
/// such as a cover performer's, or a score in MusicXML, such as the official edition over notes heard in a recording, is
/// added from the Versions drawer, lined up with the notes on the device and kept with the project. Which is "the notes"
/// and which "the performance" is only which was opened first. A small plum marker sits above every bar the version has
/// otherwise and the notes it does not have are tinted; the drawer lists those bars with Show, Take into the score and
/// Not a difference, and the bars taken with Restore the print.
/// </summary>
public partial class PlaybackLab
{
    private bool _performanceOpen;

    /// <summary>The performance shown when the piece has several; null shows the first.</summary>
    private string? _performanceId;

    /// <summary>What the last change in the drawer did, or why a file could not be added.</summary>
    private string? _performanceStatus;

    private (string Id, byte[] Midi, IReadOnlyList<Anchor> Anchors, NoteEditLog Edits, IReadOnlyList<Note> Imported, PerformanceComparison Comparison)? _comparison;

    private (SavedPerformance Of, PerformanceComparison Comparison, string Language, IReadOnlyList<ScorePerformanceMarker> Markers, IReadOnlyList<HighlightPlace> Unheard)? _performanceMarks;

    private bool HasPerformances => _project is { Performances.Count: > 0 };

    /// <summary>The performance on the score and in the drawer: the one picked, or the first.</summary>
    private SavedPerformance? ShownPerformance => _project is { Performances: { Count: > 0 } all }
        ? all.FirstOrDefault(performance => performance.Id == _performanceId) ?? all[0]
        : null;

    /// <summary>The printed notes with the player's corrections: what a performance is compared with, never the bars taken.</summary>
    private IReadOnlyList<Note> PrintedNotes => _edits.Apply(_imported);

    /// <summary>The shown performance compared with the print, worked out again only when the performance or the print changes.</summary>
    private PerformanceComparison? ShownComparison
    {
        get
        {
            if (ShownPerformance is not { } performance || _meter is null)
            {
                return null;
            }

            if (_comparison is { } cached && cached.Id == performance.Id && ReferenceEquals(cached.Midi, performance.Midi)
                && ReferenceEquals(cached.Anchors, performance.Anchors) && ReferenceEquals(cached.Edits, _edits) && ReferenceEquals(cached.Imported, _imported))
            {
                return cached.Comparison;
            }

            PerformanceComparison comparison = PerformanceComparison.Compare(PrintedNotes, _meter, performance.Notes(), performance.Alignment());
            _comparison = (performance.Id, performance.Midi, performance.Anchors, _edits, _imported, comparison);
            return comparison;
        }
    }

    /// <summary>The bars the shown performance plays differently, in bar order: aligned, not dismissed and not taken.</summary>
    private IReadOnlyList<BarComparison> DifferingBars => ShownPerformance is { } performance && ShownComparison is { } comparison
        ? [.. comparison.Differences.Where(bar => !performance.Dismissed.Contains(bar.Bar) && !performance.Taken.Any(taken => taken.Bar == bar.Bar))]
        : [];

    /// <summary>The bars of the shown performance that do not line up with the score, where no difference is claimed.</summary>
    private int UnalignedBarCount => ShownComparison?.Bars.Count(bar => !bar.Aligned) ?? 0;

    /// <summary>The markers and the printed notes not heard for the score, the same lists until the performance, the print or the language change.</summary>
    private (IReadOnlyList<ScorePerformanceMarker> Markers, IReadOnlyList<HighlightPlace> Unheard)? PerformanceMarks
    {
        get
        {
            if (ShownPerformance is not { } performance || ShownComparison is not { } comparison || _meter is null)
            {
                return null;
            }

            if (_performanceMarks is { } cached && ReferenceEquals(cached.Of, performance) && ReferenceEquals(cached.Comparison, comparison) && cached.Language == Text.Language)
            {
                return (cached.Markers, cached.Unheard);
            }

            IReadOnlyList<BarComparison> bars = DifferingBars;
            IReadOnlyList<ScorePerformanceMarker> markers = [.. bars.Select(bar =>
            {
                string marker = bar.Marker(Words);
                return new ScorePerformanceMarker(bar.Bar, _meter.GetBar(bar.Bar).Span.Onset.Value, marker, Text.Format("performance.markerLabel", bar.Bar, performance.Name, marker));
            })];
            HashSet<string> unheardIds = [.. bars.SelectMany(bar => bar.PrintedNotHeard)];
            IReadOnlyList<HighlightPlace> unheard = [.. PrintedNotes
                .Where(note => unheardIds.Contains(note.Id))
                .Select(note => new HighlightPlace(note.Span.Onset.Value, note.Span.End.Value, [new HighlightNote(note.Id, note.Span.Onset.Value, note.Span.End.Value, note.Pitch.MidiNumber)]))];
            _performanceMarks = (performance, comparison, Text.Language, markers, unheard);
            return (markers, unheard);
        }
    }

    /// <summary>The shown version's layer as worked out (artefacts included), and its notes without the likely artefacts.</summary>
    private (SavedPerformance Of, PerformanceComparison Comparison, PerformanceLayerNotes Layer, IReadOnlyList<Note> Heard)? _versionNotes;

    /// <summary>
    /// The shown version in the score's time (0022, section 4): what agrees as printed, what it adds on the grid, bars that
    /// do not line up empty, likely artefacts of a recording's hearing left out. Worked out again only when the version or
    /// its comparison change.
    /// </summary>
    private IReadOnlyList<Note>? VersionNotes
    {
        get
        {
            if (ShownPerformance is not { } performance || ShownComparison is not { } comparison || _meter is null)
            {
                return null;
            }

            if (_versionNotes is not { } cached || !ReferenceEquals(cached.Of, performance) || !ReferenceEquals(cached.Comparison, comparison))
            {
                PerformanceLayerNotes layer = PerformanceLayer.Of(comparison, performance.Notes(), PrintedNotes, _meter);
                HashSet<string> artefacts = [.. layer.Artefacts];
                cached = (performance, comparison, layer, [.. layer.Notes.Where(note => !artefacts.Contains(note.Id))]);
                _versionNotes = cached;
            }

            return cached.Heard;
        }
    }

    /// <summary>The bars of the phrase in focus the shown version has otherwise and that can be taken: not dismissed or taken.</summary>
    private IReadOnlyList<BarComparison> FocusVersionBars => FocusSpan is { } span && _meter is not null
        ? [.. DifferingBars.Where(bar => BarRange.Of(_meter, span).Contains(bar.Bar))]
        : [];

    /// <summary>The bars of the phrase in focus taken from the shown version.</summary>
    private IReadOnlyList<int> FocusTakenBars => FocusSpan is { } span && _meter is not null && ShownPerformance is { } performance
        ? [.. performance.Taken.Select(taken => taken.Bar).Where(bar => BarRange.Of(_meter, span).Contains(bar)).Order()]
        : [];

    /// <summary>The engine source for the shown version's notes in the selection, heard in focus (0022, section 5).</summary>
    private const string VersionSource = "version";

    /// <summary>Whether the player chose to hear the other version in focus.</summary>
    private bool VersionChosen => _hear.Contains(VersionSource);

    /// <summary>Whether the phrase in focus has another version to hear, with no variation open: Notes / Version / Alternate shows then.</summary>
    private bool FocusHasVersion => !FocusHasVariation && FocusSpan is { } span && VersionNotesIn(span).Count > 0;

    /// <summary>The shown version's notes that start in a span, in the score's time; none when no version is shown.</summary>
    private IReadOnlyList<Note> VersionNotesIn(TickSpan span) =>
        VersionNotes is { } notes ? [.. notes.Where(note => span.Contains(note.Span.Onset))] : [];

    /// <summary>
    /// Notes or Version in focus: loops the phrase as in the score, or as in the other version, from where the loop is or
    /// from the phrase's start.
    /// </summary>
    private async Task HearVersionAsync(bool version)
    {
        if (_selection is not { NoteIds.Count: > 0 } selection)
        {
            return;
        }

        _hear = version ? [VersionSource] : [Player.OriginalSource];
        _alternating = false;
        _looping = true;
        TickSpan range = await LoopSelectionAsync(selection);
        if (Player.State == PlaybackState.Stopped)
        {
            await Player.SeekAsync(range.Onset);
            await PlayAsync();
        }
    }

    /// <summary>The markers of the version's differences, only with extra information on (testing item 33).</summary>
    private IReadOnlyList<ScorePerformanceMarker>? ScorePerformanceMarkers => ExtraShown("performance") ? PerformanceMarks?.Markers : null;

    private IReadOnlyList<HighlightPlace>? ScoreUnheard => ExtraShown("performance") ? PerformanceMarks?.Unheard : null;

    /// <summary>A marker tapped on the score: its bar and where, for the small popover beside it.</summary>
    private ScoreMarkerPick? _markerPick;

    /// <summary>"Bar 5 · 92 %": the bar and how sure the comparison is of it.</summary>
    private string PerformanceBarLine(BarComparison bar) => Text.Format("performance.barLine", bar.Bar, bar.Confidence);

    /// <summary>"3 played, not printed · 1 printed, not heard": what the bar's differences are made of.</summary>
    private string PerformanceCounts(BarComparison bar)
    {
        List<string> parts = [];
        if (bar.PlayedNotPrinted.Count > 0)
        {
            parts.Add(Text.Format("performance.playedNotPrinted", bar.PlayedNotPrinted.Count));
        }

        if (bar.PrintedNotHeard.Count > 0)
        {
            parts.Add(Text.Format("performance.printedNotHeard", bar.PrintedNotHeard.Count));
        }

        return string.Join(" · ", parts);
    }

    private void TogglePerformance()
    {
        _performanceOpen = !_performanceOpen;
        _insightsOpen &= !_performanceOpen;
        _checkOpen &= !_performanceOpen;
        _popover = Popover.None;
    }

    /// <summary>A marker tapped on the score: its bar is selected and a small popover beside it says how the version differs.</summary>
    private async Task OnPerformancePickedAsync(ScoreMarkerPick pick)
    {
        _markerPick = pick;
        await ShowPerformanceBarAsync(pick.Bar);
    }

    /// <summary>The popover's place for CSS, under the marker.</summary>
    private string MarkerPlace => _markerPick is { } pick
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"--ax: {pick.Left + 160:0}px; --ay: {pick.Bottom:0}px")
        : "--ax: 50vw; --ay: 30vh";

    /// <summary>The marked bar in the version, for the popover's line.</summary>
    private BarComparison? MarkerBar => _markerPick is { } pick ? ShownComparison?.Bars.FirstOrDefault(bar => bar.Bar == pick.Bar) : null;

    private void CloseMarker() => _markerPick = null;

    /// <summary>«▶ Как в версии»: the bar loops as the version plays it, until stopped.</summary>
    private async Task HearMarkerVersionAsync()
    {
        if (_markerPick is not { } pick || _meter is null || _score is null)
        {
            return;
        }

        TickSpan bar = _meter.GetBar(pick.Bar).Span;
        await Player.SetSourceAsync(VersionSource, _score.WithNotes(Voiced(VersionNotesIn(bar))));
        await Player.SelectSourcesAsync(VersionSource);
        _looping = true;
        await Player.SetLoopAsync(bar);
        await Player.SeekAsync(bar.Onset);
        await PlayAsync();
    }

    /// <summary>The bars taken from versions by the merge draft of older files, for the thin plum line under them, with extra information on.</summary>
    private IReadOnlyList<long>? _takenBarTicks;

    private IReadOnlyList<long>? ScoreTakenBars
    {
        get
        {
            if (!ExtraShown("taken") || _meter is null || _project is not { } project)
            {
                return null;
            }

            long[] ticks = [.. project.TakenBars.Select(bar => _meter.GetBar(bar.Bar).Span.Onset.Value).Distinct().Order()];
            if (_takenBarTicks is null || !_takenBarTicks.SequenceEqual(ticks))
            {
                _takenBarTicks = ticks;
            }

            return _takenBarTicks;
        }
    }

    /// <summary>«Сравнить» on a marker: the comparison opens on the version, with the bar it plays there selected.</summary>
    private Task CompareAtMarkerAsync() => _markerPick is { } pick ? CompareAtAsync(pick.Bar) : Task.CompletedTask;

    /// <summary>The comparison opens on the shown version, at the bars of it that stand for <paramref name="bar"/> of the notes.</summary>
    private async Task CompareAtAsync(int bar)
    {
        if (ShownPerformance is not { } version || _meter is null || Work is not { } work || work.Source(version.Id) is null)
        {
            return;
        }

        _markerPick = null;
        TickSpan inNotes = _meter.GetBar(bar).Span;
        if (work.Match(version.Id).InSource(inNotes) is { } inVersion)
        {
            _picks[version.Id] = inVersion;
        }

        _picks[NotesSource] = inNotes;
        await OpenAssemblyAsync(version.Id);
    }

    /// <summary>In focus, «Сравнить» on the phrase's first bar the version has otherwise.</summary>
    private Task CompareInFocusAsync() => FocusVersionBars is [var first, ..] ? LeaveFocusThenAsync(() => CompareAtAsync(first.Bar)) : Task.CompletedTask;

    private async Task LeaveFocusThenAsync(Func<Task> then)
    {
        await LeaveFocusAsync();
        await then();
    }

    private void ShowPerformanceOf(string id)
    {
        _performanceId = id;
        _performanceStatus = null;
    }

    /// <summary>Show: the bar is selected, so it can be looped and read against the performance.</summary>
    private Task ShowPerformanceBarAsync(int bar) =>
        _meter is null || _score is null || bar < _meter.FirstBarNumber
            ? Task.CompletedTask
            : SelectAsync(MotifSelector.Bars(_meter, bar, bar, _score.End), note: Text.Format("check.showing", bar));

    /// <summary>
    /// A MIDI file of a performance, read on the device: lined up with the printed notes, kept with the project, and for a
    /// scanned piece the bars the recording reads otherwise flagged in Check. The file never leaves the browser.
    /// </summary>
    private async Task AddPerformanceAsync(InputFileChangeEventArgs args)
    {
        _performanceStatus = null;
        if (_project is not { } project || _meter is null)
        {
            return;
        }

        IBrowserFile file = args.File;
        if (PieceImporter.CheckSize(file.Size) is { } tooLarge)
        {
            _performanceStatus = tooLarge.Text(Words);
            return;
        }

        byte[] data;
        await using (Stream stream = file.OpenReadStream(PieceImporter.MaxFileBytes))
        {
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            data = memory.ToArray();
        }

        // A MIDI file as it is, or a score in MusicXML written out as MIDI: either can be the other version.
        VersionFileResult read = VersionFile.Read(file.Name, data);
        if (read.Rejection is { } rejection)
        {
            _performanceStatus = $"{rejection.Text(Words)} {rejection.TipIn(Words)}".Trim();
            return;
        }

        IReadOnlyList<PlayedNote> played;
        try
        {
            played = read.Midi is { } notes ? PlayedNotes.FromMidi(notes) : [];
        }
        catch (Exception exception) when (exception is MidiImportException or ArgumentException or InvalidDataException or FormatException)
        {
            _performanceStatus = Text["performance.notMidi"];
            return;
        }

        if (played.Count == 0)
        {
            _performanceStatus = Text["performance.noNotes"];
            return;
        }

        await AddVersionAsync(project, file.Name, read.Source, read.Midi!, played);
    }

    /// <summary>
    /// A recording as another version (0022): the notes the scan server heard in it, lined up like a MIDI file's and kept as
    /// a recording's. Its sound is kept on this device only, never in the project file (0024, scenario 2).
    /// </summary>
    private async Task<string?> AddRecordingVersionAsync(string fileName, byte[] midi)
    {
        _performanceStatus = null;
        if (_project is { } project && _meter is not null)
        {
            IReadOnlyList<PlayedNote> played;
            try
            {
                played = PlayedNotes.FromMidi(midi);
            }
            catch (Exception exception) when (exception is MidiImportException or ArgumentException or InvalidDataException or FormatException)
            {
                return Text["transcribe.failed"];
            }

            if (played.Count == 0)
            {
                return Text["performance.noNotes"];
            }

            await AddVersionAsync(project, fileName, PerformanceSource.Recording, midi, played);
            if (_performanceId is { } id)
            {
                // Its sound stays on this device, to be heard while comparing (0024, scenario 2).
                await KeepRecordingAsync(id);
                await KeepRecordingOriginalAsync(id, fileName);
            }

            StateHasChanged();
        }

        return null;
    }

    /// <summary>Lines the version's notes up with the piece's, keeps the version with the piece and shows it, saying how much agrees.</summary>
    private async Task AddVersionAsync(Project project, string fileName, PerformanceSource source, byte[] midi, IReadOnlyList<PlayedNote> played)
    {
        IReadOnlyList<Note> printed = PrintedNotes;
        PerformanceAlignment alignment = PerformanceAlignment.Align(printed, played);
        string name = Path.GetFileNameWithoutExtension(fileName) is { Length: > 0 } stem ? stem : Text["performance.defaultName"];
        var performance = new SavedPerformance(project.NextPerformanceId(), name, source, midi, 0, alignment.Anchors);
        project = project.AddPerformance(performance);
        PerformanceComparison comparison = PerformanceComparison.Compare(printed, _meter!, played, alignment);
        if (project.Scan is not null && CurrentPiece.Piece is { MusicXml: not null } piece)
        {
            IReadOnlyList<RecordingCorrection> corrections = RecordingCorrections.Find(printed, _meter!, ScanCheck.Of(piece), comparison);
            project = project.WithPerformanceChecks(corrections.SelectMany(correction => correction.Flags()));
            _scoreChecks = null;
        }

        _project = project;
        _performanceId = performance.Id;
        _addingSource = false;
        _comparison = (performance.Id, performance.Midi, performance.Anchors, _edits, _imported, comparison);
        _performanceStatus = null;
        CurrentPiece.Saved(_project);
        await Projects.SaveAsync(_project);
        // The notes have not changed (0024, section 6): one quiet card offers to compare, and the sheet steps aside for it.
        _performanceOpen = false;
        OfferComparison(performance);
    }

    private async Task RenamePerformanceAsync(ChangeEventArgs args)
    {
        if (_project is not { } project || ShownPerformance is not { } performance || args.Value is not string name || string.IsNullOrWhiteSpace(name) || name.Trim() == performance.Name)
        {
            return;
        }

        _project = project.ChangePerformance(performance.Id, kept => kept with { Name = name });
        CurrentPiece.Saved(_project);
        await Projects.SaveAsync(_project);
    }

    /// <summary>A note taken from a performance is changed by restoring the print, not by a correction.</summary>
    private bool RefuseTakenNote(Note note)
    {
        if (!PerformanceTake.IsTaken(note.Id))
        {
            return false;
        }

        _marksStatus = Text["performance.takenNote"];
        return true;
    }
}