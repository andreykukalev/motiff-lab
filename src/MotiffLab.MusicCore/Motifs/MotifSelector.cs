using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Motifs;

/// <summary>
/// Turns what the player marks on the piano roll into a <see cref="MotifSelection"/>: whole bars
/// typed in or clicked, or a drag whose edges snap to beats (S1-M1), limited to 1–8 bars.
/// </summary>
public static class MotifSelector
{
    /// <summary>
    /// The range from the start of <paramref name="firstBar"/> to the end of <paramref name="lastBar"/>.
    /// The bars may be given in either order. Throws when the range is over <see cref="MotifSelection.MaxBars"/>
    /// bars or a bar is not in the piece.
    /// </summary>
    public static TickSpan Bars(MeterMap meter, int firstBar, int lastBar, Tick end)
    {
        ArgumentNullException.ThrowIfNull(meter);
        (int from, int to) = firstBar <= lastBar ? (firstBar, lastBar) : (lastBar, firstBar);
        int lastInPiece = LastBar(meter, end);
        if (from < meter.FirstBarNumber || to > lastInPiece)
        {
            throw new ArgumentOutOfRangeException(nameof(lastBar), $"The piece has bars {meter.FirstBarNumber} to {lastInPiece}.");
        }

        if (to - from + 1 > MotifSelection.MaxBars)
        {
            throw new ArgumentOutOfRangeException(nameof(lastBar), $"A selection spans at most {MotifSelection.MaxBars} bars.");
        }

        return TickSpan.FromBounds(meter.GetBar(from).Span.Onset, meter.GetBar(to).Span.End);
    }

    /// <summary>
    /// The beat-aligned range a drag from <paramref name="anchor"/> to <paramref name="to"/> covers:
    /// the start moves back to its beat and the end forward to the next beat, so a drag inside one
    /// beat selects that beat. A drag over more than <see cref="MotifSelection.MaxBars"/> bars stops
    /// at that many bars from where it started. The range ends no later than the piece's last bar.
    /// </summary>
    public static TickSpan Drag(MeterMap meter, Tick anchor, Tick to, Tick end)
    {
        ArgumentNullException.ThrowIfNull(meter);
        if (end == Tick.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "The piece is empty.");
        }

        Tick pieceEnd = meter.BarAt(new Tick(end.Value - 1)).Span.End;
        Tick last = new(pieceEnd.Value - 1);
        Tick low = Min(Min(anchor, to), last);
        Tick high = Min(Max(anchor, to), last);

        Tick start = BeatStart(meter, low);
        Tick stop = BeatEnd(meter, high);
        if (stop <= start)
        {
            stop = BeatEnd(meter, start.Offset(1));
        }

        int firstBar = meter.BarAt(start).Number;
        int lastBar = meter.BarAt(new Tick(stop.Value - 1)).Number;
        if (lastBar - firstBar + 1 > MotifSelection.MaxBars)
        {
            if (anchor <= to)
            {
                stop = meter.GetBar(firstBar + MotifSelection.MaxBars - 1).Span.End;
            }
            else
            {
                start = meter.GetBar(lastBar - MotifSelection.MaxBars + 1).Span.Onset;
            }
        }

        return TickSpan.FromBounds(start, stop);
    }

    /// <summary>The notes of <paramref name="notes"/> that start inside <paramref name="span"/> and pass the filter.</summary>
    public static MotifSelection Select(IEnumerable<Note> notes, MeterMap meter, TickSpan span, SelectionFilter filter)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(filter);

        int firstBar = meter.BarAt(span.Onset).Number;
        int lastBar = meter.BarAt(new Tick(span.End.Value - 1)).Number;
        if (lastBar - firstBar + 1 > MotifSelection.MaxBars)
        {
            throw new ArgumentOutOfRangeException(nameof(span), $"A selection spans at most {MotifSelection.MaxBars} bars.");
        }

        Note[] chosen = notes
            .Where(note => span.Contains(note.Span.Onset) && filter.Matches(note))
            .OrderBy(static note => note.Span.Onset)
            .ThenBy(static note => note.Pitch.MidiNumber)
            .ThenBy(static note => note.Id, StringComparer.Ordinal)
            .ToArray();

        bool handInferred = filter.Hand is not null && chosen.Any(static note => note.Hand is { } hand
            && hand.Attribution.Provenance is not (Provenance.Imported or Provenance.UserConfirmed or Provenance.UserEdited));

        return new MotifSelection(span, firstBar, lastBar, filter, chosen.Select(static note => note.Id).ToArray(), handInferred);
    }

    /// <summary>The number of the last bar that starts before <paramref name="end"/>.</summary>
    public static int LastBar(MeterMap meter, Tick end)
    {
        ArgumentNullException.ThrowIfNull(meter);
        return meter.BarAt(new Tick(Math.Max(0, end.Value - 1))).Number;
    }

    /// <summary>The start of the beat that contains a position, never before its bar's start.</summary>
    private static Tick BeatStart(MeterMap meter, Tick position)
    {
        Bar bar = meter.BarAt(position);
        long beatTicks = bar.Signature.BeatTicks;
        long offset = position.Value - bar.Span.Onset.Value + bar.LeadIn;
        long start = position.Value - (offset % beatTicks);
        return new Tick(Math.Max(start, bar.Span.Onset.Value));
    }

    /// <summary>The first beat line at or after a position, never after its bar's end.</summary>
    private static Tick BeatEnd(MeterMap meter, Tick position)
    {
        Tick start = BeatStart(meter, position);
        if (start == position)
        {
            return position;
        }

        Bar bar = meter.BarAt(position);
        long beatTicks = bar.Signature.BeatTicks;
        long offset = start.Value - bar.Span.Onset.Value + bar.LeadIn;
        long next = start.Value + beatTicks - (offset % beatTicks);
        return new Tick(Math.Min(next, bar.Span.End.Value));
    }

    private static Tick Min(Tick a, Tick b) => a <= b ? a : b;

    private static Tick Max(Tick a, Tick b) => a >= b ? a : b;
}