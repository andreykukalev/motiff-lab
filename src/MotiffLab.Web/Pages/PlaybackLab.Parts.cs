using Microsoft.JSInterop;

using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Workspace;

namespace MotiffLab.Web.Pages;

/// <summary>
/// Pieces in a file, an experiment behind Settings · Experimental (v0.4 design B): the Contents make the bookmarks that
/// mark the pieces of a collection into pieces of their own (<see cref="PieceParts"/>). Each is saved as a new project
/// through the same path as any new piece, with its title, in the collection's playlist, keeping the collection's original
/// (marked held by this browser) and its place there (<see cref="Project.PartOf"/>, version 0.4). The collection is not
/// changed. What was made from what is read back from the project files alone: a piece says where it came from by the
/// original it keeps, and the collection finds its pieces among the library's rows (<see cref="ProjectSummary.Part"/>).
/// </summary>
public partial class PlaybackLab
{
    /// <summary>The experiment's switch in Settings · Experimental, off by default and kept on the device.</summary>
    private const string PiecePartsSetting = PartPlace.Setting;

    /// <summary>The pieces made from the piece open that are in the library; null when none.</summary>
    private SplitLink? _partsMade;

    /// <summary>The collection the piece open was made from, as its file names it; null for a piece of its own.</summary>
    private PartOrigin? _partOf;

    private bool PiecePartsOn => SettingsStore.IsOn(PiecePartsSetting);

    /// <summary>A piece opened: where it came from, from its own file, and the pieces of the library made from it.</summary>
    private async Task ReadPartsMadeAsync()
    {
        _partsMade = null;
        _partOf = null;
        if (_project is not { } project || !PiecePartsOn)
        {
            return;
        }

        PartSummary? own = project.Summary.Part;
        _partOf = own is null ? null : new PartOrigin(own.Original.Shown, project.Details.Playlists.FirstOrDefault(), own.Place);

        // A piece of a recording keeps the recording itself as its original: its sister pieces are not made from it.
        if (PieceParts.CollectionOriginal(project) is not { } original || (own is not null && PieceParts.IsPartOf(own, original)))
        {
            return;
        }

        try
        {
            _partsMade = SplitLink.Of(await Projects.ListAsync(), project.Id, original);
        }
        catch (JSException)
        {
            // The line is a convenience: the pieces are in the library all the same.
        }
    }

    /// <summary>
    /// Split confirmed: each piece is saved as a new project opening at its start, as a piece added from a file is, with
    /// where it is in the collection in its own file. Nothing is cut: the collection stays as it is.
    /// </summary>
    private async Task SavePartsAsync(PieceSplit split)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        foreach (PiecePart part in split.Parts)
        {
            Project project = part.Project;
            await Projects.SaveAsync(project.AddResumePoint(ResumePoint.Start(project.NextResumePointId(), now)));
        }

        _partsMade = new SplitLink(split.Playlist, [.. split.Parts.Select(static part => new PartLink(
            part.Project.Id, part.Project.Details.Title.Value, part.Bars?.First, part.Bars?.Last, part.Project.PartOf))]);
        _marksStatus = Text.Format("parts.made", Text.Count("parts.added", split.Parts.Count), split.Playlist);
    }
}