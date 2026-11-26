using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade.Sessions;

/// <summary>
/// The corrections of one bar, as corrections and corrections.* answer them: the bar's notes as they sound, the imported
/// notes taken out anywhere (to put back), how many corrections the log holds, the note editor's draft (whether it is open,
/// whether a step can be undone, whether it changed anything) and the layer, null when the corrections are laid strictly.
/// </summary>
internal sealed record BarCorrections(Bar Bar, IReadOnlyList<CorrectionNote> Notes, IReadOnlyList<Note> Removed, int Count, bool Editing, bool CanUndo, bool Changed, LaidCorrections? Layer);

/// <summary>A note of a bar to correct: whether the player corrected it, whether it was taken from a version, and whether it can start earlier or be shorter by a step.</summary>
internal sealed record CorrectionNote(Note Note, bool Edited, bool Taken, bool CanEarlier, bool CanShorter);

/// <summary>
/// How the corrections are laid over the notes when not strictly: left out (<paramref name="Off"/>) or one by one; whether
/// the file says they are off; how many were laid on their notes, which were moved onto another note and which no longer
/// fit; and what opening left out.
/// </summary>
internal sealed record LaidCorrections(bool Off, bool EditsOff, int Applied, IReadOnlyList<MisfitCorrection> Moved, IReadOnlyList<MisfitCorrection> Unfit, IReadOnlyList<string> Hidden);

/// <summary>
/// A correction of <paramref name="NoteId"/> in <paramref name="Bar"/> that did not fit its note, and why: laid on the note
/// <paramref name="To"/> instead, or, with none, left out.
/// </summary>
internal sealed record MisfitCorrection(string NoteId, int Bar, EditMisfit Reason, string? To = null);

/// <summary>One staff of another engine's reading of a bar (corrections.variant): its track (the part from 0), its staff, and its notes in the piece's ticks.</summary>
internal sealed record VariantStaff(int Track, int Staff, IReadOnlyList<NoteValues> Notes);