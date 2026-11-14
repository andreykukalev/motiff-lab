using Microsoft.AspNetCore.Components;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Score;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The Check layer and drawer (0018 section 9; design/kit/docs/01-ux-concept.md, "Library, sessions and import"): the bars a
/// scan or a transcription is unsure of are flagged on the score with a small amber "?" and their reasons; the Check drawer
/// lists them with Show, Fix notes and Looks right, and the resolved ones to check again. When every flag is resolved the
/// layer disappears. It takes the place of the scan's check in Insights' More tools and of the import report's list.
/// </summary>
public partial class PlaybackLab
{
    private bool _checkOpen;

    /// <summary>The bar whose notes are open under Fix notes in the drawer; null when none is.</summary>
    private int? _checkFixing;

    private (IReadOnlyList<CheckFlag>? From, string Language, IReadOnlyList<ScoreCheckFlag> Drawn)? _scoreChecks;

    private (ImportedPiece Piece, IReadOnlyList<Note> Notes, IReadOnlyList<UncertainLengths> Bars)? _uncertainLengths;

    /// <summary>Whether the piece came with Check flags, open or resolved: the Check layer.</summary>
    private bool HasCheckFlags => _project?.Check is { Count: > 0 };

    /// <summary>Whether the piece has a Check drawer: flags to check, or a transcription whose bars and tempo it holds.</summary>
    private bool HasCheck => HasCheckFlags || _project?.Transcription is not null;

    /// <summary>The open flags by bar, in bar order.</summary>
    private IReadOnlyList<IGrouping<int, CheckFlag>> OpenCheckBars =>
        _project is { } project ? [.. project.OpenChecks.GroupBy(flag => flag.Bar)] : [];

    /// <summary>The open flags by bar that the drawer lists: every piece's, or, after Open on a piece, that piece's alone.</summary>
    private IReadOnlyList<IGrouping<int, CheckFlag>> ListedCheckBars =>
        _checkPiece is { } piece && CheckPiecesShown.Any(shown => shown.FirstBar == piece.First)
            ? [.. OpenCheckBars.Where(bar => bar.Key >= piece.First && bar.Key <= piece.Last)]
            : OpenCheckBars;

    /// <summary>The bars resolved, every flag on them, in bar order.</summary>
    private IReadOnlyList<IGrouping<int, CheckFlag>> ResolvedCheckBars =>
        _project?.Check is { } check ? [.. check.Where(flag => flag.Resolved).OrderBy(flag => flag.Bar).GroupBy(flag => flag.Bar)] : [];

    /// <summary>The flags for the score, the same list until the flags or the language change.</summary>
    private IReadOnlyList<ScoreCheckFlag>? ScoreChecks
    {
        get
        {
            if (_meter is null || _project is not { } project)
            {
                return null;
            }

            if (_scoreChecks is { } cached && ReferenceEquals(cached.From, project.Check) && cached.Language == Text.Language)
            {
                return cached.Drawn;
            }

            IReadOnlyList<ScoreCheckFlag> drawn = [.. OpenCheckBars
                .Where(bar => bar.Key >= _meter.FirstBarNumber)
                .Select(bar => new ScoreCheckFlag(bar.Key, _meter.GetBar(bar.Key).Span.Onset.Value, Text.Format("check.flagLabel", bar.Key, ReasonNames(bar))))];
            _scoreChecks = (project.Check, Text.Language, drawn);
            return drawn;
        }
    }

    /// <summary>"Bar too long, grace notes": the reasons of a bar's flags, each once.</summary>
    private string ReasonNames(IEnumerable<CheckFlag> flags)
    {
        string[] names = [.. flags.Select(flag => flag.Reason).Distinct().Select(reason => CheckFlag.NameOf(reason, Words))];
        return names.Length == 0 ? "" : string.Join(", ", names.Select((name, index) => index == 0 ? name : name.ToLower(Text.Culture)));
    }

    /// <summary>"Pedal held at the end, long note": why a bar's lengths are uncertain, each reason once.</summary>
    private string LengthNames(UncertainLengths bar) =>
        string.Join(", ", bar.Reasons.Select((reason, index) => index == 0 ? HeardLengths.NameOf(reason, Words) : HeardLengths.NameOf(reason, Words).ToLower(Text.Culture)));

    /// <summary>"Read from a scan by homr 0.7.0" or "Heard in a recording by Transkun": where the doubts come from.</summary>
    private string CheckSourceLine => _project switch
    {
        { Scan: { } scan } => Text.Format("check.fromScan", scan.Engine),
        { Transcription: { } heard } => Text.Format("check.fromRecording", heard.Engine),
        _ => "",
    };

    private void ToggleCheck()
    {
        _checkOpen = !_checkOpen;
        _insightsOpen &= !_checkOpen;
        _performanceOpen &= !_checkOpen;
        _popover = Popover.None;
    }

    /// <summary>A flag tapped on the score: the drawer opens on its bar, selected.</summary>
    private async Task OnCheckPickedAsync(int bar)
    {
        _checkOpen = true;
        _insightsOpen = false;
        _performanceOpen = false;
        await ShowCheckBarAsync(bar);
    }

