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

    /// <summary>The performance asked about before it is removed for good.</summary>
    private string? _performanceRemoving;

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

    private (PerformanceLayerNotes Layer, IReadOnlyList<Note> Notes, bool Readable, bool Artefacts, ScoreSource Score, IReadOnlyList<string> Added, IReadOnlyList<string> ArtefactIds)? _versionLayer;

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

    /// <summary>
    /// The score with the other version on small staves above every system (0022, section 4), when its layer is on: what
    /// agrees is written as printed and what the version adds is plum. Engraved again only when the version, the notes or
    /// the writing change.
    /// </summary>
    private ScoreSource? VersionScore
    {
        get
        {
            if (!LayerShown("version") || ShownPerformance is not { } performance || VersionNotes is not { } versionNotes
                || _notes is not { } notes || _score is null || _versionNotes is not { } version)
            {
                return null;
            }

            // Likely artefacts of a recording's hearing stay out unless asked for, in grey (0022, section 4).
            bool artefacts = LayerShown("artefacts") && version.Layer.Artefacts.Count > 0;
            if (_versionLayer is { } cached && ReferenceEquals(cached.Layer, version.Layer) && ReferenceEquals(cached.Notes, notes) && cached.Readable == _readable
                && cached.Artefacts == artefacts)
            {
                return cached.Score;
            }

            IReadOnlyList<Note> shown = artefacts ? version.Layer.Notes : versionNotes;
            ScoreSource score = ScoreSource.FromNotes(Notation(notes, _score.End, [new NotationOssia(performance.Name, shown, AsHeard: true)]));
            string prefix = NotationGenerator.OssiaIdPrefix(0);
            HashSet<string> artefactIds = [.. version.Layer.Artefacts];
            _versionLayer = (version.Layer, notes, _readable, artefacts, score,
                [.. version.Layer.Added.Where(id => !artefactIds.Contains(id)).Select(id => prefix + id)],
                artefacts ? [.. version.Layer.Artefacts.Select(id => prefix + id)] : []);
            return score;
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

    /// <summary>Take into the score, in focus: every bar of the phrase the version has otherwise, at once.</summary>
    private async Task TakeFocusBarsAsync()
    {
        if (_project is not { } project || ShownPerformance is not { } performance || ShownComparison is not { } comparison || _meter is null)
        {
            return;
        }

        List<TakenBar> taken = [];
        foreach (BarComparison bar in FocusVersionBars)
        {
            try
            {
                taken.Add(PerformanceTake.Take(performance.Id, bar, performance.Notes(), comparison.Alignment, _meter, PrintedNotes));
            }
            catch (ArgumentException)
            {
                // A bar that does not line up has nothing to take; the others still are.
            }
        }

        if (taken.Count == 0)
        {
            return;
        }

        _project = project.ChangePerformance(performance.Id, kept => kept.TakeBars(taken));
        _performanceStatus = Text.Format("performance.takenBars", string.Join(", ", taken.Select(bar => bar.Bar)), performance.Name);
        await ApplyEditsAsync(_edits);
    }

    /// <summary>Restore these notes, in focus: the bars of the phrase taken from the version come back as they were.</summary>
    private async Task RestoreFocusBarsAsync()
    {
        if (_project is not { } project || ShownPerformance is not { } performance || FocusTakenBars is not { Count: > 0 } bars)
        {
            return;
        }

        _project = project.ChangePerformance(performance.Id, kept => kept.Restore(bars));
        _performanceStatus = Text.Format("performance.restoredBars", string.Join(", ", bars));
        await ApplyEditsAsync(_edits);
    }

    /// <summary>The notes the other version adds, plum in its layer; null while the layer is off.</summary>
    private IReadOnlyList<string>? ScoreVersionAdded => VersionScore is null ? null : _versionLayer?.Added;

    /// <summary>The likely artefacts on the layer's staves, in grey, when they are shown.</summary>
    private IReadOnlyList<string>? ScoreVersionArtefacts => VersionScore is null ? null : _versionLayer?.ArtefactIds;

    /// <summary>How many notes of the shown version are likely artefacts of a recording's hearing; 0 when none is shown.</summary>
    private int VersionArtefactCount => VersionNotes is not null && _versionNotes is { } version ? version.Layer.Artefacts.Count : 0;

    private IReadOnlyList<ScorePerformanceMarker>? ScorePerformanceMarkers => PerformanceMarks?.Markers;

    private IReadOnlyList<HighlightPlace>? ScoreUnheard => PerformanceMarks?.Unheard;

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
        _performanceRemoving = null;
        _popover = Popover.None;
    }

    /// <summary>A marker tapped on the score: the drawer opens on its bar, selected.</summary>
    private async Task OnPerformancePickedAsync(int bar)
    {
        _performanceOpen = true;
        _insightsOpen = false;
        _checkOpen = false;
        await ShowPerformanceBarAsync(bar);
    }

    private void ShowPerformanceOf(string id)
    {
        _performanceId = id;
        _performanceRemoving = null;
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
    /// a recording's. Its sound is not kept.
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
        _comparison = (performance.Id, performance.Midi, performance.Anchors, _edits, _imported, comparison);
        _performanceStatus = comparison.MatchedCount == 0
            ? Text["performance.noMatch"]
            : Text.Format("performance.added", comparison.MatchedCount, printed.Count, comparison.Differences.Count());
        CurrentPiece.Saved(_project);
        await Projects.SaveAsync(_project);
    }

    /// <summary>Take into the score: the bar's printed notes not heard leave, its played notes not printed come in, on the 16th grid.</summary>
    private async Task TakePerformanceBarAsync(BarComparison bar)
    {
        if (_project is not { } project || ShownPerformance is not { } performance || ShownComparison is not { } comparison || _meter is null)
        {
            return;
        }

        TakenBar taken;
        try
        {
            taken = PerformanceTake.Take(performance.Id, bar, performance.Notes(), comparison.Alignment, _meter, PrintedNotes);
        }
        catch (ArgumentException)
        {
            _performanceStatus = Text.Format("performance.notAligned", bar.Bar);
            return;
        }

        _project = project.ChangePerformance(performance.Id, kept => kept.TakeBars([taken]));
        _performanceStatus = Text.Format("performance.taken", bar.Bar, performance.Name);
        await ApplyEditsAsync(_edits);
    }

    /// <summary>Restore the print: the bar's printed notes come back and the performance's leave.</summary>
    private async Task RestorePerformanceBarAsync(int bar)
    {
        if (_project is not { } project || ShownPerformance is not { } performance)
        {
            return;
        }

        _project = project.ChangePerformance(performance.Id, kept => kept.Restore([bar]));
        _performanceStatus = Text.Format("performance.restored", bar);
        await ApplyEditsAsync(_edits);
    }

    /// <summary>Not a difference, or Show again: the bar's marker leaves the score or comes back, and the choice is kept.</summary>
    private async Task DismissPerformanceBarAsync(int bar, bool dismissed)
    {
        if (_project is not { } project || ShownPerformance is not { } performance)
        {
            return;
        }

        _project = project.ChangePerformance(performance.Id, kept => kept.Dismiss(bar, dismissed));
        _performanceStatus = Text.Format(dismissed ? "performance.dismissed" : "performance.undismissed", bar);
        CurrentPiece.Saved(_project);
        await Projects.SaveAsync(_project);
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

    /// <summary>Remove: asked once, then the performance, its markers, its taken bars and its Check flags go.</summary>
    private async Task RemovePerformanceAsync()
    {
        if (_project is not { } project || ShownPerformance is not { } performance)
        {
            return;
        }

        if (_performanceRemoving != performance.Id)
        {
            _performanceRemoving = performance.Id;
            return;
        }

        bool hadTaken = performance.Taken.Count > 0;
        _project = project.RemovePerformance(performance.Id);
        _performanceRemoving = null;
        _performanceId = null;
        _scoreChecks = null;
        _performanceStatus = Text.Format("performance.removed", performance.Name);
        if (hadTaken)
        {
            await ApplyEditsAsync(_edits);
            return;
        }

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