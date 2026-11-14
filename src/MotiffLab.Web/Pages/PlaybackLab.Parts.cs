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
    /// <summary>The pieces made from the piece open that are in the library; null when none.</summary>
    private SplitLink? _partsMade;

    /// <summary>The collection the piece open was made from, as its file names it; null for a piece of its own.</summary>
    private PartOrigin? _partOf;

    /// <summary>A piece opened: where it came from, from its own file, and the pieces of the library made from it.</summary>
    private async Task ReadPartsMadeAsync()
    {
        _partsMade = null;
        _partOf = null;
        await ReadOwnSoundAsync();
        if (_project is not { } project || !PiecesOn)
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
    /// where it is in the collection in its own file. Nothing is cut: the collection stays as it is. A piece of a recording
    /// whose stretch heard is kept here (version 0.5) keeps its own stretch of the sound on this device, described in its
    /// file (<see cref="PieceParts.WithSound"/>); then the first piece opens with Add a source, for its score.
    /// </summary>
    private async Task SavePartsAsync(PieceSplit split)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        List<(Project Project, string Point)> saved = [];
        foreach (PiecePart part in split.Parts)
        {
            Project project = await WithOwnSoundAsync(part.Project, now);
            ResumePoint start = ResumePoint.Start(project.NextResumePointId(), now);
            await Projects.SaveAsync(project.AddResumePoint(start));
            saved.Add((project, start.Id));
        }

        _partsMade = new SplitLink(split.Playlist, [.. split.Parts.Select(static part => new PartLink(
            part.Project.Id, part.Project.Details.Title.Value, part.Bars?.First, part.Bars?.Last, part.Project.PartOf))]);
        _marksStatus = Text.Format("parts.made", Text.Count("parts.added", split.Parts.Count), split.Playlist);
        if (_project is { Transcription: not null } && saved is [var (first, point), ..] && await CurrentPiece.OpenSavedAsync(first.Id))
        {
            Navigation.NavigateTo($"{MotiffLab.Web.Import.CurrentPiece.WorkspaceUrl(first, point)}&view={AddSourceView}");
        }
    }

    /// <summary>A piece of a recording with its own stretch of the sound kept here, cut from the stretch heard; as it was when none is kept.</summary>
    private async Task<Project> WithOwnSoundAsync(Project part, DateTimeOffset now)
    {
        if (_project is not { Transcription: not null } collection || part.PartOf?.Milliseconds is not { } stretch)
        {
            return part;
        }

        try
        {
            IJSObjectReference store = await RecordingStoreAsync();
            CutSound? cut = await store.InvokeAsync<CutSound?>(
                "keepStretchOf", $"{collection.Id}/{PartPlace.ConcertSound}", OwnSoundKeyOf(part.Id), stretch.From, stretch.To);
            return cut is null ? part : PieceParts.WithSound(part, cut.Bytes, cut.Milliseconds, cut.Sha256, HoldingDevice.Web, now);
        }
        catch (JSException)
        {
            return part;
        }
    }
}

/// <summary>A piece's own sound as kept on the device: its WAV file's size, length and SHA-256 (<c>recording-store.ts</c>).</summary>
internal sealed record CutSound(long Bytes, long Milliseconds, string Sha256);