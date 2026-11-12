using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>How a part of a collection is told: by the collection's bars, or by its PDF's or scan's pages.</summary>
public enum PartUnit
{
    Bars,
    Pages,

    /// <summary>A recording heard, by time (version 0.5): <see cref="PartRange.Milliseconds"/>.</summary>
    Milliseconds,
}

/// <summary>
/// One piece the player confirmed inside a collection (version 0.4, «Пьесы в файле»): its title, as the bookmark names it,
/// and the bars or pages it takes, both ends included.
/// </summary>
/// <param name="Title">The piece's title; blank gives "Part 2" of the collection's title.</param>
/// <param name="First">The first bar (as the workspace numbers them) or page (from 1).</param>
/// <param name="Last">The last bar or page, not before <paramref name="First"/>.</param>
public sealed record PartRange(string? Title, PartUnit Unit, int First, int Last)
{
    /// <summary>The part's own PDF: the collection's pages the part takes, copied by the app (PDFKit); null for a part without one.</summary>
    public byte[]? Pdf { get; init; }

    /// <summary>The new project's id; null takes the next one <see cref="PieceSplitOptions.NewId"/> gives.</summary>
    public string? Id { get; init; }

    /// <summary>
    /// For <see cref="PartUnit.Milliseconds"/>, the stretch of the recording the part takes, in milliseconds from the start
    /// of the stretch sent (<see cref="HeardCut.StretchOf"/> gives a piece's); null for a part by bars or pages.
    /// </summary>
    public (long From, long To)? Milliseconds { get; init; }

    /// <summary>The part's composer, as the review step names it (version 0.5); null for none.</summary>
    public string? Composer { get; init; }

    /// <summary>The part's tags, as the review step names them (version 0.5).</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>A part of a recording heard by its stretch of the sound, in milliseconds (version 0.5).</summary>
    public static PartRange InRecording(string? title, long from, long to) => new(title, PartUnit.Milliseconds, 0, 0) { Milliseconds = (from, to) };
}

/// <summary>What every part of one split shares: when it is made, the playlist it joins, the device that holds the collection.</summary>
/// <param name="NewId">A new project's id; the facade passes its own, which gives a GUID.</param>
public sealed record PieceSplitOptions(DateTimeOffset Now, Func<string> NewId)
{
    /// <summary>The playlist the parts join (the collection, version 0.2); null takes the collection's title.</summary>
    public string? Playlist { get; init; }

    /// <summary>The device that splits the collection and so holds its bytes; null leaves the collection's marks as they are.</summary>
    public HoldingDevice? Device { get; init; }

    public Words Words { get; init; } = Words.English;

    /// <summary>
    /// How many pages the collection's PDF has, as the app's viewer reads it; null counts its page objects, which a PDF
    /// with compressed objects hides (it then counts as one page).
    /// </summary>
    public int? Pages { get; init; }

    /// <summary>
    /// For a recording heard, whether each part's bars and tempo are worked out afresh from its own stretch of the sound
    /// (version 0.5, <see cref="HeardCut"/>; the experiment Several pieces in a score or recording), rather than carried
    /// over from the collection's bars (<see cref="RecordingCut"/>, as in 0.4).
    /// </summary>
    public bool FreshBars { get; init; }
}

/// <summary>
/// One part made into a piece of its own: an ordinary project the app saves beside the collection's, with where it was in
/// the collection (bars, pages, the stretch of the recording) for the app to copy the PDF's pages or the sound's stretch.
/// The project keeps the collection's original by its fingerprint (<see cref="PieceParts.OriginalId"/>), the playlist, and
/// where it is in that original (<see cref="Project.PartOf"/>, version 0.4): its pages, or the stretch of the recording;
/// none for a part of a score cut by bars without pages. A scan's page reports go with the pages the part takes.
/// </summary>
/// <param name="Span">Where the part is in the collection's notes; null for a PDF without notes.</param>
/// <param name="Notes">The part's notes as it opens, from tick 0; empty for a PDF without notes.</param>
public sealed record PiecePart(int Number, Project Project, TickSpan? Span, IReadOnlyList<Note> Notes)
{
    /// <summary>The collection's bars the part takes, as the workspace numbers them; null for a PDF without notes.</summary>
    public (int First, int Last)? Bars { get; init; }

