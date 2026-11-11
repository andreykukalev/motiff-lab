using Microsoft.AspNetCore.Components;

using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Projects;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The library as the place to manage a couple of hundred pieces (version 0.2): search, order, filters by composer and
/// tag, cards or a compact list, and the title, composer and tags of a piece edited from its card.
/// </summary>
public partial class Home
{
    [Inject]
    private LibraryShelfState Shelf { get; set; } = default!;

    private Project? _editing;
    private string _editHeading = "";

    /// <summary>The cards made again from the summaries and covers, after a listing or a change to one piece.</summary>
    private void Shelve() => Shelf.Restock(_summaries ?? [], _covers);

    private void OnShelfChanged() => _menu = null;

    /// <summary>
    /// Rows saved before the library kept a piece's title, composer and tags have none: each such piece is read once and its
    /// row written again, so search and the filters know it. The cards are made again when all are done.
    /// </summary>
    private async Task CompleteRowsAsync()
    {
        if (_summaries is not { } summaries || summaries.All(summary => summary.Details is not null))
        {
            return;
        }

        var completed = summaries.ToList();
        for (int index = 0; index < completed.Count; index++)
        {
            if (completed[index].Details is not null)
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

            _editHeading = entry.Title;
            _sheet = Sheet.Edit;
        }
        catch (ProjectFileException exception)
        {
            _message = exception.Message;
        }
    }

    /// <summary>Keeps the title, composer and tags in the project, and the card and the filters change at once.</summary>
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

    /// <summary>"Beethoven · From a recording": what a line of the list says under the title.</summary>
    private string RowLine(LibraryEntry entry)
    {
        List<string> parts = [];
        if (entry.Composer is { } composer)
        {
            parts.Add(composer);
        }

        if (entry.Summary.Origin != PieceOrigin.File)
        {
            parts.Add(Text[OriginKey(entry.Summary.Origin)]);
        }

        return string.Join(" · ", parts);
    }

    private static string OriginKey(PieceOrigin origin) => origin switch
    {
        PieceOrigin.Scan => "library.scanned",
        PieceOrigin.Pdf => "library.pdf",
        _ => "library.recorded",
    };
}