using MotiffLab.MusicCore.Events;

namespace MotiffLab.MusicCore.Projects;

/// <summary>The assembly of the player's own version from the piece's sources (decision record 0024).</summary>
public sealed partial record Project
{
    /// <summary>The assembly of the player's own version from the piece's sources (decision record 0024).</summary>
    public PieceAssembly Assembly { get; init; } = PieceAssembly.Empty;

    /// <summary>The project with the assembly's journal as it now stands; nothing in the piece's notes changes.</summary>
    internal Project WithJournal(IEnumerable<global::MotiffLab.MusicCore.Assembly.AssemblyStep> steps, int next) =>
        this with { Assembly = Assembly with { Steps = [.. steps], Next = next } };

    /// <summary>
    /// «Сделать нотами», the one rule that changes the piece's notes (decision record 0024, section 7): the notes the
    /// journal builds become the piece's notes. The notes before the first apply are kept as «Ноты до сборки» and stay the
    /// base the journal builds from; corrections made since the last apply stay where they still fit.
    /// </summary>
    internal Project ApplyAssembly(global::MotiffLab.MusicCore.Assembly.SourceMusic notes, global::MotiffLab.MusicCore.Assembly.SourceMusic before, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(before);
        bool first = Assembly.Before is null;
        return this with
        {
            Assembly = Assembly with { Applied = notes, Before = Assembly.Before ?? before, AppliedAt = at },
            Edits = first ? NoteEditLog.Empty : PieceAssembly.Fitting(Edits, notes.Notes),
        };
    }
}