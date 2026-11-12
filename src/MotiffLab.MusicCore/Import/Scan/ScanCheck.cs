using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Scan;

/// <summary>
/// The "Check the scan" step for a piece read from a photo or PDF by sheet music recognition
/// (decision record 0011): which bars look doubtful and why, and the notes marked as detected.
/// </summary>
/// <remarks>
/// <para>
/// Recognition writes MusicXML that imports like any other, but its notes are a reading of a picture,
/// not facts from a file. Every note is marked <see cref="Provenance.Detected"/>: notes in a flagged
/// bar at <see cref="FlaggedConfidence"/>, the rest at <see cref="CleanConfidence"/>, since the engine
/// gives no per-note scores. Notes in a bar the player has confirmed become
/// <see cref="Provenance.UserConfirmed"/>; corrections still go to the edit log and become user-edited.
/// </para>
/// <para>
/// With the scan server's report on each page (<see cref="PageReport"/>), the page a bar was read from counts too: a bar
/// on a weak page is at <see cref="WeakPageConfidence"/>, below the 0.6 hints need, and every bar on a poor page is
/// flagged <see cref="ScanFlag.PageQuality"/>. The report is not kept: it counts each time it is given.
/// </para>
/// <para>
/// The checks are rules on the imported score, so they say what is wrong in it, not what the page shows.
/// Pure and deterministic: the same piece gives the same bars.
/// </para>
/// </remarks>
public sealed record ScanCheck
{
    /// <summary>The confidence of a note in a bar a check flagged: below the 0.6 that hints need (05-domain-architecture).</summary>
    public const double FlaggedConfidence = 0.4;

    /// <summary>The confidence of a note in a bar no check flagged; it is still a reading, not a fact.</summary>
    public const double CleanConfidence = 0.7;

    /// <summary>The confidence of a note in a bar no check flagged on a weak page: below the 0.6 hints need, so it shows as unsure.</summary>
    public const double WeakPageConfidence = 0.5;

    /// <summary>The lowest and highest keys of an 88-key piano, A0 and C8.</summary>
    public const int LowestPianoKey = 21;

    public const int HighestPianoKey = 108;

    private ScanCheck(IReadOnlyList<ScanBar> bars, IReadOnlyList<ScanPage> pages) => (Bars, Pages) = (bars, pages);

    /// <summary>Every measure of the score in order, flagged or not.</summary>
    public IReadOnlyList<ScanBar> Bars { get; }

    /// <summary>
    /// Each page the scan server reported on, in order, with its quality and the bars read from it; empty when the check
    /// was given no reports. For the summary by page: «Страница 4: 9 из 12 тактов под вопросом».
    /// </summary>
    public IReadOnlyList<ScanPage> Pages { get; }

    /// <summary>The bars at least one check flagged, in order.</summary>
    public IEnumerable<ScanBar> Flagged => Bars.Where(static bar => bar.IsFlagged);

    /// <summary>Checks a piece imported from recognised MusicXML.</summary>
    /// <exception cref="ArgumentException">The piece was not imported from MusicXML.</exception>
    public static ScanCheck Of(ImportedPiece piece) => Of(piece, null);

    /// <summary>
    /// Checks a piece imported from recognised MusicXML whose pages the scan server reported on, one report per page in
    /// order (<see cref="ScanJoin"/> marks where each page starts). Without reports, as <see cref="Of(ImportedPiece)"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The piece was not imported from MusicXML.</exception>
    public static ScanCheck Of(ImportedPiece piece, IReadOnlyList<PageReport>? pages)
    {
        ArgumentNullException.ThrowIfNull(piece);
        MusicXmlImport import = piece.MusicXml ?? throw new ArgumentException("A scan is read as MusicXML.", nameof(piece));
        PageMap? map = pages is { Count: > 0 } && import.Measures.Count > 0 ? PageMap.Of(piece, pages.Count) : null;

        var notesByMeasure = import.SourceNotes.Zip(import.Notes)
            .ToLookup(static pair => pair.First.MeasureIndex);
        var restsByMeasure = import.Rests.ToLookup(static rest => rest.MeasureIndex);
        var graceByMeasure = import.GraceNotes.ToLookup(static grace => grace.MeasureIndex);

        var bars = new List<ScanBar>(import.Measures.Count);
        for (int i = 0; i < import.Measures.Count; i++)
        {
            MusicXmlMeasure measure = import.Measures[i];
            var notes = notesByMeasure[measure.Index].ToList();
            var flags = new List<ScanFlag>();

            long expected = piece.Meter.SignatureAt(measure.Span.Onset).BarTicks;
            bool first = i == 0;
            bool last = i == import.Measures.Count - 1;
            if (measure.Span.Duration > expected)
            {
                flags.Add(ScanFlag.BarTooLong);
            }
            else if (measure.Span.Duration < expected && !first && !last)
            {
                // A short first bar is a pickup, and a short last bar usually completes it.
                flags.Add(ScanFlag.BarTooShort);
            }

            if (StavesDiffer(measure, notes.Select(static pair => pair.First), restsByMeasure[measure.Index]))
            {
                flags.Add(ScanFlag.StavesDiffer);
            }

            if (notes.Any(static pair => pair.First.Pitch is < LowestPianoKey or > HighestPianoKey))
            {
                flags.Add(ScanFlag.OutOfPianoRange);
            }

            if (graceByMeasure[measure.Index].Any())
            {
                flags.Add(ScanFlag.GraceNotes);
            }

            int? page = map?.PageOf(measure.Span.Onset);
            PageQuality? quality = page is { } number ? pages![number - 1].Quality : null;
            if (quality == PageQuality.Poor)
            {
                flags.Add(ScanFlag.PageQuality);
            }

            bars.Add(new ScanBar(
                piece.Meter.ToPosition(measure.Span.Onset).Bar,
                measure.Number,
                measure.Span,
                flags,
                [.. notes.Select(static pair => pair.Second.Id)])
            {
                Page = page,
                PageQuality = quality,
            });
        }

        return new ScanCheck(bars, PagesOf(pages, bars));
    }