    /// <summary>The collection's pages the part takes; null for notes without pages.</summary>
    public (int First, int Last)? Pages { get; init; }

    /// <summary>For a recording heard, the stretch of the sound the part takes, in milliseconds from the start; null otherwise.</summary>
    public (long From, long To)? Milliseconds { get; init; }
}

/// <summary>The parts of one collection and what links them: the collection's original and the playlist.</summary>
/// <param name="Original">The collection's original as each part keeps it, under <see cref="PieceParts.OriginalId"/>; null when the collection has none to name.</param>
public sealed record PieceSplit(PieceOriginal? Original, string Playlist, IReadOnlyList<PiecePart> Parts);

/// <summary>
/// Several pieces over one original (v0.4 design, B, «Первая версия без изменения формата»; ideas bank, «Несколько
/// произведений внутри одного файла или записи»: a logical organisation, not physical cutting). The collection's project is
/// not changed: each confirmed part becomes an ordinary project with its own notes, title, playlist and Check flags, and
/// the collection's original described in its <c>originals</c> with the same fingerprint, so the library can show the
/// pieces of one collection together. A score or scan is cut by bars (pages are bars from the page map, a scan's
/// <c>new-page</c> marks); a recording heard is cut by bars, with the stretch of the sound each part takes.
/// </summary>
/// <remarks>
/// Pure and deterministic: the same collection, parts and options give the same projects. A part's notes start at tick 0
/// with the meter, key, clefs and tempo in effect where it begins; their provenance and confidence are those of the
/// collection's notes (detected stays detected; a scan's checked bars stay checked; a recording's bars stay inferred). A
/// part is made from the notes as read or heard: the player's corrections, marks, bookmarks, ideas and versions stay with
/// the collection. A note tied across the place a part begins is cut there. Joining the parts' notes back in order gives
/// the collection's notes.
/// </remarks>
public static class PieceParts
{
    /// <summary>The id each part gives the collection's original in its <c>originals</c>.</summary>
    public const string OriginalId = "src_book";

    /// <summary>The id of a part's own stretch of the sound (version 0.5), cut and kept on the device that cut it.</summary>
    public const string SoundId = "src_sound";

    /// <summary>
    /// The part with its own stretch of the sound described in its originals (<see cref="SoundId"/>): a WAV kept only on the
    /// device that cut it, named by the part's title, with its size, length and fingerprint. The sound itself is never in
    /// the project file.
    /// </summary>
    public static Project WithSound(Project part, long bytes, long durationMs, string sha256, HoldingDevice device, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(part);
        string fileName = Path.ChangeExtension(MidiExporter.FileName(part.Details.Title.Value), ".wav");
        var sound = new PieceOriginal(SoundId, OriginalKind.Audio, fileName, OriginalKept.Device, at) { Bytes = bytes, DurationMs = durationMs, Sha256 = sha256 };
        return part.WithOriginal(sound.HeldBy(device, at));
    }

    /// <summary>The collection cut into the parts, in their order.</summary>
    /// <exception cref="ArgumentException">No part, a part outside the collection or overlapping another, pages for notes without pages, or a part of a PDF without its own PDF.</exception>
    /// <exception cref="NotSupportedException">The collection is a MIDI file (not a recording heard), which is not split yet.</exception>
    public static PieceSplit Split(Project collection, IReadOnlyList<PartRange> parts, PieceSplitOptions options)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(options);
        if (parts.Count == 0)
        {
            throw new ArgumentException("Name at least one part.", nameof(parts));
        }

