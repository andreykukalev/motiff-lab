using Microsoft.AspNetCore.Components;

using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The resume point the workspace works from (0018, section 9): the address names it ("&amp;resume=r_…"), the
/// header shows its name, and the passage, tempo and loop the player is on are saved into it as they work. A
/// project opened without one gets one, so every piece opens where it was left. Für Elise has no project, so
/// it has no resume point either. Until the redesign, the old workspace keeps the minimum: a project file
/// of version 12 reads and writes resume points in place of the sessions of 0012.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>The resume point the address names; see <see cref="CurrentResumePoint"/>.</summary>
    [SupplyParameterFromQuery(Name = "resume")]
    public string? ResumeId { get; set; }

    /// <summary>An idea to open on arrival ("&amp;idea=o_…"), from the project page's library.</summary>
    [SupplyParameterFromQuery(Name = "idea")]
    public string? IdeaId { get; set; }

    /// <summary>The id of the resume point in use, once the piece has loaded; null without a project.</summary>
    private string? _resumeId;

    private ResumePoint? CurrentResumePoint => _resumeId is { } id ? _project?.FindResumePoint(id) : null;

    /// <summary>Where ‹ leads: the project page, which lists the resume points and ideas, or the projects.</summary>
    private string BackUrl => _project is { } project ? ProjectPage.Url(project.Id) : "";

    private string BackLabel => _project is null ? Text["nav.projects"] : Text["nav.project"];

    /// <summary>The second line of the header: the resume point's name, or that the piece is not saved.</summary>
    private string SessionLine
    {
        get
        {
            if (_score is null)
            {
                return Text["workspace.emptyLine"];
            }

            return CurrentResumePoint is { Name: { } name } ? name : _project is null ? Text["workspace.notSaved"] : Text["workspace.stays"];
        }
    }

    /// <summary>
    /// Settles the resume point after the piece loads: the one the address names, else a new one to open the
    /// piece from; selects the passage it was on, sets its tempo, and opens an idea when asked.
    /// </summary>
    private async Task ArriveAtResumePointAsync()
    {
        _resumeId = null;
        if (_project is not { } project)
        {
            return;
        }

        ResumePoint? point = ResumeId is { Length: > 0 } named ? project.FindResumePoint(named) : null;
        bool fresh = false;
        if (point is null)
        {
            point = ResumePoint.Start(project.NextResumePointId(), DateTimeOffset.Now);
            _project = project.AddResumePoint(point);
            CurrentPiece.Saved(_project);
            await Projects.SaveAsync(_project);
            fresh = true;
        }

        _resumeId = point.Id;
        _tab = _scanCheck is null ? InspectorTab.Practise : InspectorTab.Tools;

        if (point.Passage is { } passage && _selection?.Span != passage.Span)
        {
            await MoveSelectionAsync(passage.Span, _filters.Contains(passage.Filter) ? passage.Filter : SelectionFilter.All, note: null);
        }

        if (point.TempoPercent != TempoPercent)
        {
            await SetTempoPercentAsync(point.TempoPercent);
        }

        string? idea = IdeaId;
        if (idea is { Length: > 0 } && Kept.FirstOrDefault(kept => kept.Id == idea) is { } observation)
        {
            _tab = InspectorTab.Explore;
            if (observation.Variation is not null)
            {
                await OpenObservationAsync(observation);
            }
            else
            {
                await MoveSelectionAsync(observation.Phrase.Span, observation.Phrase.Filter, Text.Format("workspace.openedIdea", observation.Name));
            }
        }

        if (fresh || idea is { Length: > 0 })
        {
            // The address names the resume point and no longer asks for the idea, so a reload comes back here as it is.
            Navigation.NavigateTo(MotiffLab.Web.Import.CurrentPiece.WorkspaceUrl(_project!, point.Id), replace: true);
        }
    }

    /// <summary>The project with the resume point's passage, tempo, loop and time brought up to date with the workspace.</summary>
    private Project TouchResumePoint(Project project) =>
        _resumeId is { } id && project.FindResumePoint(id) is not null
            ? project.ChangeResumePoint(id, point => point.Touch(
                point.At,
                _selection is { NoteIds.Count: > 0 } selection ? SavedPassage.Of(selection) : point.Passage,
                point.Focused,
                point.Overlays,
                Math.Clamp(TempoPercent, ResumePoint.MinTempoPercent, ResumePoint.MaxTempoPercent),
                point.Hands,
                _looping,
                DateTimeOffset.Now))
            : project;
}