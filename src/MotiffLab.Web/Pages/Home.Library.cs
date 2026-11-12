using Microsoft.AspNetCore.Components;

using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Projects;
using MotiffLab.Web.Workspace;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The library as the place to manage a couple of hundred pieces (version 0.2, ux-prototype/v2/screens/lib-1-list.png to
/// lib-4-props.png): the tabs, search, filters by composer, tag and playlist, the list ordered by its columns, and a piece's
/// properties: its title, composer, tags and playlists.
/// </summary>
public partial class Home
{
    [Inject]
    private LibraryShelfState Shelf { get; set; } = default!;

    private Project? _editing;

    /// <summary>The cards made again from the summaries and covers, after a listing or a change to one piece.</summary>
    private void Shelve() => Shelf.Restock(_summaries ?? [], _covers);

    private void OnShelfChanged() => _menu = null;

    private void Narrow(LibraryQuery query)
    {
        Shelf.Narrow(query);
        _menu = null;
    }

    /// <summary>
    /// Rows saved before the library kept a piece's title, composer and tags have none, and rows of a piece made from a
    /// collection saved before they kept where it came from (version 0.4) do not say so: each such piece is read once and
    /// its row written again, so search, the filters and the collection know it. The cards are made again when all are done.
    /// </summary>
    private async Task CompleteRowsAsync()
    {
        if (_summaries is not { } summaries || !summaries.Any(Incomplete))
        {
            return;
        }

        var completed = summaries.ToList();
        for (int index = 0; index < completed.Count; index++)
        {
            if (!Incomplete(completed[index]))
            {
                continue;
            }

            try
            {
                if (await Projects.LoadAsync(completed[index].Id) is { } project)
                {
                    completed[index] = await Projects.SaveSummaryAsync(project);
                }
            }
            catch (ProjectFileException)
            {
                // A damaged project keeps its row as it was; opening it says what is wrong.
            }
        }

        _summaries = completed;
        Shelve();
    }

    /// <summary>A row without the title the library shows, or of a piece made from a collection that does not say so.</summary>
    private static bool Incomplete(ProjectSummary summary) =>
        summary.Details is null || PartPlace.Unsaid(summary);

    private async Task OpenEditAsync(LibraryEntry entry)
    {
        _menu = null;
        _message = null;
        try
        {
            _editing = await Projects.LoadAsync(entry.Summary.Id);
            if (_editing is null)
            {
                _message = Text["project.gone"];
                return;
            }

            _sheet = Sheet.Edit;
        }
        catch (ProjectFileException exception)
        {
            _message = exception.Message;
        }
    }

    /// <summary>Keeps the title, composer, tags and playlists in the project, and the line and the filters change at once.</summary>
    private async Task SaveDetailsAsync(PieceMetadata metadata)
    {
        if (_editing is not { } project)
        {
            return;
        }

        project = project with { Metadata = metadata };
        await Projects.SaveAsync(project);
        ProjectSummary summary = project.Summary;
        _summaries = [.. (_summaries ?? []).Select(kept => kept.Id == summary.Id ? summary : kept)];
        Shelve();
        CloseSheet();
    }

    private static string OriginKey(PieceOrigin origin) => origin switch
    {
        PieceOrigin.Scan => "library.scanned",
        PieceOrigin.Pdf => "library.pdf",
        _ => "library.recorded",
    };
}