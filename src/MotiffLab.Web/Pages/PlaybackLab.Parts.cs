using Microsoft.JSInterop;

using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Workspace;

namespace MotiffLab.Web.Pages;

/// <summary>
/// Pieces in a file, an experiment behind Settings · Experimental (v0.4 design B, first version without a format change):
/// the Contents make the bookmarks that mark the pieces of a collection into pieces of their own (<see cref="PieceParts"/>).
/// Each is saved as a new project through the same path as any new piece, with its title, in the collection's playlist,
/// naming the collection's original by its fingerprint and marked held by this browser. The collection is not changed;
/// which pieces were made from it, and where each was (for a recording, its stretch of the sound), is kept in this
/// browser beside the projects (<c>Scripts/storage/piece-parts.ts</c>) until the format step keeps it in the projects.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>The experiment's switch in Settings · Experimental, off by default and kept on the device.</summary>
    private const string PiecePartsSetting = "experimental.pieceParts";

    private IJSObjectReference? _partsModule;

    /// <summary>The pieces this browser made from the piece open, those still in the library; null when none.</summary>
    private SplitLink? _partsMade;

    /// <summary>The collection the piece open was split from, when this browser split it and the collection is still here; null otherwise.</summary>
    private PartOrigin? _partOf;

    private bool PiecePartsOn => SettingsStore.IsOn(PiecePartsSetting);

    private async Task<IJSObjectReference> PartsModuleAsync() =>
        _partsModule ??= await JS.InvokeAsync<IJSObjectReference>("import", "./js/storage/piece-parts.js");

    /// <summary>A piece opened: what this browser made from it before, with the pieces deleted since left out, and what it was made from.</summary>
    private async Task ReadPartsMadeAsync()
    {
        _partsMade = null;
        _partOf = null;
        if (_project is not { } project || !PiecePartsOn)
        {
            return;
        }

        try
        {
            IJSObjectReference module = await PartsModuleAsync();
            SplitLink? kept = await module.InvokeAsync<SplitLink?>("splitOf", project.Id);
            PartOrigin? origin = await module.InvokeAsync<PartOrigin?>("partOf", project.Id);
            if (kept is null && origin is null)
            {
                return;
            }

            HashSet<string> saved = [.. (await Projects.ListAsync()).Select(static summary => summary.Id)];
            if (kept is not null && kept.Parts.Where(part => saved.Contains(part.Id)).ToArray() is { Length: > 0 } left)
            {
                _partsMade = kept with { Parts = left };
            }

            _partOf = origin is not null && saved.Contains(origin.Collection) ? origin : null;
        }
        catch (JSException)
        {
            // The lines are a convenience: the pieces are in the library all the same.
        }
    }

    /// <summary>
    /// Split confirmed: each piece is saved as a new project opening at its start, as a piece added from a file is, and
    /// this browser remembers which pieces came from the collection. Nothing is cut: the collection stays as it is.
    /// </summary>
    private async Task SavePartsAsync(PieceSplit split)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        foreach (PiecePart part in split.Parts)
        {
            Project project = part.Project;
            await Projects.SaveAsync(project.AddResumePoint(ResumePoint.Start(project.NextResumePointId(), now)));
        }

        var made = new SplitLink(_project?.Details.Title.Value ?? "", split.Playlist, [.. split.Parts.Select(static part => new PartLink(
            part.Project.Id, part.Project.Details.Title.Value, part.Bars?.First, part.Bars?.Last, part.Milliseconds?.From, part.Milliseconds?.To))]);
        _partsMade = made;
        _marksStatus = Text.Format("parts.made", Text.Count("parts.added", made.Parts.Count), made.Playlist);
        if (_project is { } collection)
        {
            try
            {
                await (await PartsModuleAsync()).InvokeVoidAsync("keepSplit", collection.Id, made);
            }
            catch (JSException)
            {
                // Blocked storage: the pieces are saved; only the collection's line is not kept.
            }
        }
    }
}