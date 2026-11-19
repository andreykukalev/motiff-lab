namespace MotiffLab.MusicCore.Events;

/// <summary>
/// How the player's corrections are laid over the imported notes when a piece opens (version 0.5, «Мои правки: вкл /
/// выкл»). The app chooses it; music-core knows no switch.
/// </summary>
public enum EditsLayer
{
    /// <summary>Every correction must fit, or the piece does not open: the behaviour before version 0.5, and the default.</summary>
    Strict,

    /// <summary>The corrections one by one (<see cref="NoteEditLog.ApplyEach"/>): one that no longer fits is moved or set aside, and the piece opens.</summary>
    Lenient,

    /// <summary>No correction is laid over the notes: they are as imported. The log stays as it is.</summary>
    Off,
}

/// <summary>Why a correction could not be laid on its note.</summary>
public enum EditMisfit
{
    /// <summary>The note it names is not in the piece (any more), and no note of the same pitch is near.</summary>
    Missing,

    /// <summary>The note it names is there, but not as the correction found it, and no note of the same pitch is near.</summary>
    Changed,

    /// <summary>A note the player added has an id the piece already has.</summary>
    IdTaken,
}

/// <summary>A correction laid on another note than the one it names: the nearest of the same pitch, its timing moved with it.</summary>
/// <param name="Index">Where the correction is in the log, from 0.</param>
/// <param name="NoteId">The note it was laid on.</param>
/// <param name="Shift">How far, in ticks, that note's onset is from the one the correction found (its new values moved as far).</param>
/// <param name="Reason">Why it could not be laid on the note it names.</param>
public sealed record MovedEdit(int Index, NoteEdit Edit, string NoteId, long Shift, EditMisfit Reason);

/// <summary>A correction that no longer fits the notes: it stays in the log, unapplied, until the player takes it out.</summary>
/// <param name="Index">Where the correction is in the log, from 0.</param>
public sealed record UnfitEdit(int Index, NoteEdit Edit, EditMisfit Reason);

/// <summary>
/// The corrections laid over the imported notes one by one (<see cref="NoteEditLog.ApplyEach"/>): the notes, and for each
/// correction whether it was applied, moved to a near note of the same pitch, or did not fit.
/// </summary>
/// <param name="Notes">The imported notes with every correction that fits applied, in onset order.</param>
/// <param name="Applied">The log positions of the corrections laid on the note they name.</param>
public sealed record EditsApplied(IReadOnlyList<Note> Notes, IReadOnlyList<int> Applied, IReadOnlyList<MovedEdit> Moved, IReadOnlyList<UnfitEdit> Unfit)
{
    /// <summary>The notes as imported: an empty log, every correction applied.</summary>
    public static EditsApplied None(IReadOnlyList<Note> imported) => new(imported, [], [], []);

    /// <summary>True when every correction was laid on the note it names, as a strict open requires.</summary>
    public bool AllFit => Moved.Count == 0 && Unfit.Count == 0;

    /// <summary>The notes some correction now changes, removes or adds: those named, and those moved corrections were laid on.</summary>
    public IReadOnlySet<string> EditedIds(NoteEditLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        HashSet<string> ids = new(Applied.Select(index => log.Edits[index].NoteId), StringComparer.Ordinal);
        ids.UnionWith(Moved.Select(static edit => edit.NoteId));
        return ids;
    }

    /// <summary>The notes the log names whose corrections were laid on <paramref name="noteId"/>: it, and those moved onto it.</summary>
    public IReadOnlySet<string> NamedFor(string noteId)
    {
        ArgumentNullException.ThrowIfNull(noteId);
        HashSet<string> named = new(StringComparer.Ordinal) { noteId };
        named.UnionWith(Moved.Where(edit => edit.NoteId == noteId).Select(static edit => edit.Edit.NoteId));
        return named;
    }
}