using MotiffLab.MusicCore.Events;

namespace MotiffLab.Web.Workspace;

/// <summary>
/// What «Мои правки» shows (version 0.5): whether the corrections are on, how many the piece has, how many were laid on
/// a near note of the same pitch, those that no longer fit, and how many motifs, variations and ideas are hidden.
/// </summary>
public sealed record EditsLayerView(bool On, int Count, int Moved, IReadOnlyList<EditsLayerRow> Unfit, int Hidden);

/// <summary>A correction that no longer fits: the note it names, its bar, the note by name and why.</summary>
public sealed record EditsLayerRow(string NoteId, int Bar, string Note, EditMisfit Reason);