    /// <summary>Show: the bar is selected, so it can be looped and compared with the page or the recording.</summary>
    private Task ShowCheckBarAsync(int bar)
    {
        if (_meter is null || _score is null || bar < _meter.FirstBarNumber)
        {
            return Task.CompletedTask;
        }

        // A scanned measure that runs long covers more than one bar of the meter.
        TickSpan span = _scanCheck?.Find(bar) is { } scanned
            ? MotifSelector.Bars(_meter, bar, _meter.ToPosition(new Tick(scanned.Span.End.Value - 1)).Bar, _score.End)
            : MotifSelector.Bars(_meter, bar, bar, _score.End);
        return SelectAsync(span, note: Text.Format("check.showing", bar));
    }

    /// <summary>Fix notes: the bar is selected and its notes open to correct, under its entry.</summary>
    private async Task FixCheckBarAsync(int bar)
    {
        await ShowCheckBarAsync(bar);
        _checkFixing = _checkFixing == bar ? null : bar;
    }

    /// <summary>Looks right, or Check again: every flag on the bar resolved or open again; for a scan the bar's notes read as confirmed.</summary>
    private async Task ResolveCheckAsync(int bar, bool resolved)
    {
        if (_project is not { } project || !(project.Check ?? []).Any(flag => flag.Bar == bar))
        {
            return;
        }

        _checkPieceUndo = null;
        await CheckChangedAsync(project.ResolveCheck(bar, resolved), Text.Format(resolved ? "check.resolved" : "check.reopened", bar));
    }

    /// <summary>The flags changed: the project saved with them, and what reads the checked bars brought up to date.</summary>
    private async Task CheckChangedAsync(Project changed, string status)
    {
        _checkFixing = null;
        _project = changed;
        _marksStatus = status;
        if (_scanCheck is not null && _project.Scan is { } scan)
        {
            _imported = _scanCheck.Mark(_imported, scan.ConfirmedBars);
            await ApplyEditsAsync(_edits);
            return;
        }

        // The bar's notes are checked, or to check again: what was read from them says so (#352).
        if (_selection is { } selection)
        {
            Analyze(selection);
        }

        if (_dna is not null)
        {
            _dna = BuildDna();
        }

        CurrentPiece.Saved(_project);
        await Projects.SaveAsync(_project);
    }

    /// <summary>The recognised notes still to check: those in the bars with an open Check flag (#352).</summary>
    private NoteDoubt Doubt => _project is { } project && _notes is { } notes && _meter is { } meter
        ? NoteDoubt.Of(notes, meter, project.OpenChecks.Select(static flag => flag.Bar), Words)
        : NoteDoubt.None;

    /// <summary>
    /// The notes heard in a recording whose length is uncertain, by bar (#352): worked out from the notes as they are, never
    /// saved; empty for any other piece.
    /// </summary>
    private IReadOnlyList<UncertainLengths> UncertainLengthBars
    {
        get
        {
            if (CurrentPiece.Piece is not { } piece || _notes is not { } notes)
            {
                return [];
            }

            // Worked out once for the notes as they are, not on every render of the drawer.
            if (_uncertainLengths is not { } cached || !ReferenceEquals(cached.Piece, piece) || !ReferenceEquals(cached.Notes, notes))
            {
                _uncertainLengths = cached = (piece, notes, HeardLengths.Of(piece, notes));
            }

            return cached.Bars;
        }
    }

    /// <summary>The bar the selection starts in, so its entry in the drawer is marked; null without a selection.</summary>
    private int? SelectedCheckBar => _selection is { } selection && _meter is not null ? _meter.BarAt(selection.Span.Onset).Number : null;

    /// <summary>For a scan, the selected bars that Check did not flag and the player has not confirmed: they can be confirmed too.</summary>
    private IReadOnlyList<int> UncheckedSelectedBars => _project?.Scan is { } scan
        ? [.. SelectedScanBars.Where(bar => !scan.IsConfirmed(bar) && !(_project.Check ?? []).Any(flag => flag.Bar == bar))]
        : [];

    /// <summary>
    /// The head of the drawer for a transcription (0018 section 9, decision record 0016): the bars and tempo worked out
    /// from the notes, turned off or on, in another signature, at twice or half the tempo, or a beat earlier or later.
    /// The piece opens again in the new bars, with its flags worked out for them.
    /// </summary>
    private async Task ChangeBarsAsync(Func<Project, BarGrid?> bars, string status)
    {
        if (_project is not { CanChangeBars: true } project)
        {
            return;
        }

        BarGrid? chosen = bars(project);
        if (Equals(chosen, project.Transcription!.Bars))
        {
            return;
        }

        Project changed = project.ChangeBars(chosen);
        await Projects.SaveAsync(changed);
        OpenedProject opened = OpenedProject.Open(changed);
        _checkFixing = null;
        _scoreChecks = null;
        _marksStatus = status;
        CurrentPiece.Open(opened.Piece, _filter, changed, opened);
    }

