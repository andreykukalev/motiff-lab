using System.Globalization;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Notation;

/// <summary>
/// Which bars of a staff are written as the score prints them («Как напечатано», step 1, research 2026-11-20 section 2):
/// for notes with a source record (<see cref="PrintedNotation"/>), each note in its own staff and voice, with the score's
/// rests and spaces, written values with dots and ties, tuplets, stems and beams.
/// </summary>
/// <remarks>
/// A bar of a staff keeps the generator's writing when the record does not fit it: a voice whose notes or rests overlap,
/// a voice whose values (with the time it skips as written) do not add up to the bar, a written value that is not the note's length, a tie or voice that
/// crosses to another staff, a chord whose notes are written differently. In a bar that fits, a voice with a note the player
/// corrected, added or removed is written by the generator's rules inside that voice; a note added goes into the first
/// voice with room for it, or into a voice of its own.
/// </remarks>
internal static class PrintedBars
{
    /// <summary>
    /// The staff's bars written as printed among the bars written, by bar index (<see cref="StaffPlan.Printed"/>), and the
    /// notes written as printed in all of them (<see cref="StaffPlan.AsPrinted"/>).
    /// </summary>
    public static (Dictionary<int, PrintedCell> Cells, HashSet<string> AsPrinted) Plan(StaffPlan staff, WrittenBars written, PrintedNotation printed)
    {
        if (printed.SourceStaff(staff) is not int sourceStaff)
        {
            return ([], []);
        }

        var cells = new Dictionary<int, PrintedCell>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var generated = new HashSet<string>(StringComparer.Ordinal);
        for (int index = written.FirstIndex; index <= written.LastIndex; index++)
        {
            Bar bar = written.Bars[index];
            ShownNote[] inBar = [.. staff.Notes.Where(note => !note.IsGrace && note.Note.Span.Onset.Value < bar.Span.End.Value && note.Note.Span.End.Value > bar.Span.Onset.Value)];
            PrintedCell? cell = Cell(staff, sourceStaff, bar, inBar, printed);
            foreach (ShownNote note in inBar)
            {
                seen.Add(note.Note.Id);
                if (cell is null || !cell.AsPrinted.Contains(note.Note.Id))
                {
                    generated.Add(note.Note.Id);
                }
            }

            if (cell is not null)
            {
                cells[index] = cell;
            }
        }

        seen.ExceptWith(generated);
        return (cells, seen);
    }

    /// <summary>The staff in the bar as printed; null where the record does not fit and the generator writes the bar.</summary>
    private static PrintedCell? Cell(StaffPlan staff, int sourceStaff, Bar bar, ShownNote[] inBar, PrintedNotation printed)
    {
        long start = bar.Span.Onset.Value;
        long stop = bar.Span.End.Value;
        var voices = new SortedDictionary<string, VoicePlan>(VoiceOrder.Instance);
        VoicePlan Voice(string key) => voices.TryGetValue(key, out VoicePlan? voice) ? voice : voices[key] = new VoicePlan();

        var added = new List<ShownNote>();
        foreach (ShownNote note in inBar)
        {
            if (!printed.TryGet(note.Note.Id, out Note original, out MusicXmlNote record, out MusicXmlWrittenNote[] written) || record.Track != staff.Track)
            {
                added.Add(note);
            }
            else if (note.Note.Span != original.Span || record.Staff != sourceStaff || written.Length == 0)
            {
                // Corrected: it keeps its voice, written there by the generator's rules.
                Voice(record.Voice).Generated.Add(note);
            }
            else if (!AddPieces(Voice, note, written, sourceStaff, start, stop))
            {
                return null;
            }
        }

        // A note of the score that is no longer here (removed or moved) leaves its voice to the generator.
        HashSet<string> present = [.. inBar.Select(note => note.Note.Id)];
        foreach ((_, MusicXmlWrittenNote piece) in printed.Written(staff.Track, sourceStaff, start, stop).Where(item => !present.Contains(item.Id)))
        {
            Voice(piece.Voice).Changed = true;
        }

        if (!AddRests(Voice, printed, staff.Track, sourceStaff, start, stop))
        {
            return null;
        }

        foreach (ShownNote note in added.OrderBy(note => note.Note.Span.Onset))
        {
            VoicePlan room = voices.Values.FirstOrDefault(voice => !voice.Sounds(note)) ?? Voice("+" + (voices.Count + 1).ToString(CultureInfo.InvariantCulture));
            room.Generated.Add(note);
        }

        return Written(voices.Values, bar);
    }

