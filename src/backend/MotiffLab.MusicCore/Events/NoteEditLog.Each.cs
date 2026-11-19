using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Events;

public sealed partial record NoteEditLog
{
    /// <summary>How far from the onset a correction found a note may be to be laid on another of the same pitch: a thirty-second.</summary>
    public const long NearOnset = CanonicalTime.TicksPerQuarter / 8;

    /// <summary>
    /// The corrections laid over the imported notes one by one, in order (version 0.5, «Мои правки»), even when the notes
    /// changed under them. A correction whose note is as it found it is applied. Else it is laid on the nearest note of the
    /// same pitch no earlier correction touched, with an onset within <see cref="NearOnset"/> of the one it found (the
    /// lower id on a tie), its new values moved by the same distance: moved. Else it does not fit, and why. A later
    /// correction of the same note follows the earlier one to the note it was laid on. The log itself never changes.
    /// </summary>
    public EditsApplied ApplyEach(IReadOnlyList<Note> imported)
    {
        ArgumentNullException.ThrowIfNull(imported);
        if (IsEmpty)
        {
            return EditsApplied.None(imported);
        }

        var layer = new Layering(imported);
        for (int index = 0; index < Edits.Count; index++)
        {
            layer.Lay(index, Edits[index]);
        }

        return layer.Result();
    }

    /// <summary>The log without the corrections of <paramref name="noteIds"/> that do not fit the notes; the others stay.</summary>
    public NoteEditLog DropUnfit(IReadOnlyList<Note> imported, IEnumerable<string> noteIds)
    {
        ArgumentNullException.ThrowIfNull(noteIds);
        HashSet<string> named = new(noteIds, StringComparer.Ordinal);
        HashSet<int> dropped = [.. ApplyEach(imported).Unfit.Where(edit => named.Contains(edit.Edit.NoteId)).Select(static edit => edit.Index)];
        return dropped.Count == 0 ? this : new(Edits.Where((_, index) => !dropped.Contains(index)));
    }

    /// <summary>The refusal of a strict open: the first correction that was not laid on the note it names.</summary>
    private static InvalidOperationException Refusal(EditsApplied each)
    {
        (NoteEdit edit, EditMisfit reason) = each.Moved.Select(static moved => (moved.Index, moved.Edit, moved.Reason))
            .Concat(each.Unfit.Select(static unfit => (unfit.Index, unfit.Edit, unfit.Reason)))
            .OrderBy(static found => found.Index)
            .Select(static found => (found.Edit, found.Reason))
            .First();
        return new InvalidOperationException(reason switch
        {
            EditMisfit.IdTaken => $"Note {edit.NoteId} is already in the piece.",
            EditMisfit.Missing => $"Note {edit.NoteId} is not in the piece.",
            _ => $"Note {edit.NoteId} is not as the edit found it.",
        });
    }

    /// <summary>The notes as the corrections are laid on them one by one, and what became of each.</summary>
    private sealed class Layering
    {
        private readonly Dictionary<string, Note?> _current;
        private readonly List<string> _order;
        private readonly HashSet<string> _touched = new(StringComparer.Ordinal);

        /// <summary>For a note the log names whose correction was laid on another: that note and how far it is.</summary>
        private readonly Dictionary<string, (string NoteId, long Shift, EditMisfit Reason)> _moves = new(StringComparer.Ordinal);
        private readonly List<int> _applied = [];
        private readonly List<MovedEdit> _moved = [];
        private readonly List<UnfitEdit> _unfit = [];

        public Layering(IReadOnlyList<Note> imported)
        {
            _current = imported.ToDictionary(static note => note.Id, static note => (Note?)note, StringComparer.Ordinal);
            _order = [.. imported.Select(static note => note.Id)];
        }

        public void Lay(int index, NoteEdit edit)
        {
            if (edit.IsAddition)
            {
                Add(index, edit);
                return;
            }

            NoteValues before = edit.Before!.Value;
            (string target, long shift, EditMisfit reason) = _moves.TryGetValue(edit.NoteId, out var move) ? move : (edit.NoteId, 0L, EditMisfit.Changed);
            if (Find(target) is { } note && NoteValues.Of(note) == Shifted(before, shift))
            {
                Set(note, edit.After, shift);
                if (target == edit.NoteId)
                {
                    _applied.Add(index);
                }
                else
                {
                    _moved.Add(new MovedEdit(index, edit, target, shift, reason));
                }

                return;
            }

            EditMisfit misfit = Find(edit.NoteId) is null ? EditMisfit.Missing : EditMisfit.Changed;
            if (Nearest(before) is { } near)
            {
                long distance = near.Span.Onset.Value - before.Span.Onset.Value;
                Set(near, edit.After, distance);
                _moves[edit.NoteId] = (near.Id, distance, misfit);
                _moved.Add(new MovedEdit(index, edit, near.Id, distance, misfit));
                return;
            }

            _unfit.Add(new UnfitEdit(index, edit, misfit));
        }

        public EditsApplied Result() =>
            new([.. _order.Select(id => _current[id]).OfType<Note>().OrderBy(static note => note.Span.Onset)], _applied, _moved, _unfit);

        private void Add(int index, NoteEdit edit)
        {
            if (_current.ContainsKey(edit.NoteId) || edit.After is not { } values)
            {
                _unfit.Add(new UnfitEdit(index, edit, EditMisfit.IdTaken));
                return;
            }

            _current[edit.NoteId] = new Note
            {
                Id = edit.NoteId,
                Pitch = values.Pitch,
                Span = values.Span,
                Velocity = values.Velocity,
                Track = edit.Track,
                Hand = edit.Hand is { } hand ? new Attributed<Hand>(hand, Attribution.UserEdited) : null,
                Attribution = Attribution.UserEdited,
            };
            _order.Add(edit.NoteId);
            _touched.Add(edit.NoteId);
            _applied.Add(index);
        }

        private Note? Find(string id) => _current.TryGetValue(id, out Note? note) ? note : null;

        /// <summary>The nearest note of the pitch no correction touched yet, within a thirty-second of the onset; the lower id on a tie.</summary>
        private Note? Nearest(NoteValues before) => _current.Values
            .OfType<Note>()
            .Where(note => note.Pitch == before.Pitch && !_touched.Contains(note.Id) && Math.Abs(note.Span.Onset.Value - before.Span.Onset.Value) <= NearOnset)
            .OrderBy(note => Math.Abs(note.Span.Onset.Value - before.Span.Onset.Value))
            .ThenBy(static note => note.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        private void Set(Note note, NoteValues? after, long shift)
        {
            _touched.Add(note.Id);
            _current[note.Id] = after is { } values
                ? note with { Pitch = values.Pitch, Span = Shifted(values, shift).Span, Velocity = values.Velocity, Attribution = Attribution.UserEdited }
                : null;
        }

        private static NoteValues Shifted(NoteValues values, long shift) => shift == 0
            ? values
            : values with { Span = new TickSpan(new Tick(Math.Max(0, values.Span.Onset.Value + shift)), values.Span.Duration) };
    }
}