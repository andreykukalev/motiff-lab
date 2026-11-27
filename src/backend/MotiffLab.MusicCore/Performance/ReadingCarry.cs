using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Performance;

/// <summary>What became of one of the player's corrections when the same pages were read again («Как напечатано», step 2).</summary>
internal enum CarryOutcome
{
    /// <summary>The new reading makes the same mistake: the correction is carried over to it.</summary>
    Carried,

    /// <summary>The new reading already gives what the player corrected the note to: nothing to carry.</summary>
    ByReading,

    /// <summary>The new reading gives neither: the correction stays on the earlier reading, its bar flagged in Check, and the player decides.</summary>
    NotCarried,
}

/// <summary>One correction of the earlier reading and what became of it on the new one.</summary>
/// <param name="NoteId">The note it names on the earlier reading.</param>
/// <param name="Bar">The bar of its place in the new reading, as the workspace shows it (its music matched, so a bar the new reading has more or less before it moves it).</param>
/// <param name="Corrected">The values the player gave the note; null when the player removed it.</param>
/// <param name="Read">What the new reading gives at its place: the note carried to, or the nearest in pitch that starts there; null for none.</param>
internal sealed record CarriedCorrection(string NoteId, int Bar, CarryOutcome Outcome, NoteValues? Corrected, NoteValues? Read);

/// <summary>
/// The player's corrections of an earlier reading of a score carried over to a new reading of the same pages («Как
/// напечатано», step 2; concept 3.3, Andrei's answer 5 of 2026-11-25). The two readings are matched by the notes that
/// start together, aligned with gaps as a performance is (<see cref="ReadingMatch"/>, ticks on both sides), which
/// gives each onset of the earlier reading its place in the new one; a note of the new reading is the same as one of the
/// earlier when it has its pitch and length at that place. Each correction, as it lies on the earlier reading's notes,
/// is then one of three (<see cref="CarryOutcome"/>): a changed note the new reading still reads as the player found it
/// is changed there too, one it reads as the player corrected it is left, any other stays on the earlier reading; a
/// removed note is removed again where the new reading has it, and left where it has not; an added note is added where
/// the new reading has no note of its pitch starting at its place. Every correction carried is in one group, with the
/// origin it had. A bar the player confirmed («Выглядит верно») is confirmed in the new reading when its notes, as
/// corrected on both, are the same. Pure: the same readings, corrections and group give the same carry.
/// </summary>
/// <param name="Edits">The corrections carried, laid on the new reading's notes.</param>
/// <param name="Corrections">Each correction of the earlier reading, in the order the player made them.</param>
/// <param name="ConfirmedBars">The bars confirmed in the new reading.</param>
internal sealed record ReadingCarry(NoteEditLog Edits, IReadOnlyList<CarriedCorrection> Corrections, IReadOnlyList<int> ConfirmedBars)
{
    public int Count(CarryOutcome outcome) => Corrections.Count(correction => correction.Outcome == outcome);

    /// <summary>The bars of the new reading with a correction not carried, in order, each once.</summary>
    public IReadOnlyList<int> NotCarriedBars => [.. Corrections.Where(static correction => correction.Outcome == CarryOutcome.NotCarried).Select(static correction => correction.Bar).Distinct().Order()];