    /// <summary>Adds a note's written notes in the bar to their voice; false when one crosses the bar, the staff or the voice.</summary>
    private static bool AddPieces(Func<string, VoicePlan> voice, ShownNote note, MusicXmlWrittenNote[] written, int sourceStaff, long start, long stop)
    {
        int[] inBar = [.. Enumerable.Range(0, written.Length).Where(index => written[index].Span.Onset.Value < stop && written[index].Span.End.Value > start)];
        if (inBar.Length == 0 || inBar.Any(index => written[index] is var piece
            && (piece.Staff != sourceStaff || piece.Voice != written[inBar[0]].Voice || piece.Span.Onset.Value < start || piece.Span.End.Value > stop)))
        {
            return false;
        }

        VoicePlan plan = voice(written[inBar[0]].Voice);
        plan.Notes.Add(note);
        plan.Pieces.AddRange(inBar.Select(index => (note, written[index], index > 0, index < written.Length - 1)));
        return true;
    }

    /// <summary>Adds the staff's rests and spaces in the bar to their voices; false when one crosses the bar.</summary>
    private static bool AddRests(Func<string, VoicePlan> voice, PrintedNotation printed, int track, int sourceStaff, long start, long stop)
    {
        foreach (MusicXmlRest rest in printed.Rests(track, sourceStaff, start, stop))
        {
            if (rest.Span.Onset.Value < start || rest.Span.End.Value > stop)
            {
                return false;
            }

            voice(rest.Voice).Rests.Add(rest);
        }

        foreach (MusicXmlSpace space in printed.Spaces(track, sourceStaff, start, stop))
        {
            if (space.Span.Onset.Value < start || space.Span.End.Value > stop)
            {
                return false;
            }

            voice(space.Voice).Items.Add(new PrintedItem(PrintedKind.Space, space.Span.Onset.Value, space.Span.End.Value));
        }

        return true;
    }

    /// <summary>The voices written, each adding up to the bar; null when one does not fit.</summary>
    private static PrintedCell? Written(IEnumerable<VoicePlan> plans, Bar bar)
    {
        var voices = new List<PrintedVoice>();
        var asPrinted = new HashSet<string>(StringComparer.Ordinal);
        foreach (VoicePlan plan in plans)
        {
            PrintedVoice? voice = plan.Generated.Count > 0 || plan.Changed ? GeneratedVoice(plan, bar) : PrintedVoiceOf(plan, bar);
            if (voice is null)
            {
                return null;
            }

            if (voice.Generated is null)
            {
                asPrinted.UnionWith(plan.Notes.Select(note => note.Note.Id));
            }

            if (voice.Items.Length > 0 || voice.Generated is { Length: > 0 })
            {
                voices.Add(voice);
            }
        }

        return voices.Count == 0 ? null : new PrintedCell([.. voices], asPrinted);
    }

    /// <summary>A voice the generator writes: its notes' chords on the display grid, one after another; null when two overlap.</summary>
    private static PrintedVoice? GeneratedVoice(VoicePlan plan, Bar bar)
    {
        Chord[] chords = [.. plan.Notes.Concat(plan.Generated)
            .Where(note => note.Onset < bar.Span.End.Value && note.End > bar.Span.Onset.Value)
            .GroupBy(note => (note.Onset, note.End))
            .Select(group => new Chord(group.Key.Onset, group.Key.End, Lowest(group)))
            .OrderBy(chord => chord.Onset)];
        return chords.Zip(chords.Skip(1)).Any(pair => pair.Second.Onset < pair.First.End) ? null : new PrintedVoice([], null, chords);
    }

    /// <summary>A voice as the score writes it; null when it does not fit the bar.</summary>
    private static PrintedVoice? PrintedVoiceOf(VoicePlan plan, Bar bar)
    {
        List<PrintedItem>? items = Items(plan, bar);
        if (items is null || AddsUp(items, bar) is not { } complete)
        {
            return null;
        }

        PrintedItem[] grouped = PrintedTuplets.Group(complete, bar);
        return new PrintedVoice(grouped, Beams(grouped), null);
    }