    /// <summary>Each reported page with the bars read from it; none without reports.</summary>
    private static List<ScanPage> PagesOf(IReadOnlyList<PageReport>? pages, List<ScanBar> bars) =>
        pages is null ? [] : [.. pages.Select((report, index) => new ScanPage(index + 1, report, [.. bars.Where(bar => bar.Page == index + 1)]))];

    /// <summary>The bar that starts at bar number <paramref name="bar"/>, or null when no measure does.</summary>
    public ScanBar? Find(int bar) => Bars.FirstOrDefault(scanned => scanned.Bar == bar);

    /// <summary>
    /// The notes marked as a scan's notes are: detected, less sure in flagged bars and on weak pages, and confirmed in
    /// the bars the player said look right. Hands read from the staves are marked the same way.
    /// Notes the check does not know, such as ones added later, are left as they are.
    /// </summary>
    public IReadOnlyList<Note> Mark(IReadOnlyList<Note> notes, IReadOnlyCollection<int> confirmedBars)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(confirmedBars);

        var attributions = new Dictionary<string, Attribution>(StringComparer.Ordinal);
        foreach (ScanBar bar in Bars)
        {
            Attribution attribution = confirmedBars.Contains(bar.Bar)
                ? Attribution.UserConfirmed
                : Attribution.Detected(bar.Confidence);
            foreach (string id in bar.NoteIds)
            {
                attributions[id] = attribution;
            }
        }

        return [.. notes.Select(note => attributions.TryGetValue(note.Id, out Attribution? attribution)
            ? note with
            {
                Attribution = attribution,
                Hand = note.Hand is { } hand ? hand with { Attribution = attribution } : null,
            }
            : note)];
    }

    /// <summary>The piece with its notes marked for a scan (<see cref="Mark"/>).</summary>
    public ImportedPiece Mark(ImportedPiece piece, IReadOnlyCollection<int> confirmedBars)
    {
        ArgumentNullException.ThrowIfNull(piece);
        return piece with { Notes = Mark(piece.Notes, confirmedBars) };
    }

    /// <summary>
    /// True when one staff's notes and rests stop short of the measure's end while another staff's
    /// reach it: one hand's rhythm does not add up. Staves without anything written are not compared.
    /// </summary>
    private static bool StavesDiffer(MusicXmlMeasure measure, IEnumerable<MusicXmlNote> notes, IEnumerable<MusicXmlRest> rests)
    {
        Tick end = measure.Span.End;
        var reach = new Dictionary<(int Track, int Staff), Tick>();
        void Extend(int track, int staff, Tick until)
        {
            Tick capped = until > end ? end : until;
            reach[(track, staff)] = reach.TryGetValue((track, staff), out Tick current) && current > capped ? current : capped;
        }

        foreach (MusicXmlNote note in notes)
        {
            Extend(note.Track, note.Staff, note.Span.End);
        }

        foreach (MusicXmlRest rest in rests)
        {
            Extend(rest.Track, rest.Staff, rest.Span.End);
        }

        return reach.Count > 1 && reach.Values.Any(until => until == end) && reach.Values.Any(until => until < end);
    }
}

