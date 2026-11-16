using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>What a boundary between pieces, movements or large sections was found by (<see cref="SectionFinder"/>).</summary>
public enum SectionCueKind
{
    /// <summary>The start of the piece or the recording: the first section's own start.</summary>
    Opening,

    /// <summary>A final bar line (thin and thick, no repeat) before the end of the piece.</summary>
    FinalBarline,

    /// <summary>A double bar line without a repeat.</summary>
    DoubleBarline,

    /// <summary>A heading written over the music, such as "Trio", "II." or a tempo word.</summary>
    Heading,

    /// <summary>A rehearsal mark, such as "B".</summary>
    Rehearsal,

    /// <summary>A new key signature.</summary>
    KeyChange,

    /// <summary>A new time signature.</summary>
    MeterChange,

    /// <summary>A new tempo that holds for a while.</summary>
    TempoChange,

    /// <summary>Whole bars without a sounding note before the boundary, in notation.</summary>
    Rest,

    /// <summary>Seconds without a sounding note before the boundary, in a recording.</summary>
    Silence,

    /// <summary>The music heard before and after a silence fits different keys.</summary>
    HeardKey,

    /// <summary>The playing before and after a silence keeps different tempos.</summary>
    HeardTempo,

    /// <summary>A silence in a recording with the same key and tempo on both sides: proposed only to make up the number of pieces.</summary>
    SameMusic,

    /// <summary>A boundary the player added by hand.</summary>
    Added,

    /// <summary>A boundary of the pieces layer the player confirmed before (<see cref="Projects.PieceLayer.Review"/>).</summary>
    Kept,
}

/// <summary>
/// One fact a found boundary rests on, in both languages, with how much it counts towards the boundary's confidence.
/// Its text states what the score or the recording shows ("double bar line", "key changes to D minor"), nothing more.
/// </summary>
/// <param name="Weight">From 0 to 1: how strongly this fact alone says a new section starts.</param>
/// <param name="Label">For a heading, the heading as written; null for any other cue.</param>
public sealed record SectionCue(SectionCueKind Kind, double Weight, string English, string Russian, string? Label = null)
{
    /// <summary>For a silence, how long it lasts in microseconds; null for any other cue.</summary>
    public long? Microseconds { get; init; }

    public string In(Words words) => words.T(English, Russian);
}

/// <summary>
/// A section proposed as a bookmark (Andrei, 2026-11-11, "automatic bookmarks"): a piece, a movement or a large section
/// of a long score or recording, from the bar its boundary was found at to the next one. It is an inference, never a
/// fact of the file (<see cref="Attribution"/>), and it is not saved: confirmed, it becomes an ordinary bookmark.
/// </summary>
/// <param name="Number">The section's place among the proposals, from 1.</param>
/// <param name="Cues">What its boundary was found by, strongest first; the first section has the opening cue.</param>
public sealed record FoundSection(int Number, TickSpan Span, int FirstBar, int LastBar, double Confidence, IReadOnlyList<SectionCue> Cues)
{
    /// <summary>The bar the boundary was found at, when the player moved it; null when it is where it was found.</summary>
    public int? MovedFrom { get; init; }

    /// <summary>The player's title for it, trimmed; null until the player names it (<see cref="FoundSections.Rename"/>).</summary>
    public string? Title { get; init => field = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }

    /// <summary>Whether the player set this boundary (<see cref="FoundSections.Add"/>): it is the player's, not found.</summary>
    public bool IsAdded => Cues.Any(cue => cue.Kind == SectionCueKind.Added);

    /// <summary>Whether its boundary is only a silence with the same key and tempo on both sides (<see cref="SectionFinder.FindPieces"/>).</summary>
    public bool IsOnlySilence => Cues.Any(cue => cue.Kind == SectionCueKind.SameMusic);

    /// <summary>Whether this is the first section, which starts where the piece starts.</summary>
    public bool IsOpening => Cues.Any(cue => cue.Kind == SectionCueKind.Opening);

    /// <summary>The heading written where it starts ("Trio", "Andante"), if any.</summary>
    public string? Heading => Cues.Select(cue => cue.Label).FirstOrDefault(label => label is not null);

    /// <summary>Whether the player confirmed this piece before, in the pieces layer (<see cref="Projects.PieceLayer.Review"/>).</summary>
    public bool IsKept => Cues.Any(cue => cue.Kind == SectionCueKind.Kept);

