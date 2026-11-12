namespace MotiffLab.Web.Workspace;

/// <summary>
/// A collection this browser split into pieces (the «Пьесы в файле» experiment, version 0.4), as
/// <c>Scripts/storage/piece-parts.ts</c> keeps it beside the projects: the collection's title, the playlist the pieces
/// joined and the pieces, so the collection can say «Разделено на N пьес» and each piece where it came from. It waits
/// there for the format step, which keeps it in the projects.
/// </summary>
public sealed record SplitLink(string Title, string Playlist, IReadOnlyList<PartLink> Parts);

/// <summary>
/// One piece made from a collection: its project, its title, the collection's bars it took and, for a recording, its
/// stretch of the collection's sound in milliseconds, the part's reference to it (no sound is copied).
/// </summary>
public sealed record PartLink(string Id, string Title, int? First, int? Last, long? From, long? To);

/// <summary>Where a piece came from: the collection's project, its title and playlist, and the piece's place in it.</summary>
public sealed record PartOrigin(string Collection, string Title, string Playlist, PartLink Part);