/// <summary>One measure of a scanned score and what the checks found in it.</summary>
/// <param name="Bar">The bar number the measure starts at, as the workspace shows it.</param>
/// <param name="Measure">The measure's number in the recognised score, when it has one; it counts the measures on the pages.</param>
/// <param name="Span">The whole measure: a measure that runs long covers more than one bar of the meter.</param>
/// <param name="NoteIds">The notes that start in the measure.</param>
public sealed record ScanBar(int Bar, string? Measure, TickSpan Span, IReadOnlyList<ScanFlag> Flags, IReadOnlyList<string> NoteIds)
{
    public bool IsFlagged => Flags.Count > 0;

    /// <summary>The page the measure was read from, from 1, when the scan server reported on the pages; otherwise null.</summary>
    public int? Page { get; init; }

    /// <summary>How well that page can be trusted (<see cref="PageReport.Quality"/>); null without a report.</summary>
    public PageQuality? PageQuality { get; init; }

    /// <summary>
    /// How sure the reading of the bar is: <see cref="ScanCheck.FlaggedConfidence"/> when flagged,
    /// <see cref="ScanCheck.WeakPageConfidence"/> on a weak page, otherwise <see cref="ScanCheck.CleanConfidence"/>.
    /// </summary>
    public double Confidence => IsFlagged ? ScanCheck.FlaggedConfidence
        : PageQuality == Scan.PageQuality.Weak ? ScanCheck.WeakPageConfidence
        : ScanCheck.CleanConfidence;

    /// <summary>What each flag means, in plain words for the player, in the order the checks ran.</summary>
    public IEnumerable<string> Reasons => ReasonsIn(Words.English);

    /// <summary><see cref="Reasons"/> in the player's language.</summary>
    public IEnumerable<string> ReasonsIn(Words words) => Flags.Select(flag => Describe(flag, words));

    public static string Describe(ScanFlag flag) => Describe(flag, Words.English);

    public static string Describe(ScanFlag flag, Words words) => flag switch
    {
        ScanFlag.BarTooLong => words.T(
            "The notes add up to more than the time signature allows: a note or rest was probably read too long.",
            "Ноты в сумме длиннее, чем позволяет размер: вероятно, какую-то ноту или паузу прочитали слишком длинной."),
        ScanFlag.BarTooShort => words.T(
            "The notes add up to less than the time signature: a note or rest was probably missed or read too short.",
            "Ноты в сумме короче, чем требует размер: вероятно, какую-то ноту или паузу пропустили или прочитали слишком короткой."),
        ScanFlag.StavesDiffer => words.T("The two hands do not add up to the same length.", "У двух рук получается разная длина такта."),
        ScanFlag.OutOfPianoRange => words.T(
            "A note is outside the piano's range, so a clef or ledger line was probably misread.",
            "Одна из нот выходит за диапазон фортепиано: вероятно, неверно прочитали ключ или добавочную линейку."),
        ScanFlag.GraceNotes => words.T(
            "The bar has grace notes, which are often misread, and Motiff Lab does not play them yet.",
            "В такте есть форшлаги: их часто читают неверно, и Motiff Lab пока их не играет."),
        ScanFlag.PageQuality => words.T(
            "The page this bar is on was too dark, too soft or could not be read, so any note in it may be wrong. Compare it with the page, or photograph the page again.",
            "Страница с этим тактом слишком тёмная, нечёткая или не прочиталась, поэтому любая нота в такте может быть неверной. Сверьте такт со страницей или переснимите её."),
        _ => throw new ArgumentOutOfRangeException(nameof(flag), flag, "Unknown scan flag."),
    };
}

/// <summary>One page of a scan the server reported on: its place in the score, from 1, the server's report and the bars read from it.</summary>
public sealed record ScanPage(int Page, PageReport Report, IReadOnlyList<ScanBar> Bars)
{
    /// <summary>How well the page can be trusted (<see cref="PageReport.Quality"/>).</summary>
    public PageQuality Quality => Report.Quality;

    /// <summary>The bars below the 0.6 a hint needs, flagged or on a weak page, other than those the player confirmed.</summary>
    public int DoubtfulOf(IReadOnlyCollection<int> confirmedBars)
    {
        ArgumentNullException.ThrowIfNull(confirmedBars);
        return Bars.Count(bar => bar.Confidence < ScanCheck.CleanConfidence && !confirmedBars.Contains(bar.Bar));
    }
}

/// <summary>Why a scanned bar is worth checking.</summary>
public enum ScanFlag
{
    BarTooLong,
    BarTooShort,
    StavesDiffer,
    OutOfPianoRange,
    GraceNotes,

    /// <summary>The bar is on a page the scan server's report calls poor (<see cref="PageReport.Quality"/>).</summary>
    PageQuality,
}