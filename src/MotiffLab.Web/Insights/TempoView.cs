using MotiffLab.MusicCore.Analysis;

namespace MotiffLab.Web.Insights;

/// <summary>What the tempo card shows: music-core's profile of the whole piece and of each piece of its pieces layer.</summary>
public sealed record TempoView(TempoProfile Whole, IReadOnlyList<TempoPieceRow> Pieces);

/// <summary>One piece of the pieces layer with its own profile and its bars.</summary>
public sealed record TempoPieceRow(string Title, TempoProfile Profile, int FirstBar, int LastBar);