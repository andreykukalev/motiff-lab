namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// A project's card in the library (0018, section 9): the piece, its resume points and ideas, when it was last opened,
/// where it came from, how many bars Check still asks about, and the latest resume point, without loading the project.
/// </summary>
public sealed record ProjectSummary(string Id, string FileName, int MotifCount, int VariationCount, DateTimeOffset LastOpened, int ResumeCount = 0, int IdeaCount = 0)
{
    /// <summary>A file, a scan or a recording; summaries saved before it was kept read as a file.</summary>
    public PieceOrigin Origin { get; init; }

    /// <summary>The bars with Check flags not yet resolved.</summary>
    public int CheckBars { get; init; }

    /// <summary>The resume point worked at last, or null when there is none (or the summary was saved before it was kept).</summary>
    public ResumeSummary? Latest { get; init; }

    /// <summary>For notes read from a PDF piece, that piece's id (0023), so the PDF view finds its notes; null otherwise.</summary>
    public string? ReadFrom { get; init; }

    /// <summary>The title, composer and tags the card shows (<see cref="Project.Details"/>); null for a summary saved before they were kept.</summary>
    public PieceDetails? Details { get; init; }

    /// <summary>The piece's originals and where their bytes are (version 0.3); empty for a summary saved before they were kept.</summary>
    public IReadOnlyList<OriginalSummary> Originals { get; init; } = [];

    /// <summary>
    /// For a piece made from a collection (version 0.4, «Пьесы в файле»), the collection's original it keeps and where it is
    /// in it; null for a piece of its own, and for a summary saved before it was kept.
    /// </summary>
    public PartSummary? Part { get; init; }

    /// <summary>
    /// What the piece is known by on every device (version 0.6.1, <see cref="SamePiece.Keys"/>), so the library finds the
    /// projects that may be one piece without reading them; null for a summary saved before it was kept. The apps fill it from
    /// the project; <see cref="Project.Summary"/> leaves it out.
    /// </summary>
    public IReadOnlyList<string>? PieceKeys { get; init; }
}

/// <summary>
/// A piece made from a collection, as its library row tells it (version 0.4): the collection's original as the piece keeps
/// it (<see cref="PieceParts.OriginalId"/>) and its place there (<see cref="Projects.PartOf"/>), null for a part of a score
/// cut by bars, whose place the format has no value for.
/// </summary>
public sealed record PartSummary(PieceOriginal Original, PartOf? Place);

/// <summary>A resume point as its piece's card shows it: its name, the position and passage in ticks, and when it was last active.</summary>
public sealed record ResumeSummary(string Id, string? Name, long At, long? From, long? To, DateTimeOffset LastActive);