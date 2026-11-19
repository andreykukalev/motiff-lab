using MotiffLab.MusicCore.Pieces;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// The PDF piece a piece's notes were read from (0023; Andrei, 2026-11-05): the PDF's project, which is never changed, and
/// where each of its pages starts in these notes, so the player switches between the score as it is and the notes read
/// from it at the same place.
/// </summary>
/// <param name="ProjectId">The PDF piece's project id.</param>
/// <param name="Pages">Where each of its pages starts in these notes.</param>
public sealed record ReadFromPdf(string ProjectId, PageMap Pages)
{
    public string ProjectId { get; } = !string.IsNullOrWhiteSpace(ProjectId) ? ProjectId : throw new ArgumentException("The PDF piece's id is required.", nameof(ProjectId));

    public PageMap Pages { get; } = Pages ?? throw new ArgumentNullException(nameof(Pages));
}