    /// <summary>Inferred, at the confidence of its boundary; the player's own when the player set or confirmed it.</summary>
    public Attribution Attribution => IsAdded ? Attribution.UserEdited : IsKept ? Attribution.UserConfirmed : Attribution.Inferred(Confidence);

    /// <summary>The player's title, else "Trio" or "Part 2"; "Часть 2".</summary>
    public string NameIn(Words words) => Title ?? Heading ?? words.T($"Part {Number}", $"Часть {Number}");

    /// <summary>The title a confirmed bookmark gets: the player's, else "Part 2, bars 41–80"; "Часть 2, такты 41–80".</summary>
    public string TitleIn(Words words) => Title ?? $"{NameIn(words)}, {BarsIn(words)}";

    /// <summary>The title of a piece in the pieces layer: the player's, else the heading, else "Piece 2"; "Пьеса 2".</summary>
    public string PieceTitleIn(Words words) => Title ?? Heading ?? words.T($"Piece {Number}", $"Пьеса {Number}");

    /// <summary>"bars 41–80"; "такты 41–80".</summary>
    public string BarsIn(Words words) => FirstBar == LastBar
        ? words.T($"bar {FirstBar}", $"такт {FirstBar}")
        : words.T($"bars {FirstBar}–{LastBar}", $"такты {FirstBar}–{LastBar}");

    /// <summary>Why it was proposed, facts only: "double bar line, key changes to D minor"; after a move, where from.</summary>
    public string ReasonIn(Words words)
    {
        string found = string.Join(", ", Cues.Select(cue => cue.In(words)));
        return MovedFrom is { } bar ? words.T($"moved from bar {bar}; found by: {found}", $"перенесено с такта {bar}; найдено по: {found}") : found;
    }

    /// <summary>"likely", "possible", "uncertain"; "вероятно", "возможно", "не уверено".</summary>
    public string ConfidenceIn(Words words) => Confidence switch
    {
        >= 0.7 => words.T("likely", "вероятно"),
        >= 0.5 => words.T("possible", "возможно"),
        _ => words.T("uncertain", "не уверено"),
    };
}

/// <summary>
/// The sections proposed for a piece, as the player confirms, moves and dismisses them (<see cref="SectionFinder.Find"/>).
/// Immutable: every change gives new proposals. Nothing here is saved; a confirmed section is saved as an ordinary bookmark
/// by the caller, and <see cref="Pending"/> leaves out every section a bookmark already starts at.
/// </summary>
public sealed class FoundSections
{
    private readonly MeterMap _meter;

    internal FoundSections(IReadOnlyList<FoundSection> items, MeterMap meter, int lastBar, bool pieces = false)
    {
        Items = items;
        _meter = meter;
        LastBar = lastBar;
        IsPieces = pieces;
    }

    public static FoundSections None { get; } = new([], MeterMap.Default, 0);

    /// <summary>The sections in score order, numbered from 1; empty when no boundary was found.</summary>
    public IReadOnlyList<FoundSection> Items { get; }

    /// <summary>The last bar of the piece.</summary>
    public int LastBar { get; }

    /// <summary>
    /// True for the pieces of a score or recording (<see cref="SectionFinder.FindPieces"/>): they always cover it, so there
    /// is at least one, from its start, and dismissing the last boundary leaves that one.
    /// </summary>
    public bool IsPieces { get; }

    /// <summary>The sections no bookmark starts at yet: the ones still to confirm, move or dismiss.</summary>
    public IReadOnlyList<FoundSection> Pending(IEnumerable<Bookmark> bookmarks)
    {
        ArgumentNullException.ThrowIfNull(bookmarks);
        HashSet<int> marked = [.. bookmarks.Select(bookmark => _meter.BarAt(bookmark.Span.Onset).Number)];
        return [.. Items.Where(section => !marked.Contains(section.FirstBar))];
    }

    /// <summary>The section with this number, or null.</summary>
    public FoundSection? Find(int number) => Items.FirstOrDefault(section => section.Number == number);

    /// <summary>Whether the section's start can move to the bar: after the start of the one before it and before the start of the one after.</summary>
    public bool CanMove(int number, int bar)
    {
        int index = IndexOf(number);
        return index > 0
            && bar > Items[index - 1].FirstBar
            && bar <= (index + 1 < Items.Count ? Items[index + 1].FirstBar - 1 : LastBar);
    }

