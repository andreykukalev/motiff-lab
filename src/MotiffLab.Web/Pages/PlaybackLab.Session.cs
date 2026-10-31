using Microsoft.AspNetCore.Components;

using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The session the workspace works in (0012): the address names it ("&amp;session=s_…"), the header
/// says what it is for, the passage the player works on is saved into it, and what they keep is marked
/// with it. A project opened without a session gets one, so every piece of work has a context to come
/// back to. Für Elise has no project, so it has no session either.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>The session the address names; see <see cref="CurrentSession"/>.</summary>
    [SupplyParameterFromQuery(Name = "session")]
    public string? SessionId { get; set; }

    /// <summary>An idea to open on arrival ("&amp;idea=o_…"), from the project page's library.</summary>
    [SupplyParameterFromQuery(Name = "idea")]
    public string? IdeaId { get; set; }

    /// <summary>The id of the session in use, once the piece has loaded; null without a project.</summary>
    private string? _sessionId;

    private Session? CurrentSession => _sessionId is { } id ? _project?.FindSession(id) : null;

    /// <summary>Where ‹ leads: the project page, which lists the sessions and ideas, or the projects.</summary>
    private string BackUrl => _project is { } project ? ProjectPage.Url(project.Id) : "";

    private string BackLabel => _project is null ? Text["nav.projects"] : Text["nav.project"];

    /// <summary>The second line of the header: the session's intent and task, or that the piece is not saved.</summary>
    private string SessionLine
    {
        get
        {
            if (_score is null)
            {
                return Text["workspace.emptyLine"];
            }

            if (CurrentSession is not { } session)
            {
                return _project is null ? Text["workspace.notSaved"] : Text["workspace.stays"];
            }

            string intent = Text[$"intent.{ProjectPage.Key(session.Intent)}"];
            return session.Task is { } task ? $"{intent} · {task}" : intent;
        }
    }

    /// <summary>
    /// Settles the session after the piece loads: the one the address names, else a new one to open the
    /// piece in; selects the passage it was on, opens the tab its intent asks for, and an idea when asked.
    /// </summary>
    private async Task ArriveInSessionAsync()
    {
        _sessionId = null;
        if (_project is not { } project)
        {
            return;
        }

        Session? session = SessionId is { Length: > 0 } named ? project.FindSession(named) : null;
        bool fresh = false;
        if (session is null)
        {
            session = Session.Start(project.NextSessionId(), SessionIntent.Open, null, DateTimeOffset.Now);
            _project = project.StartSession(session);
            CurrentPiece.Saved(_project);
            await Projects.SaveAsync(_project);
            fresh = true;
        }

        _sessionId = session.Id;
        _tab = session.Intent switch
        {
            SessionIntent.Understand => InspectorTab.Understand,
            SessionIntent.Explore => InspectorTab.Explore,
            _ => _scanCheck is null ? InspectorTab.Practise : InspectorTab.Tools,
        };

        if (session.Passage is { } passage && _selection?.Span != passage.Span)
        {
            await MoveSelectionAsync(passage.Span, _filters.Contains(passage.Filter) ? passage.Filter : SelectionFilter.All, note: null);
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
            // The address names the session and no longer asks for the idea, so a reload comes back here as it is.
            Navigation.NavigateTo(MotiffLab.Web.Import.CurrentPiece.WorkspaceUrl(_project!, session.Id), replace: true);
        }
    }

    /// <summary>The project with the session's passage and time brought up to date with the selection.</summary>
    private Project TouchSession(Project project) =>
        _sessionId is { } id && project.FindSession(id) is not null
            ? project.ChangeSession(id, session => session.Touch(_selection is { NoteIds.Count: > 0 } selection ? SavedPassage.Of(selection) : session.Passage, DateTimeOffset.Now))
            : project;

    /// <summary>An observation marked as kept in the session, when there is one.</summary>
    private Observation InSession(Observation observation) => _sessionId is { } id && _project is not null ? observation with { SessionId = id } : observation;
}