    /// <summary>The voice's chords, rests and spaces in time order, each with its written value; null when one has none.</summary>
    private static List<PrintedItem>? Items(VoicePlan plan, Bar bar)
    {
        var items = new List<PrintedItem>(plan.Items);
        foreach (IGrouping<long, (ShownNote Note, MusicXmlWrittenNote Piece, bool TieIn, bool TieOut)> chord in plan.Pieces.GroupBy(piece => piece.Piece.Span.Onset.Value))
        {
            if (ChordItem([.. chord.OrderBy(piece => piece.Piece.NoteIndex)]) is not { } item)
            {
                return null;
            }

            items.Add(item);
        }

        foreach (MusicXmlRest rest in plan.Rests)
        {
            if (RestItem(rest, bar) is not { } item)
            {
                return null;
            }

            items.Add(item);
        }

        return [.. items.OrderBy(item => item.Start)];
    }

    /// <summary>Notes written together: one length, one written value and the same ties; null otherwise.</summary>
    private static PrintedItem? ChordItem((ShownNote Note, MusicXmlWrittenNote Piece, bool TieIn, bool TieOut)[] pieces)
    {
        (ShownNote _, MusicXmlWrittenNote first, bool tieIn, bool tieOut) = pieces[0];
        bool together = pieces.All(piece => piece.Piece.Span == first.Span && piece.TieIn == tieIn && piece.TieOut == tieOut
            && piece.Piece.Writing.Type == first.Writing.Type && piece.Piece.Writing.Dots == first.Writing.Dots && piece.Piece.Writing.Tuplet == first.Writing.Tuplet);
        if (!together || Value(first.Writing, first.Duration) is not { } value)
        {
            return null;
        }

        return new PrintedItem(PrintedKind.Chord, first.Span.Onset.Value, first.Span.End.Value)
        {
            Dur = value.Dur,
            Dots = value.Dots,
            Notes = Lowest(pieces.Select(piece => piece.Note)),
            TieIn = tieIn,
            TieOut = tieOut,
            Writing = first.Writing with
            {
                StemUp = pieces.Select(piece => piece.Piece.Writing.StemUp).FirstOrDefault(stem => stem is not null),
                Beam = pieces.Select(piece => piece.Piece.Writing.Beam).FirstOrDefault(beam => beam != MusicXmlBeam.None),
            },
        };
    }

    /// <summary>A rest as written: the whole bar's, one with its written value, or one without a sign; null when its value is not its length.</summary>
    private static PrintedItem? RestItem(MusicXmlRest rest, Bar bar)
    {
        long start = rest.Span.Onset.Value;
        long end = rest.Span.End.Value;
        if (rest.IsMeasureRest && start == bar.Span.Onset.Value && end == bar.Span.End.Value && bar.IsComplete && !bar.IsPickup)
        {
            return new PrintedItem(PrintedKind.MeasureRest, start, end);
        }

        if (rest.Writing?.IsHidden == true)
        {
            return Fillable(start, end, bar) ? new PrintedItem(PrintedKind.Space, start, end) : null;
        }

        if (rest.Writing is { } writing && Value(writing, rest.Duration) is { } value)
        {
            return new PrintedItem(PrintedKind.Rest, start, end) { Dur = value.Dur, Dots = value.Dots, Writing = writing };
        }

        return rest.IsMeasureRest && Fillable(start, end, bar) ? new PrintedItem(PrintedKind.FilledRest, start, end) : null;
    }

    /// <summary>
    /// The items when they add up to the bar: one after another from its start to its end, the time the voice skips written
    /// as spaces on the 128th grid; null when items overlap, leave the bar or leave a gap the score does not write.
    /// </summary>
    private static List<PrintedItem>? AddsUp(List<PrintedItem> items, Bar bar)
    {
        long cursor = bar.Span.Onset.Value;
        foreach (PrintedItem item in items)
        {
            if (item.Start != cursor || item.End > bar.Span.End.Value || (item.Kind == PrintedKind.Space && !Fillable(item.Start, item.End, bar)))
            {
                return null;
            }

            cursor = item.End;
        }

        return cursor == bar.Span.End.Value ? items : null;
    }

