using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Notation;

/// <summary>
/// A range of bars to engrave on their own (decision record 0018, section 4; preparation item 10): bar
/// numbers as the meter map counts them, 0 for a pickup, both ends included.
/// </summary>
public readonly record struct BarRange
{
    public BarRange(int firstBar, int lastBar)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstBar);
        ArgumentOutOfRangeException.ThrowIfLessThan(lastBar, firstBar);
        FirstBar = firstBar;
        LastBar = lastBar;
    }

    public int FirstBar { get; }

    public int LastBar { get; }

    public int BarCount => LastBar - FirstBar + 1;

    public bool Contains(int bar) => bar >= FirstBar && bar <= LastBar;

    /// <summary>The bars a span touches: from the bar that holds its onset to the bar that holds its last tick.</summary>
    public static BarRange Of(MeterMap meter, TickSpan span)
    {
        ArgumentNullException.ThrowIfNull(meter);
        Bar first = meter.BarAt(span.Onset);
        Bar last = meter.BarAt(span.Duration > 0 ? new Tick(span.End.Value - 1) : span.Onset);
        return new BarRange(first.Number, last.Number);
    }
}

/// <summary>
/// Staves to engrave above the original, bar for bar (decision record 0018, section 4; preparation item 10):
/// other notes, such as a variation in place of the passage it changed, or the same notes written as played.
/// Verovio engraves them from the one MEI document at <see cref="NotationGenerator.OssiaScale"/>, with the
/// label at the left; two ossias at once compare two variations.
/// </summary>
/// <param name="Label">The label at the left of the staves, in the player's language: "Variation", "As played", the name of an idea.</param>
/// <param name="Notes">The notes to write, over the whole piece; the bars of the excerpt pick what is shown.</param>
/// <param name="AsHeard">Written for reading, as <see cref="NotationGenerator.Generate"/>'s <c>asHeard</c>.</param>
/// <param name="PlayedTempos">Written as played, with the marks of the playing, as <c>playedTempos</c>.</param>
/// <param name="Pedal">With <paramref name="PlayedTempos"/>: when the sustain pedal was held.</param>
public sealed record NotationOssia(string Label, IEnumerable<Note> Notes, bool AsHeard = false, IReadOnlyList<PlaybackTempo>? PlayedTempos = null, IEnumerable<TickSpan>? Pedal = null);

/// <summary>An ossia as engraved: its label, its staves numbered as in the document, and how many notes it holds.</summary>
/// <param name="IdPrefix">What its notes' ids start with: the shared-model id follows ("ossia-n_0005").</param>
public sealed record GeneratedOssia(string Label, IReadOnlyList<NotationStaff> Staves, int NoteCount, string IdPrefix);