    private Task ToggleBarsAsync(ChangeEventArgs args)
    {
        if (args.Value is not true)
        {
            return ChangeBarsAsync(_ => null, Text["check.barsOff"]);
        }

        if (_project is not { CanChangeBars: true } project)
        {
            return Task.CompletedTask;
        }

        if (project.FindBars() is not { } found)
        {
            _marksStatus = Text["transcribe.barsNone"];
            return Task.CompletedTask;
        }

        return ChangeBarsAsync(_ => found, Text["check.barsOn"]);
    }

    private Task ChangeSignatureAsync(ChangeEventArgs args) =>
        _project?.Transcription?.Bars is { } bars && BarGrid.Signatures.FirstOrDefault(signature => signature.ToString() == args.Value as string) is { Numerator: > 1 } chosen
            ? ChangeBarsAsync(project => project.FindBars(chosen, bars.StepsPerBeat) ?? bars, Text.Format("check.barsSignature", chosen))
            : Task.CompletedTask;

    /// <summary>Twice or half the tempo: the same notes written in note values half or twice as long.</summary>
    private Task ChangeTempoAsync(bool faster)
    {
        if (_project?.Transcription?.Bars is not { } bars || !CanChangeTempo(bars, faster))
        {
            return Task.CompletedTask;
        }

        int steps = faster ? bars.StepsPerBeat * 2 : bars.StepsPerBeat / 2;
        return ChangeBarsAsync(project => project.FindBars(bars.Signature, steps) ?? bars, Text[faster ? "check.barsFaster" : "check.barsSlower"]);
    }

    private static bool CanChangeTempo(BarGrid bars, bool faster) =>
        faster ? bars.StepsPerBeat * 2 <= BarGrid.MostStepsPerBeat : bars.StepsPerBeat % 2 == 0 && bars.StepsPerBeat / 2 >= BarGrid.FewestStepsPerBeat;

    /// <summary>Every bar line a beat earlier or later, for a downbeat the bass misplaced.</summary>
    private Task ShiftBarsAsync(int beats) =>
        _project?.Transcription?.Bars is { } bars
            ? ChangeBarsAsync(_ => bars.Shifted(beats), Text[beats < 0 ? "check.barsEarlier" : "check.barsLater"])
            : Task.CompletedTask;

    /// <summary>The piece the list keeps to, after Open (version 0.5, Check by pieces): its first and last bars; null for every piece.</summary>
    private (int First, int Last)? _checkPiece;

    /// <summary>The last Looks right for a whole piece: the project, the piece's first bar and the bars it resolved, kept here only, for Undo.</summary>
    private (string ProjectId, int FirstBar, IReadOnlyList<int> Bars)? _checkPieceUndo;

    private (Project From, IReadOnlyList<CheckPiece> Pieces)? _checkPieces;

    /// <summary>The pieces of the layer with their bars to check, while the experiment is on; empty without a layer or flags.</summary>
    private IReadOnlyList<CheckPiece> CheckPiecesShown
    {
        get
        {
            if (!PiecesOn || _project is not { } project || _meter is not { } meter)
            {
                return [];
            }

            if (_checkPieces is not { } cached || !ReferenceEquals(cached.From, project))
            {
                _checkPieces = cached = (project, CheckPieces.Of(project, meter));
            }

            return cached.Pieces;
        }
    }

    /// <summary>The first bar of the piece whose Looks right can be undone, in the piece open.</summary>
    private int? CheckPieceUndoable => _checkPieceUndo is { } undo && undo.ProjectId == _project?.Id ? undo.FirstBar : null;

    /// <summary>Open: the list keeps to the piece's bars; again, or null, to every piece's.</summary>
    private void ShowCheckPiece(CheckPiece? piece) => _checkPiece = piece is null ? null : (piece.FirstBar, piece.LastBar);

    /// <summary>Looks right for a whole piece: every flagged bar still open in it resolved, in one save (music-core's <see cref="CheckPieces.Resolve(Project, IEnumerable{int}, bool)"/>).</summary>
    private async Task ResolveCheckPieceAsync(CheckPiece piece)
    {
        if (_project is not { } project || CheckPieces.OpenBars(project, piece.FirstBar, piece.LastBar) is not { Count: > 0 } bars)
        {
            return;
        }

        await CheckChangedAsync(CheckPieces.Resolve(project, bars, resolved: true), Text.Format("checkPieces.resolved", piece.Title, Text.Count("checkPieces.bars", bars.Count)));
        _checkPieceUndo = (project.Id, piece.FirstBar, bars);
    }

    /// <summary>Undo: the bars the last Looks right for a piece resolved are open again.</summary>
    private async Task UndoCheckPieceAsync()
    {
        if (_checkPieceUndo is not { } undo || _project is not { } project || undo.ProjectId != project.Id)
        {
            return;
        }

        _checkPieceUndo = null;
        int[] bars = [.. undo.Bars.Where(bar => (project.Check ?? []).Any(flag => flag.Bar == bar))];
        await CheckChangedAsync(CheckPieces.Resolve(project, bars, resolved: false), Text.Count("checkPieces.undone", bars.Length));
    }
}