        PieceOriginal? original = OriginalOf(collection, options);
        string playlist = PieceMetadata.Clean(options.Playlist) ?? collection.Details.Title.Value;
        var shared = new Shared(collection, original, playlist, options);
        IReadOnlyList<PiecePart> made = collection.HasNotes ? FromNotes(shared, parts) : FromDocument(shared, parts);
        return new PieceSplit(original, playlist, made);
    }

    /// <summary>What the parts of one split share.</summary>
    private sealed record Shared(Project Collection, PieceOriginal? Original, string Playlist, PieceSplitOptions Options)
    {
        public string TitleOf(PartRange range, int number) =>
            PieceMetadata.Clean(range.Title) ?? $"{Collection.Details.Title.Value} · {Options.Words.T("Part", "Часть")} {number}";

        /// <summary>A new project for the part: its id, file name, title, the playlist and the collection's original.</summary>
        public Project Create(PartRange range, int number, string extension, byte[] source)
        {
            string title = TitleOf(range, number);
            string fileName = Path.ChangeExtension(MidiExporter.FileName(title), extension);
            Project project = Project.Create(range.Id ?? Options.NewId(), fileName, source, Options.Now) with { Metadata = new PieceMetadata(title, range.Composer, range.Tags, [Playlist]) };
            return Original is { } book ? project.WithOriginal(book) : project;
        }

        /// <summary>
        /// The part with its own PDF described as photos of its pages when the collection's PDF is photos (v0.4, «Фото
        /// страниц»; <see cref="OriginalKind.Images"/>, a value of the project file since 0.3), so the iPad reads it page by
        /// page as it would the collection; as it is otherwise.
        /// </summary>
        public Project AsPhotos(Project part, int pages) =>
            Collection.FindOriginal(PieceOriginal.DocumentId) is { Kind: OriginalKind.Images } && part.FindOriginal(PieceOriginal.DocumentId) is { } own
                ? part.WithOriginal(own with { Kind = OriginalKind.Images, Pages = pages })
                : part;

        /// <summary>
        /// Where the part is in the collection's original (<see cref="Projects.PartOf"/>, version 0.4): the stretch of the
        /// recording when the original is the recording, else the pages when the collection has pages; null when the
        /// collection names no original, or its notes have neither (a score or a scan of one page, cut by bars), since a
        /// place in bars is not a value of the format.
        /// </summary>
        public PartOf? PartOf((int First, int Last)? pages, (long From, long To)? milliseconds) =>
            Original is null ? null
            : Original.Kind == OriginalKind.Audio ? milliseconds is var (from, to) && to > from ? Projects.PartOf.InRecording(OriginalId, from, to) : null
            : pages is var (first, last) ? Projects.PartOf.OnPages(OriginalId, first, last)
            : null;
    }

    /// <summary>The parts of a PDF without notes: each a PDF piece of the pages the app copied.</summary>
    private static List<PiecePart> FromDocument(Shared shared, IReadOnlyList<PartRange> parts)
    {
        int pages = shared.Options.Pages ?? PdfDocument.PageCount(shared.Collection.Document!);
        List<PiecePart> made = [];
        int after = 0;
        for (int i = 0; i < parts.Count; i++)
        {
            PartRange range = parts[i];
            if (range.Unit != PartUnit.Pages)
            {
                throw new ArgumentException("A PDF without notes is split by its pages.", nameof(parts));
            }

            CheckRange(range, after, pages > 0 ? pages : int.MaxValue, "page");
            after = range.Last;
            byte[] pdf = range.Pdf is { } own && PdfDocument.IsPdf(own) ? own : throw new ArgumentException($"Part {i + 1} needs its own PDF: the pages {range.First}–{range.Last} copied.", nameof(parts));
            Project project = shared.Create(range, i + 1, PdfDocument.Extension, []) with { Document = pdf };
            project = shared.AsPhotos(project.Fingerprinted(), range.Last - range.First + 1);
            made.Add(new PiecePart(i + 1, project with { PartOf = shared.PartOf((range.First, range.Last), null) }, null, []) { Pages = (range.First, range.Last) });
        }

        return made;
    }

    /// <summary>The parts of a collection with notes: each cut from the notes as read or heard.</summary>
    private static List<PiecePart> FromNotes(Shared shared, IReadOnlyList<PartRange> parts)
    {
        OpenedProject opened = OpenedProject.Open(shared.Collection);
        shared = shared with { Collection = opened.Project };
        Project collection = opened.Project;
        ImportedPiece piece = opened.Piece;
        PageMap? pages = PagesOf(collection, piece);
        if (collection.Transcription is null && piece.MusicXml is null)
        {
            throw new NotSupportedException("A MIDI file is not split into pieces yet: only scores, scans and recordings heard are.");
        }

        MidiImport? engine = collection.Transcription is null ? null : PieceImporter.Import(collection.FileName, collection.Source).Piece?.Midi;
        HeardClock? clock = engine is null ? null : HeardClock.Of(piece, engine);
        List<PiecePart> made = [];
        Tick after = Tick.Zero;
        for (int i = 0; i < parts.Count; i++)
        {
            TickSpan span = SpanOf(parts[i], piece, pages, clock);
            if (span.Onset < after)
            {
                throw new ArgumentException($"Part {i + 1} begins before part {i} ends: name the parts in order, each once.", nameof(parts));
            }

            after = span.End;
            Cut cut = engine is null ? ScoreCutOf(piece, span)
                : shared.Options.FreshBars ? FreshCut(collection, engine, clock!, parts[i], span, shared.Original is not null)
                : RecordingCut.Of(collection, piece, engine, span, shared.Original is not null);
            made.Add(Part(shared, parts[i], i + 1, piece, pages, span, cut));
        }

        return made;
    }

    /// <summary>The notes of one part, ready to become a project: its source, its origin and the stretch of a recording.</summary>
    internal sealed record Cut(byte[] Source, string Extension, TranscriptionOrigin? Transcription, (long From, long To)? Milliseconds)
    {
        /// <summary>The part's own Check flags, worked out for its own bars; null moves the collection's to the part's bars.</summary>
        public IReadOnlyList<CheckFlag>? Check { get; init; }
    }

    /// <summary>A part of a recording cut by time on bars of its own: its stretch as sent, or the time of its bar lines.</summary>
    private static Cut FreshCut(Project collection, MidiImport engine, HeardClock clock, PartRange range, TickSpan span, bool linked)
    {
        (long from, long to) = range.Milliseconds ?? HeardCut.StretchOfBars(clock, span);
        return HeardCut.Of(collection, engine, from, to, linked);
    }

    private static Cut ScoreCutOf(ImportedPiece piece, TickSpan span)
    {
        int[] measures = [.. piece.MusicXml!.Measures.Where(measure => measure.Span.Onset >= span.Onset && measure.Span.Onset < span.End).Select(static measure => measure.Index)];
        return measures.Length > 0
            ? new Cut(ScoreCut.Cut(piece.ScoreXml!, measures[0], measures[^1]), ".musicxml", null, null)
            : throw new ArgumentException("The part has no bars of the score.", nameof(span));
    }

    private static PiecePart Part(Shared shared, PartRange range, int number, ImportedPiece piece, PageMap? pages, TickSpan span, Cut cut)
    {
        if (pages is null && range.Pdf is not null)
        {
            throw new ArgumentException("These notes have no pages, so a part keeps no PDF of its own.", nameof(range));
        }

        (int First, int Last)? onPages = pages is null ? null : (pages.PageOf(span.Onset), pages.PageOf(new Tick(span.End.Value - 1)));
        Project collection = shared.Collection;
        Project project = shared.Create(range, number, cut.Extension, cut.Source) with { Transcription = cut.Transcription, Check = [] };
        if (collection.Scan is { } scan)
        {
            project = project with { Scan = new ScanOrigin(scan.Engine, []) { From = shared.Original?.Id, At = scan.At, Pages = ReportsOn(scan, pages, onPages) } };
        }

        ImportedPiece own = OpenedProject.Open(project).Piece;
        int? Moved(int bar) => BarIn(piece.Meter, own, span, bar);
        project = project with
        {
            Check = cut.Check ?? [.. (collection.Check ?? []).Where(static flag => flag.Source != CheckSource.Performance)
                .Select(flag => Moved(flag.Bar) is { } moved ? new CheckFlag(moved, flag.Source, flag.Reason, flag.Resolved) : null).OfType<CheckFlag>()],
            Scan = project.Scan is { } read
                ? read.Confirm(collection.Scan!.ConfirmedBars.Select(Moved).OfType<int>())
                : null,
            PartOf = shared.PartOf(onPages, cut.Milliseconds),
        };
        if (pages is not null && onPages is var (first, last))
        {
            project = shared.AsPhotos(WithPdf(project, range.Pdf, pages, span, first, last), last - first + 1);
        }

        OpenedProject opened = OpenedProject.Open(project);
        return new PiecePart(number, project, span, opened.Notes)
        {
            Bars = (piece.Meter.BarAt(span.Onset).Number, piece.Meter.BarAt(new Tick(span.End.Value - 1)).Number),
            Pages = onPages,
            Milliseconds = cut.Milliseconds,
        };
    }

    /// <summary>
    /// The scan server's reports on the part's pages, numbered from 1 again, when the collection keeps one report per page
    /// of its page map (version 0.4); none otherwise, since a report then cannot be told to belong to a page of the part.
    /// </summary>
    private static IReadOnlyList<Import.Scan.PageReport> ReportsOn(ScanOrigin scan, PageMap? pages, (int First, int Last)? onPages) =>
        pages is not null && onPages is var (first, last) && scan.Pages.Count == pages.Pages
            ? Import.Scan.PageReport.ForPages([.. scan.Pages.Skip(first - 1).Take(last - first + 1)], last - first + 1)
            : [];

    /// <summary>The part with its own PDF, the pages starting where they did in the collection; as it is without one.</summary>
    private static Project WithPdf(Project project, byte[]? pdf, PageMap pages, TickSpan span, int first, int last)
    {
        if (pdf is null)
        {
            return project;
        }

        if (!PdfDocument.IsPdf(pdf))
        {
            throw new ArgumentException("A part's \"pdf\" is not a PDF.", nameof(pdf));
        }

        Tick[] starts = [Tick.Zero, .. Enumerable.Range(first + 1, last - first).Select(page => new Tick(pages.StartOf(page).Value - span.Onset.Value))];
        return (project with
        {
            Document = pdf,
            DocumentPages = new PageMap(starts),
            Scan = project.Scan is { } scan ? scan with { From = PieceOriginal.DocumentId } : null,
        }).Fingerprinted();
    }

    /// <summary>A collection's bar as the part numbers it; null for a bar outside the part.</summary>
    private static int? BarIn(MeterMap collection, ImportedPiece part, TickSpan span, int bar)
    {
        Tick onset;
        try
        {
            onset = collection.GetBar(bar).Span.Onset;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        return onset >= span.Onset && onset < span.End ? part.Meter.BarAt(new Tick(onset.Value - span.Onset.Value)).Number : null;
    }

    /// <summary>Where a part is in the collection's notes: from its first bar's or page's start to its last one's end.</summary>
    private static TickSpan SpanOf(PartRange range, ImportedPiece piece, PageMap? pages, HeardClock? clock)
    {
        if (range.Unit == PartUnit.Milliseconds)
        {
            if (clock is null || range.Milliseconds is not var (from, to))
            {
                throw new ArgumentException("Only a recording heard is split by time, each part with its \"ms\".", nameof(range));
            }

            Tick onset = clock.TickAt(from * 1000), until = clock.TickAt(to * 1000);
            return from >= 0 && until > onset ? TickSpan.FromBounds(onset, until) : throw new ArgumentException($"A part's stretch runs forwards from 0 ms; not {from}–{to} ms.", nameof(range));
        }

        if (range.Unit == PartUnit.Pages)
        {
            if (pages is null)
            {
                throw new ArgumentException("These notes have no pages: name the part by its bars.", nameof(range));
            }

            CheckRange(range, 0, pages.Pages, "page");
            Tick next = range.Last < pages.Pages ? pages.StartOf(range.Last + 1) : piece.End;
            return TickSpan.FromBounds(pages.StartOf(range.First), next);
        }

        MeterMap meter = piece.Meter;
        int lastBar = meter.BarAt(new Tick(Math.Max(0, piece.End.Value - 1))).Number;
        CheckRange(range, meter.FirstBarNumber - 1, lastBar, "bar");
        Tick end = meter.GetBar(range.Last).Span.End;
        return TickSpan.FromBounds(meter.GetBar(range.First).Span.Onset, range.Last == lastBar && piece.End > end ? piece.End : end);
    }

    private static void CheckRange(PartRange range, int after, int last, string unit)
    {
        if (range.First <= after || range.Last < range.First || range.Last > last)
        {
            throw new ArgumentException($"A part's {unit}s run from {after + 1} to {last}, first before last, each part after the one before; not {range.First}–{range.Last}.", nameof(range));
        }
    }

    /// <summary>Where the collection's pages start in its notes: the PDF's page map, else a scan's <c>new-page</c> marks; null without pages.</summary>
    public static PageMap? PagesOf(Project collection, ImportedPiece piece)
    {
        if (collection.DocumentPages is { } map)
        {
            return map;
        }

        if (collection.Scan is null || piece.ScoreXml is not { } xml)
        {
            return null;
        }

        int count = Import.Scan.ScanJoin.Split(xml).Count;
        return count > 1 ? PageMap.Of(piece, count) : null;
    }

    /// <summary>
    /// The collection's original as a part keeps it (<see cref="OriginalId"/>): its PDF, else the original its notes were
    /// read or heard from, else the file they were imported from; with its fingerprint worked out when its bytes are here,
    /// kept beside the part rather than in its file, and marked held by the device that splits it. A recording heard whose
    /// piece describes no original (every one made before version 0.4) is described as the recording, by the piece's name,
    /// with no fingerprint and held by no device, since neither app keeps that sound: so a part can say which stretch of it
    /// it takes (<see cref="Project.PartOf"/>).
    /// </summary>
    private static PieceOriginal? OriginalOf(Project collection, PieceSplitOptions options)
    {
        Project fingerprinted = collection.Fingerprinted();
        PieceOriginal? found = fingerprinted.Document is not null ? fingerprinted.FindOriginal(PieceOriginal.DocumentId)
            : (collection.Scan?.From ?? collection.Transcription?.From) is { } from && collection.FindOriginal(from) is { } read ? read
            : collection.FindOriginal(PieceOriginal.SourceFileId) is { } file ? file with { Sha256 = file.Sha256 ?? PieceOriginal.Fingerprint(collection.Source) }
            : null;
        if (found is null)
        {
            return HeardFrom(collection);
        }

        PieceOriginal original = new(OriginalId, found.Kind, found.FileName, OriginalKept.Device, found.AddedAt)
        {
            Name = NameOf(found, collection),
            Sha256 = found.Sha256,
            Bytes = found.Bytes,
            Pages = found.Pages ?? options.Pages ?? (collection.Document is { } pdf && PdfDocument.PageCount(pdf) is > 0 and var count ? count : null),
            DurationMs = found.DurationMs,
            Held = found.Held,
        };
        return options.Device is { } device ? original.HeldBy(device, options.Now) : original;
    }

    /// <summary>
    /// The recording a piece was heard from, when the piece describes none: by its name, its bytes on no device; null for a
    /// piece not heard from a recording.
    /// </summary>
    private static PieceOriginal? HeardFrom(Project collection)
    {
        if (collection.Transcription is null)
        {
            return null;
        }

        string name = Path.GetFileNameWithoutExtension(collection.FileName);
        return new PieceOriginal(OriginalId, OriginalKind.Audio, string.IsNullOrWhiteSpace(name) ? "recording" : name, OriginalKept.Device, collection.CreatedAt)
        {
            Name = TitleOf(collection),
        };
    }

    /// <summary>
    /// The collection's title, as the name its parts give its original when the original has none of the player's: so a
    /// part says it was made from «Сборник», not from «IMG_0412.pdf».
    /// </summary>
    private static string? TitleOf(Project collection) => PieceMetadata.Clean(collection.Details.Title.Value);

    /// <summary>The name the player gave the original, else the collection's title (<see cref="TitleOf"/>).</summary>
    private static string? NameOf(PieceOriginal found, Project collection) => found.Name ?? TitleOf(collection);

    /// <summary>
    /// The original a part made from <paramref name="collection"/> keeps under <see cref="OriginalId"/>, as
    /// <see cref="Split"/> describes it (without any device's mark), so an app can find the pieces made from the piece open
    /// among its library's rows (<see cref="IsPartOf"/>); null for a piece that names no original.
    /// </summary>
    public static PieceOriginal? CollectionOriginal(Project collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        return OriginalOf(collection, new PieceSplitOptions(collection.CreatedAt, static () => ""));
    }

    /// <summary>
    /// Whether a piece made from a collection (<paramref name="part"/>, from its library row) was made from the collection
    /// whose original is <paramref name="collection"/> (<see cref="CollectionOriginal"/>): the same fingerprint when both
    /// have one, else the same kind, file name and time it was added, as a recording described by its name has no fingerprint.
    /// </summary>
    public static bool IsPartOf(PartSummary part, PieceOriginal collection)
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(collection);
        PieceOriginal kept = part.Original;
        return kept.Sha256 is { } fingerprint && collection.Sha256 is not null
            ? collection.Matches(fingerprint)
            : kept.Kind == collection.Kind && kept.FileName == collection.FileName && kept.AddedAt == collection.AddedAt;
    }
}