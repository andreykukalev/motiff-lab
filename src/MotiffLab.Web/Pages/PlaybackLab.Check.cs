using Microsoft.AspNetCore.Components;

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

    /// <summary>Whether the piece came with Check flags, open or resolved: the Check layer.</summary>
    private bool HasCheckFlags => _project?.Check is { Count: > 0 };

    /// <summary>Whether the piece has a Check drawer: flags to check, or a transcription whose bars and tempo it holds.</summary>
    private bool HasCheck => HasCheckFlags || _project?.Transcription is not null;

    /// <summary>The open flags by bar, in bar order.</summary>
    private IReadOnlyList<IGrouping<int, CheckFlag>> OpenCheckBars =>
        _project is { } project ? [.. project.OpenChecks.GroupBy(flag => flag.Bar)] : [];

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

        _checkFixing = null;
        _project = project.ResolveCheck(bar, resolved);
        _marksStatus = Text.Format(resolved ? "check.resolved" : "check.reopened", bar);
        if (_scanCheck is not null && _project.Scan is { } scan)
        {
            _imported = _scanCheck.Mark(_imported, scan.ConfirmedBars);
            await ApplyEditsAsync(_edits);
            return;
        }

        CurrentPiece.Saved(_project);
        await Projects.SaveAsync(_project);
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
}