using Microsoft.AspNetCore.Components;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The PDF view under the new sources (version 0.6, behind experimental.sources): the switch of all the piece's views and the
/// Sources button in its bar, and another PDF or photos original of the piece shown in place of its first PDF, only read.
/// </summary>
public partial class PdfReader
{
    /// <summary>Another PDF or photos original of the piece than its first PDF, by its id (version 0.6, the new sources).</summary>
    [SupplyParameterFromQuery(Name = "original")]
    public string? OriginalId { get; set; }

    private bool SourcesOn => MotiffLab.Web.Sources.SourcesExperiment.IsOn(SettingsStore);

    /// <summary>The original shown when it is not the piece's first PDF: its marks are not kept, so its pages are only read.</summary>
    private string? ShownOriginal => SourcesOn && OriginalId is { Length: > 0 } id && id != PieceOriginal.DocumentId ? id : null;

    /// <summary>The bytes of the PDF shown: the piece's PDF, or another original kept in its file.</summary>
    private byte[]? ShownDocument => ShownOriginal is { } id
        ? _project?.Sources.Data.GetValueOrDefault(id) is { } bytes && PdfDocument.IsPdf(bytes) ? bytes : null
        : _project?.Document;

    /// <summary>Whether the PDF view can lead to a view: Notes and versions on the notes' page, a PDF whose bytes are in the piece.</summary>
    private static bool CanShow(Project project, SourceView view) => view.Kind != ViewKind.Original
        || (view.Id == PieceOriginal.DocumentId ? project.Document is not null : project.Sources.Data.ContainsKey(view.Id));

    /// <summary>A view picked: the notes as the player left them, another PDF here, other Notes or a version on the notes' page.</summary>
    private async Task PickViewAsync(Project project, string key)
    {
        if (key.Split(':', 2) is not [var kind, var id])
        {
            return;
        }

        if (kind == "original")
        {
            string other = id == PieceOriginal.DocumentId ? "" : $"&original={Uri.EscapeDataString(id)}";
            Navigation.NavigateTo($"pdf?project={Uri.EscapeDataString(project.Id)}{other}");
        }
        else if (kind == "notes" && id == project.NotesId)
        {
            await ShowNotesAsync();
        }
        else
        {
            Navigation.NavigateTo($"{MotiffLab.Web.Import.CurrentPiece.WorkspaceUrl(project, project.RecentResumePoints.FirstOrDefault()?.Id)}&view={Uri.EscapeDataString(key)}");
        }
    }

    /// <summary>The Sources button: the notes' page with the Sources sheet open.</summary>
    private void OpenSources(Project project) =>
        Navigation.NavigateTo($"{MotiffLab.Web.Import.CurrentPiece.WorkspaceUrl(project, project.RecentResumePoints.FirstOrDefault()?.Id)}&view=versions");
}