    /// <summary>
    /// What the carry did, in a line of the toast: «перенесено: 3 исправления · уже верно в новом чтении: 1 · не перенесено:
    /// 2 (такты 3, 7) · «Верно» перенесено: 1 такт», each part only when it counts something; null when nothing does.
    /// </summary>
    public string? Text(Words words)
    {
        int carried = Count(CarryOutcome.Carried), byReading = Count(CarryOutcome.ByReading), notCarried = Count(CarryOutcome.NotCarried);
        IReadOnlyList<int> bars = NotCarriedBars;
        string where = string.Join(", ", bars);
        List<string> parts = [];
        if (carried > 0)
        {
            string corrections = words.Count(carried, "correction", "corrections", "исправление", "исправления", "исправлений");
            parts.Add(words.T($"{corrections} carried over", $"перенесено: {corrections}"));
        }

        if (byReading > 0)
        {
            parts.Add(words.T($"already right in the new reading: {byReading}", $"уже верно в новом чтении: {byReading}"));
        }

        if (notCarried > 0)
        {
            parts.Add(words.T($"not carried, in Check: {notCarried} ({words.Bars(where, bars.Count == 1)})", $"не перенесено, в «Проверить»: {notCarried} ({words.Bars(where, bars.Count == 1)})"));
        }

        if (ConfirmedBars.Count > 0)
        {
            string confirmed = words.Count(ConfirmedBars.Count, "bar", "bars", "такт", "такта", "тактов");
            parts.Add(words.T($"«Looks right» carried over: {confirmed}", $"«Верно» перенесено: {confirmed}"));
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>
    /// The corrections <paramref name="edits"/> of the reading <paramref name="before"/> and its bars <paramref name="confirmed"/>
    /// carried over to the new reading <paramref name="after"/>, the corrections carried in <paramref name="group"/>.
    /// </summary>
    public static ReadingCarry Of(ImportedPiece before, NoteEditLog edits, IReadOnlyCollection<int> confirmed, ImportedPiece after, string group)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(confirmed);
        ArgumentNullException.ThrowIfNull(after);
        var carry = new Carrying(before, after);
        EditsApplied was = edits.ApplyEach(before.Notes);
        List<(int Index, CarriedCorrection Correction, Func<NoteEditLog, NoteEditLog>? Edit)> made = [];
        foreach ((int index, string id) in TouchedIds(was, edits))
        {
            if (carry.Correction(id, was, edits.Edits[index].Origin) is { } correction)
            {
                made.Add((index, correction.Correction, correction.Edit));
            }
        }

        made.AddRange(was.Unfit.Select(unfit => (unfit.Index, new CarriedCorrection(unfit.Edit.NoteId, carry.BarAfter(unfit.Edit.Before ?? unfit.Edit.After), CarryOutcome.NotCarried, unfit.Edit.After, null), (Func<NoteEditLog, NoteEditLog>?)null)));
        made.Sort(static (a, b) => a.Index.CompareTo(b.Index));
        NoteEditLog carried = made.Select(static each => each.Edit).OfType<Func<NoteEditLog, NoteEditLog>>().Aggregate(NoteEditLog.Empty, static (log, edit) => edit(log));
        carried = new NoteEditLog(carried.Edits.Select(edit => edit with { Group = group }));
        return new ReadingCarry(carried, [.. made.Select(static each => each.Correction)], carry.Confirmed(confirmed, was.Notes, carried.ApplyEach(after.Notes).Notes));
    }

    /// <summary>The notes the corrections change, remove or add on the earlier reading, each with the last correction laid on it, in the order they were first corrected.</summary>
    private static IEnumerable<(int Last, string Id)> TouchedIds(EditsApplied was, NoteEditLog edits)
    {
        IEnumerable<(int Index, string Id)> laid = was.Applied.Select(index => (index, edits.Edits[index].NoteId)).Concat(was.Moved.Select(static moved => (moved.Index, moved.NoteId)));
        return laid.GroupBy(static each => each.Id, StringComparer.Ordinal)
            .OrderBy(static each => each.Min(laidOn => laidOn.Index))
            .Select(static each => (each.Max(laidOn => laidOn.Index), each.Key));
    }

    /// <summary>The two readings matched (<see cref="ReadingMatch"/>) and the notes of the earlier paired with the new one's.</summary>
    private sealed class Carrying
    {
        private readonly ImportedPiece _before;
        private readonly ImportedPiece _after;
        private readonly ReadingMatch _match;
        private readonly Dictionary<string, Note> _imported;
        private readonly Dictionary<string, Note> _pairOf = new(StringComparer.Ordinal);
        private readonly HashSet<string> _used = new(StringComparer.Ordinal);

        public Carrying(ImportedPiece before, ImportedPiece after)
        {
            _before = before;
            _after = after;
            _match = new ReadingMatch(before, after);
            _imported = before.Notes.ToDictionary(static note => note.Id, StringComparer.Ordinal);
            foreach (Note note in before.Notes.OrderBy(static note => note.Span.Onset).ThenBy(static note => note.Id, StringComparer.Ordinal))
            {
                if (Free(Placed(NoteValues.Of(note)), note.Track) is { } same)
                {
                    _pairOf[note.Id] = same;
                    _used.Add(same.Id);
                }
            }
        }

        /// <summary>The bar of the new reading where values of the earlier lie.</summary>
        public int BarAfter(NoteValues? values) => _after.Meter.BarAt(values is { } found ? Placed(found).Span.Onset : Tick.Zero).Number;

        /// <summary>The correction of the note <paramref name="id"/> as it lies on the earlier reading, and the edit that carries it; null when it changes nothing.</summary>
        public (CarriedCorrection Correction, Func<NoteEditLog, NoteEditLog>? Edit)? Correction(string id, EditsApplied was, string? origin)
        {
            Note? found = _imported.GetValueOrDefault(id);
            Note? now = was.Notes.FirstOrDefault(note => note.Id == id);
            NoteValues? before = found is null ? null : NoteValues.Of(found), after = now is null ? null : NoteValues.Of(now);
            if (before == after)
            {
                return null;
            }

            int bar = BarAfter(before ?? after);
            if (before is not { } replaced)
            {
                return Added(id, bar, after!.Value, now!, origin);
            }

            Note? same = _pairOf.GetValueOrDefault(id);
            if (after is not { } corrected)
            {
                return same is null
                    ? (new CarriedCorrection(id, bar, CarryOutcome.ByReading, null, null), null)
                    : (new CarriedCorrection(id, bar, CarryOutcome.Carried, null, NoteValues.Of(same)), log => new NoteEditLog([.. log.Edits, new NoteEdit(same.Id, NoteValues.Of(same), null) { Origin = origin }]));
            }

            NoteValues placed = Placed(corrected);
            if (same is not null)
            {
                NoteValues to = placed with { Velocity = corrected.Velocity == replaced.Velocity ? same.Velocity : corrected.Velocity };
                return (new CarriedCorrection(id, bar, CarryOutcome.Carried, corrected, NoteValues.Of(same)), log => new NoteEditLog([.. log.Edits, new NoteEdit(same.Id, NoteValues.Of(same), to) { Origin = origin }]));
            }

            if (Free(placed, found!.Track) is { } read)
            {
                _used.Add(read.Id);
                return (new CarriedCorrection(id, bar, CarryOutcome.ByReading, corrected, NoteValues.Of(read)), null);
            }

            return (new CarriedCorrection(id, bar, CarryOutcome.NotCarried, corrected, Nearest(Placed(replaced)) is { } near ? NoteValues.Of(near) : null), null);
        }

        /// <summary>
        /// The bars of the new reading that take the confirmation of the bars <paramref name="confirmed"/> of the earlier: the
        /// bar where an earlier bar's music lies (moved as its first onset matched, else as the onsets before it), starting
        /// there and as long, with the same notes, as corrected on both, at the same places in the bar.
        /// </summary>
        public IReadOnlyList<int> Confirmed(IReadOnlyCollection<int> confirmed, IReadOnlyList<Note> was, IReadOnlyList<Note> now) =>
            [.. confirmed.Distinct().Order()
                .Where(number => number >= _before.Meter.FirstBarNumber)
                .Select(number => _before.Meter.GetBar(number))
                .Where(earlier => earlier.Span.Onset < _before.End)
                .Select(earlier => (Earlier: earlier, Later: _match.Moved(earlier)))
                .Where(bar => bar.Later is { } later && later.Span.Onset < _after.End && later.Span.Duration == bar.Earlier.Span.Duration
                    && InBar(was, bar.Earlier).SequenceEqual(InBar(now, later)))
                .Select(static bar => bar.Later!.Value.Number)
                .Distinct()];

        private static IEnumerable<(int Pitch, long At, long Duration)> InBar(IReadOnlyList<Note> notes, Bar bar) => notes
            .Where(note => bar.Span.Contains(note.Span.Onset))
            .Select(note => (note.Pitch.MidiNumber, note.Span.Onset.Value - bar.Span.Onset.Value, note.Span.Duration))
            .Order();

        private (CarriedCorrection, Func<NoteEditLog, NoteEditLog>?) Added(string id, int bar, NoteValues values, Note added, string? origin)
        {
            NoteValues placed = Placed(values);
            Note? there = _after.Notes.FirstOrDefault(note => !_used.Contains(note.Id) && note.Pitch == placed.Pitch && note.Span.Onset == placed.Span.Onset);
            if (there is not null)
            {
                _used.Add(there.Id);
                return (new CarriedCorrection(id, bar, CarryOutcome.ByReading, values, NoteValues.Of(there)), null);
            }

            NoteEditLog Carry(NoteEditLog log)
            {
                NoteEditLog with = log.Add(_after.Notes, placed, added.Track, added.Hand?.Value, out _);
                return new NoteEditLog([.. with.Edits.SkipLast(1), with.Edits[^1] with { Origin = origin }]);
            }

            return (new CarriedCorrection(id, bar, CarryOutcome.Carried, values, null), Carry);
        }

        /// <summary>Values of the earlier reading at their place in the new one (<see cref="ReadingMatch.Shift"/>).</summary>
        private NoteValues Placed(NoteValues values) =>
            values with { Span = new TickSpan(new Tick(Math.Max(0, values.Span.Onset.Value + _match.Shift(values.Span.Onset.Value))), values.Span.Duration) };

        /// <summary>The note of the new reading no earlier note is paired with that has these pitch, onset and length; the same track first, then the lower id.</summary>
        private Note? Free(NoteValues values, int track) => _after.Notes
            .Where(note => !_used.Contains(note.Id) && note.Pitch == values.Pitch && note.Span == values.Span)
            .OrderBy(note => note.Track == track ? 0 : 1)
            .ThenBy(static note => note.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        /// <summary>The note of the new reading no earlier note is paired with that starts at the place, the nearest in pitch.</summary>
        private Note? Nearest(NoteValues values) => _after.Notes
            .Where(note => !_used.Contains(note.Id) && note.Span.Onset == values.Span.Onset)
            .OrderBy(note => Math.Abs(note.Pitch.MidiNumber - values.Pitch.MidiNumber))
            .ThenBy(static note => note.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}