    /// <summary>
    /// The proposals with the section's start moved to the start of the bar: the section before it ends there. The first
    /// section starts where the piece starts and does not move.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The section cannot start at that bar (<see cref="CanMove"/>).</exception>
    public FoundSections Move(int number, int bar)
    {
        if (!CanMove(number, bar))
        {
            throw new ArgumentOutOfRangeException(nameof(bar), bar, $"Section {number} cannot start at bar {bar}.");
        }

        int index = IndexOf(number);
        FoundSection moved = Items[index];
        if (moved.FirstBar == bar)
        {
            return this;
        }

        Tick onset = _meter.GetBar(bar).Span.Onset;
        FoundSection before = Items[index - 1];
        var items = Items.ToList();
        items[index - 1] = before with { Span = TickSpan.FromBounds(before.Span.Onset, onset), LastBar = bar - 1 };
        items[index] = moved with { Span = TickSpan.FromBounds(onset, moved.Span.End), FirstBar = bar, MovedFrom = moved.MovedFrom ?? moved.FirstBar };
        return new FoundSections(items, _meter, LastBar, IsPieces);
    }

    /// <summary>
    /// The proposals without the section: its bars join the section before it. Once no boundary is left, nothing is
    /// proposed; for pieces (<see cref="IsPieces"/>) the one piece is left, and the first piece does not go.
    /// </summary>
    public FoundSections Dismiss(int number)
    {
        int index = IndexOf(number);
        if (index < 0 || (IsPieces && index == 0))
        {
            return this;
        }

        var items = Items.ToList();
        FoundSection gone = items[index];
        items.RemoveAt(index);
        if (index > 0 && !gone.IsOpening)
        {
            FoundSection before = items[index - 1];
            items[index - 1] = before with { Span = TickSpan.FromBounds(before.Span.Onset, gone.Span.End), LastBar = gone.LastBar };
        }

        return items.All(section => section.IsOpening) && !IsPieces
            ? new FoundSections([], _meter, LastBar)
            : Numbered(items);
    }

    /// <summary>Whether a boundary can be added at the bar: inside a section, after the bar it starts at.</summary>
    public bool CanAdd(int bar) => Items.Any(section => bar > section.FirstBar && bar <= section.LastBar);

    /// <summary>
    /// The proposals with a boundary the player set at the start of the bar: the section the bar is in ends before it, and
    /// a new one, the player's own (<see cref="FoundSection.IsAdded"/>), runs from it to where that section ended.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">No section has the bar after its first (<see cref="CanAdd"/>).</exception>
    public FoundSections Add(int bar)
    {
        if (!CanAdd(bar))
        {
            throw new ArgumentOutOfRangeException(nameof(bar), bar, $"No boundary can be added at bar {bar}.");
        }

        int index = Items.ToList().FindIndex(section => bar > section.FirstBar && bar <= section.LastBar);
        FoundSection split = Items[index];
        Tick onset = _meter.GetBar(bar).Span.Onset;
        var items = Items.ToList();
        items[index] = split with { Span = TickSpan.FromBounds(split.Span.Onset, onset), LastBar = bar - 1 };
        items.Insert(index + 1, new FoundSection(0, TickSpan.FromBounds(onset, split.Span.End), bar, split.LastBar, 1, [SectionCues.Added()]));
        return Numbered(items);
    }

    /// <summary>The proposals with the section titled by the player; a blank title takes the player's title away.</summary>
    /// <exception cref="ArgumentOutOfRangeException">No section has the number.</exception>
    public FoundSections Rename(int number, string? title)
    {
        int index = IndexOf(number);
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number, $"There is no section {number}.");
        }

        var items = Items.ToList();
        items[index] = items[index] with { Title = title };
        return new FoundSections(items, _meter, LastBar, IsPieces);
    }

    private FoundSections Numbered(List<FoundSection> items) =>
        new([.. items.Select((section, at) => section with { Number = at + 1 })], _meter, LastBar, IsPieces);

    private int IndexOf(int number)
    {
        for (int index = 0; index < Items.Count; index++)
        {
            if (Items[index].Number == number)
            {
                return index;
            }
        }

        return -1;
    }
}