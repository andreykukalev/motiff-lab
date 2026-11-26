using MotiffLab.MusicCore.Events;

namespace MotiffLab.MusicCore.Notation;

/// <summary>What a layer of a staff holds in a bar, in time order: rests, spaces, pieces of chords, beams and tuplets of them.</summary>
internal abstract class Element(long start, long ticks, int dur, int dots)
{
    public long Start { get; } = start;

    public long Ticks { get; } = ticks;

    public int Dur { get; } = dur;

    public int Dots { get; } = dots;

    /// <summary>The triplet beat the element is written in, if any: its @dur is then 3/2 of its length.</summary>
    public TupletSpan? Tuplet { get; init; }
}

/// <summary>A rest for the whole bar.</summary>
internal sealed class MeasureRest() : Element(0, 0, 0, 0);

/// <summary>A rest, or a space in a lower voice, which takes the time without a sign.</summary>
internal sealed class Rest(long start, long ticks, int dur, int dots, bool space) : Element(start, ticks, dur, dots)
{
    public bool IsSpace { get; } = space;
}

/// <summary>Eighths and shorter under one beam.</summary>
internal sealed class Beamed(Element[] elements) : Element(elements[0].Start, elements.Sum(element => element.Ticks), 0, 0)
{
    public Element[] Elements { get; } = elements;
}

/// <summary>The elements of a triplet beat.</summary>
internal sealed class TupletGroup(Element[] elements, TupletSpan span) : Element(elements[0].Start, elements.Sum(element => element.Ticks), 0, 0)
{
    public Element[] Elements { get; } = elements;

    public TupletSpan Span { get; } = span;
}

/// <summary>One note value of a chord: the whole chord, or a piece of it tied to the next or from the last.</summary>
internal sealed class ChordPiece(long start, long ticks, int dur, int dots, Chord chord, int index, bool tieIn, bool tieOut)
    : Element(start, ticks, dur, dots)
{
    public Chord Chord { get; } = chord;

    public int Index { get; } = index;

    public bool TieIn { get; } = tieIn;

    public bool TieOut { get; } = tieOut;

    public SpelledPitch[] Spellings { get; set; } = [];

    /// <summary>A grace note: written small and slashed, taking no time.</summary>
    public bool IsGrace { get; init; }

    /// <summary>The stem as the score writes it («Как напечатано»): up, down, or null where the engraver places it.</summary>
    public bool? StemUp { get; init; }

    public bool[] ShowAccidental { get; set; } = [];
}