    /// <summary>The score's beams over the items, from a note that begins one to the note that ends it; null when it writes none.</summary>
    private static (int First, int Last)[]? Beams(PrintedItem[] items)
    {
        if (items.All(item => item.Writing?.Beam is null or MusicXmlBeam.None))
        {
            return null;
        }

        var beams = new List<(int, int)>();
        int open = -1;
        for (int index = 0; index < items.Length; index++)
        {
            MusicXmlBeam beam = items[index].Kind == PrintedKind.Chord ? items[index].Writing!.Beam : MusicXmlBeam.None;
            if (beam == MusicXmlBeam.Begin)
            {
                open = index;
            }
            else if (beam == MusicXmlBeam.End && open >= 0)
            {
                beams.Add((open, index));
                open = -1;
            }
            else if (beam == MusicXmlBeam.None && items[index].Kind == PrintedKind.Chord)
            {
                open = -1;
            }
        }

        return [.. beams];
    }

    /// <summary>
    /// The written value of a note or rest that lasts <paramref name="duration"/>: its type and dots, with its tuplet's ratio,
    /// when they make that length; the value that does, when the file gives no type; null otherwise.
    /// </summary>
    internal static (int Dur, int Dots)? Value(MusicXmlWriting writing, QuarterFraction duration)
    {
        IEnumerable<QuarterFraction> values = writing.Type is { } type ? NoteValue.Named.TryGetValue(type, out QuarterFraction named) ? [named] : [] : NoteValue.Named.Values;
        int[] dots = writing.Type is null ? [0, 1, 2] : [writing.Dots];
        (int actual, int normal) = writing.Tuplet is { } tuplet ? (tuplet.Actual, tuplet.Normal) : (1, 1);
        // MEI writes a value as the parts of a whole note it is: 1, 2, 4 … 128; a breve is not written.
        foreach (QuarterFraction value in values.Where(value => (4 * value.Denominator) % value.Numerator == 0 && value.Numerator <= 4))
        {
            foreach (int dot in dots.Where(dot => dot is >= 0 and <= 4))
            {
                QuarterFraction length = NoteValue.Dotted(value, dot);
                if (new QuarterFraction(length.Numerator * normal, length.Denominator * actual) == duration)
                {
                    return ((int)(4 * value.Denominator / value.Numerator), dot);
                }
            }
        }

        return null;
    }

    /// <summary>Whether the generator can fill the span with note values: both ends on the 128th grid of the bar.</summary>
    private static bool Fillable(long from, long to, Bar bar) => (from - bar.Origin) % 15 == 0 && (to - bar.Origin) % 15 == 0;

    private static ShownNote[] Lowest(IEnumerable<ShownNote> notes) =>
        [.. notes.OrderBy(note => note.Note.Pitch.MidiNumber).ThenBy(note => note.Note.Id, StringComparer.Ordinal)];

    /// <summary>A voice of the score in a bar while it is planned.</summary>
    private sealed class VoicePlan
    {
        /// <summary>The notes as printed.</summary>
        public List<ShownNote> Notes { get; } = [];

        public List<(ShownNote Note, MusicXmlWrittenNote Piece, bool TieIn, bool TieOut)> Pieces { get; } = [];

        public List<MusicXmlRest> Rests { get; } = [];

        /// <summary>The score's spaces.</summary>
        public List<PrintedItem> Items { get; } = [];

        /// <summary>Notes corrected or added, written by the generator's rules.</summary>
        public List<ShownNote> Generated { get; } = [];

        /// <summary>A note of the score is no longer in the voice.</summary>
        public bool Changed { get; set; }

        /// <summary>Whether a note of the voice sounds while <paramref name="note"/> does.</summary>
        public bool Sounds(ShownNote note) => Notes.Concat(Generated).Any(other => other.Note.Span.Onset < note.Note.Span.End && note.Note.Span.Onset < other.Note.Span.End);
    }

    /// <summary>The score's voices in order: numbered ones by number, then the others, then voices for notes added.</summary>
    private sealed class VoiceOrder : IComparer<string>
    {
        public static readonly VoiceOrder Instance = new();

        public int Compare(string? x, string? y)
        {
            static (int, long, string) Key(string? voice) =>
                long.TryParse(voice, NumberStyles.None, CultureInfo.InvariantCulture, out long number) ? (0, number, string.Empty)
                : voice?.StartsWith('+') == true ? (2, 0, voice) : (1, 0, voice ?? string.Empty);
            (int, long, string) a = Key(x);
            (int, long, string) b = Key(y);
            return a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1) : a.Item2 != b.Item2 ? a.Item2.CompareTo(b.Item2) : string.CompareOrdinal(a.Item3, b.Item3);
        }
    }
}