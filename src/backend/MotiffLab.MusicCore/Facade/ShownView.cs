namespace MotiffLab.MusicCore.Facade;

/// <summary>What a piece open only to be shown shows: the number of the piece it was opened from, and the Notes' or draft's id.</summary>
internal readonly record struct ShownView(int Piece, string Id, bool Draft);