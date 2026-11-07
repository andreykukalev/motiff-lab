using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// The assembly kept with a piece (decision record 0024, section 7; project file version 15): the journal the player's
/// notes are rebuilt by, and, once «Сделать нотами» was pressed, the notes the piece plays since then and the notes
/// before the first apply («Ноты до сборки»). Closing the piece applies nothing.
/// </summary>
/// <param name="Steps">The journal, in order.</param>
/// <param name="Next">The number the next step takes.</param>
public sealed record PieceAssembly(IReadOnlyList<AssemblyStep> Steps, int Next)
{
    public static PieceAssembly Empty { get; } = new([], 1);

    /// <summary>The piece's notes since «Сделать нотами»; null until it was pressed.</summary>
    public SourceMusic? Applied { get; init; }

    /// <summary>The notes before the first apply, the base the journal builds from from then on; null until applied.</summary>
    public SourceMusic? Before { get; init; }

    /// <summary>When «Сделать нотами» was last pressed.</summary>
    public DateTimeOffset? AppliedAt { get; init; }

    public bool IsEmpty => Steps.Count == 0 && Applied is null && Before is null;

    /// <summary>
    /// The piece as the applied notes have it: their notes, bars, keys and tempo. The score is engraved from the notes
    /// from then on, not shown as the file wrote it.
    /// </summary>
    public ImportedPiece Over(ImportedPiece piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if (Applied is not { } applied)
        {
            return piece;
        }

        return piece with
        {
            Notes = applied.Notes,
            Meter = applied.Meter,
            Keys = applied.Keys,
            End = applied.End,
            ScoreXml = null,
            AssembledTempos = applied.Tempos,
        };
    }

    /// <summary>
    /// The edits of the player's notes made since the last apply that still fit <paramref name="notes"/>: kept on top of
    /// the newly applied notes, the rest are part of them or lost their note.
    /// </summary>
    public static NoteEditLog Fitting(NoteEditLog edits, IReadOnlyList<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(notes);
        Dictionary<string, NoteValues> current = notes.ToDictionary(note => note.Id, NoteValues.Of, StringComparer.Ordinal);
        List<NoteEdit> kept = [];
        foreach (NoteEdit edit in edits.Edits)
        {
            if (edit.IsAddition ? !current.ContainsKey(edit.NoteId) : current.TryGetValue(edit.NoteId, out NoteValues values) && values == edit.Before)
            {
                kept.Add(edit);
                if (edit.After is { } after)
                {
                    current[edit.NoteId] = after;
                }
                else
                {
                    current.Remove(edit.NoteId);
                }
            }
        }

        return new NoteEditLog(kept);
    }
}