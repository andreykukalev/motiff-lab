using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Settings;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// A project as it is saved on the device and in an exported file (S1-P1): one source file and
/// what the player made from it. Notes are not stored; they come from importing the source again,
/// which is deterministic, so a saved project reopens exactly as it was (AC-S1-20).
/// </summary>
/// <remarks>
/// Motifs and variations are stored as the choices that made them: a motif's range, filter and note
/// ids, and a variation's operation, parameters and Keep choices. Corrections of imported notes are
/// stored as the edit log (S1-I7), applied to the notes on every open. Timing layers join the format with
/// rhythm restoration (#27). Times are passed in, never read from a clock, so building a project is pure.
/// What the player kept about a phrase is stored as observations (#78), each variation as its steps.
/// Version 12 (0018) adds what the redesign keeps with the piece: the marks, the bookmarks and their
/// categories, the resume points that replace the sessions of 0012, the Check flags and the piece's
/// settings overrides.
/// </remarks>
/// <param name="Id">Stable for the life of the project, for example "p_3f9c…".</param>
/// <param name="Source">The imported file's bytes, exactly as picked.</param>
public sealed record Project(
    string Id,
    string FileName,
    byte[] Source,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastOpened,
    IReadOnlyList<SavedMotif> Motifs,
    IReadOnlyList<SavedVariation> Variations)
{
    /// <summary>
    /// The entities of the file this build cannot read (S6, the tolerant reader), kept to be written back unchanged; the
    /// marks keep their own in <see cref="Marks.Unknown"/> and <see cref="DocumentMarks.Unknown"/>. Empty for a project
    /// this build made or a file it reads whole.
    /// </summary>
    public IReadOnlyList<UnknownEntity> Unknown { get; init; } = [];

    /// <summary>The player's corrections of the imported notes, oldest first (S1-I7).</summary>
    public NoteEditLog Edits { get; init; } = NoteEditLog.Empty;

    /// <summary>The player's changes to the suggested phrase boundaries (S1-I5): boundaries they set and ones they took away.</summary>
    public PhraseEdits Phrases { get; init; } = PhraseEdits.None;

    /// <summary>The player's changes at the Short level (decision record 0018, section 3): cuts inside phrases they set and suggested ones they took away.</summary>
    public PhraseEdits ShortPhrases { get; init; } = PhraseEdits.None;

    /// <summary>The player's changes at the Sentence level: phrase boundaries where a sentence must end, and ones the grouping must run on across.</summary>
    public PhraseEdits Sentences { get; init; } = PhraseEdits.None;

    /// <summary>The edits at all three levels, as <see cref="PhraseLevels"/> takes them.</summary>
    public LevelEdits PhraseLevels => new(ShortPhrases, Phrases, Sentences);

    /// <summary>The project with the edits of all three levels.</summary>
    public Project WithPhraseLevels(LevelEdits edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        return this with { ShortPhrases = edits.Short, Phrases = edits.Phrase, Sentences = edits.Sentence };
    }

    /// <summary>Set when the source was read from a photo or PDF by sheet music recognition (0011); null for a file the player imported.</summary>
    public ScanOrigin? Scan { get; init; }

    /// <summary>Set when the source was heard in an audio or video recording by transcription (0014); null otherwise.</summary>
    public TranscriptionOrigin? Transcription { get; init; }

    /// <summary>
    /// The printed piece as the player added it, a PDF, shown exactly as it is in the "PDF" view (0023; Andrei, 2026-11-04);
    /// null for a piece that came as notes. A piece with a document and no source has no notes yet.
    /// </summary>
    public byte[]? Document { get; init; }

    /// <summary>The player's ink and bookmarks on the PDF's pages (0023), kept apart from the marks on the notes.</summary>
    public DocumentMarks DocumentMarks { get; init; } = DocumentMarks.Empty;

    /// <summary>
    /// For a piece with a PDF and notes, where each of the PDF's pages starts in the notes (0023; testing item 16): read from
    /// the PDF (<see cref="Recognised"/>), or added beside the notes (<see cref="WithDocument"/>); null otherwise.
    /// </summary>
    public PageMap? DocumentPages { get; init; }

    /// <summary>
    /// For notes read from a PDF piece into a separate piece, as between 2026-11-05 and 2026-11-07, that piece and where its
    /// pages start in these notes (0023); null otherwise. Nothing new is written that way.
    /// </summary>
    public ReadFromPdf? ReadFrom { get; init; }

    /// <summary>The title, composer and tags the player set in the library (version 0.2); none until they set one.</summary>
    public PieceMetadata Metadata { get; init; } = PieceMetadata.None;

    /// <summary>The title, composer and tags a card shows, and where each comes from: the player, the file or its name.</summary>
    public PieceDetails Details => PieceDetails.Of(this);

    /// <summary>Whether the piece has notes: a source to import. A PDF added as it is has none until it is recognised.</summary>
    public bool HasNotes => Source.Length > 0;

    /// <summary>A new project for a file the player just imported, with nothing made from it yet.</summary>
    public static Project Create(string id, string fileName, byte[] source, DateTimeOffset now) =>
        new(id, fileName, source, now, now, [], []);

    /// <summary>A new project for a PDF the player added to read as it is: the document and no notes (0023).</summary>
    /// <exception cref="ArgumentException">The bytes are not a PDF.</exception>
    public static Project CreateDocument(string id, string fileName, byte[] pdf, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        if (!PdfDocument.IsPdf(pdf))
        {
            throw new ArgumentException("The file is not a PDF.", nameof(pdf));
        }

        return new Project(id, fileName, [], now, now, [], []) { Document = pdf }.Fingerprinted();
    }

    /// <summary>
    /// The PDF piece with the notes the scan read from its pages (0011, 0023; testing item 16, Andrei, 2026-11-06: "the PDF
    /// is available right inside the piece"): the MusicXML becomes its source, named after the PDF, and the PDF, its marks
    /// and its bookmarks stay exactly as they were, for the "PDF" view beside the "Notes" one in the same piece.
    /// </summary>
    /// <exception cref="InvalidOperationException">The piece is not a PDF with no notes yet.</exception>
    public Project Recognised(byte[] musicXml, string engine, PageMap pages, DateTimeOffset? at = null)
    {
        ArgumentNullException.ThrowIfNull(musicXml);
        ArgumentNullException.ThrowIfNull(pages);
        if (Document is null || HasNotes)
        {
            throw new InvalidOperationException("Only a PDF with no notes yet is recognised into its own piece.");
        }

        if (musicXml.Length == 0)
        {
            throw new ArgumentException("The scan read no notes.", nameof(musicXml));
        }

        return this with
        {
            FileName = Path.ChangeExtension(FileName, ".musicxml"),
            Source = musicXml,
            Scan = ScanOrigin.Read(engine) with { From = PieceOriginal.DocumentId, At = at },
            DocumentPages = pages,
        };
    }

    /// <summary>
    /// The piece with a PDF of it kept inside it, to read as it is in the "PDF" view (testing item 16: one piece, many
    /// sources). The pages are linked to the bars roughly, <paramref name="pages"/> when the PDF's notes were read and lined
    /// up, else the bars shared evenly. A PDF kept before is replaced, and its marks with it.
    /// </summary>
    /// <exception cref="ArgumentException">The bytes are not a PDF.</exception>
    /// <exception cref="InvalidOperationException">The piece has no notes: it is a PDF piece already.</exception>
    public Project WithDocument(byte[] pdf, PageMap pages)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(pages);
        if (!PdfDocument.IsPdf(pdf))
        {
            throw new ArgumentException("The file is not a PDF.", nameof(pdf));
        }

        if (!HasNotes)
        {
            throw new InvalidOperationException("A PDF piece has its PDF already.");
        }

        bool same = Document is { } kept && kept.AsSpan().SequenceEqual(pdf);
        Project changed = this with
        {
            Document = pdf,
            DocumentPages = pages,
            DocumentMarks = same ? DocumentMarks : DocumentMarks.Empty,
            ReadFrom = null,
            KeptOriginals = same ? KeptOriginals : [.. KeptOriginals.Where(static original => original.Id != PieceOriginal.DocumentId)],
        };
        return changed.Fingerprinted();
    }

    /// <summary>
    /// The descriptions the file keeps of the piece's originals (version 0.3), as they are written: laid over the ones the
    /// piece's own data shows (<see cref="PieceOriginals.Derived"/>). Apps read <see cref="Originals"/>.
    /// </summary>
    public IReadOnlyList<PieceOriginal> KeptOriginals { get; init; } = [];

    /// <summary>
    /// The piece's originals (version 0.3, local-first): the PDF, the file the notes came from, the recordings and MIDI
    /// files of its versions, and those whose bytes are only on another device. Only their descriptions are in the file.
    /// </summary>
    public IReadOnlyList<PieceOriginal> Originals => PieceOriginals.Over(PieceOriginals.Derived(this), KeptOriginals);

    public PieceOriginal? FindOriginal(string id) => Originals.FirstOrDefault(original => original.Id == id);

    /// <summary>The original a picked file is, by its fingerprint (<see cref="PieceOriginal.Fingerprint"/>); null when none matches.</summary>
    public PieceOriginal? OriginalWithFingerprint(string sha256) => Originals.FirstOrDefault(original => original.Matches(sha256));

    /// <summary>The project with an original's description added, or put in place of the one with its id.</summary>
    public Project WithOriginal(PieceOriginal original)
    {
        ArgumentNullException.ThrowIfNull(original);
        List<PieceOriginal> kept = [.. KeptOriginals];
        int index = kept.FindIndex(each => each.Id == original.Id);
        if (index >= 0)
        {
            kept[index] = original;
        }
        else
        {
            kept.Add(original);
        }

        return this with { KeptOriginals = kept };
    }

    /// <summary>
    /// The project with a device's mark on an original set (it holds the bytes) or taken back; the same project when the
    /// mark is already so, so the apps save only on a change.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No original has the id.</exception>
    public Project MarkHeld(string id, HoldingDevice device, bool held, DateTimeOffset at)
    {
        PieceOriginal original = FindOriginal(id) ?? throw new KeyNotFoundException($"There is no original {id}.");
        return original.IsHeldBy(device) == held ? this : WithOriginal(held ? original.HeldBy(device, at) : original.NotHeldBy(device));
    }

    /// <summary>
    /// The project with the PDF's fingerprint worked out when it is not known yet (a file saved before originals were
    /// kept), once: the device that has the PDF does it, so another device can link the file the player picks again by
    /// it. The PDF is the one original in the file that never goes online; the imported file and a MIDI version go with
    /// the notes. The same project when nothing was missing.
    /// </summary>
    public Project Fingerprinted() =>
        Document is { } document && FindOriginal(PieceOriginal.DocumentId) is { Sha256: null } original
            ? WithOriginal(original with { Sha256 = PieceOriginal.Fingerprint(document), Bytes = document.Length })
            : this;

    /// <summary>
    /// The piece with its PDF linked again from a file the player picked on this device (version 0.3, «Привязать оригинал»):
    /// the bytes match the PDF's fingerprint, so the same piece gets its PDF back, its pages linked to the bars as
    /// <paramref name="pages"/> says; null when the file is another one.
    /// </summary>
    /// <exception cref="InvalidOperationException">The piece has its PDF here already, or keeps no PDF original.</exception>
    public Project? RelinkDocument(byte[] pdf, PageMap pages)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(pages);
        if (Document is not null || FindOriginal(PieceOriginal.DocumentId) is not { Kind: OriginalKind.Pdf } original)
        {
            throw new InvalidOperationException("Only a piece whose PDF is on another device links it again.");
        }

        return PdfDocument.IsPdf(pdf) && original.Matches(PieceOriginal.Fingerprint(pdf))
            ? this with { Document = pdf, DocumentPages = HasNotes ? pages : null }
            : null;
    }

    /// <summary>
    /// The copy saved online (0014): the notes and everything made from them, without the PDF, which can run to tens of
    /// megabytes and stays on the device with its marks. A piece that is only a PDF has nothing to save online. The
    /// originals' descriptions go with it (version 0.3), so another device shows where the PDF is.
    /// </summary>
    public Project? ForOnline() => Document is null ? this
        : HasNotes ? this with { Document = null, DocumentMarks = DocumentMarks.Empty, DocumentPages = null, KeptOriginals = [.. Originals] }
        : null;

    /// <summary>The PDF's file name: the piece's, as a PDF.</summary>
    public string DocumentFileName => Path.ChangeExtension(FileName, PdfDocument.Extension);

    /// <summary>The row on the projects screen (N1).</summary>
    public ProjectSummary Summary => new(Id, FileName, Motifs.Count, Variations.Count, LastOpened, ResumePoints.Count, Observations.Count)
    {
        Origin = !HasNotes ? PieceOrigin.Pdf : Scan is not null ? PieceOrigin.Scan : Transcription is not null ? PieceOrigin.Recording : PieceOrigin.File,
        CheckBars = OpenChecks.Select(flag => flag.Bar).Distinct().Count(),
        ReadFrom = ReadFrom?.ProjectId,
        Details = Details,
        Latest = RecentResumePoints.FirstOrDefault() is { } latest
            ? new ResumeSummary(latest.Id, latest.Name, latest.At.Value, latest.Passage?.Span.Onset.Value, latest.Passage?.Span.End.Value, latest.LastActive)
            : null,
    };

    /// <summary>The marks the player made on the piece (0018, section 5): ink, margin notes, highlights and fingering.</summary>
    public Marks Marks { get; init; } = Marks.Empty;

    /// <summary>The project with its marks changed by <paramref name="change"/>.</summary>
    public Project ChangeMarks(Func<Marks, Marks> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return this with { Marks = change(Marks) };
    }

    /// <summary>The bookmark categories (0018, section 5): the four built-in ones, as named here, then the player's own.</summary>
    public IReadOnlyList<BookmarkCategory> Categories { get; init; } = BookmarkCategory.BuiltIn;

    /// <summary>The player's bookmarks, in the player's order; <see cref="BookmarksInScoreOrder"/> sorts them by place.</summary>
    public IReadOnlyList<Bookmark> Bookmarks { get; init; } = [];

    public IReadOnlyList<Bookmark> BookmarksInScoreOrder => [.. Bookmarks.OrderBy(bookmark => bookmark.Span.Onset).ThenBy(bookmark => bookmark.Span.End)];

    public BookmarkCategory? FindCategory(string id) => Categories.FirstOrDefault(category => category.Id == id);

    public Bookmark? FindBookmark(string id) => Bookmarks.FirstOrDefault(bookmark => bookmark.Id == id);

    /// <summary>The project with the bookmark at the end of the player's order.</summary>
    /// <exception cref="ArgumentException">A bookmark with the same id is already there, or its category is not.</exception>
    public Project AddBookmark(Bookmark bookmark)
    {
        ArgumentNullException.ThrowIfNull(bookmark);
        if (Bookmarks.Any(kept => kept.Id == bookmark.Id))
        {
            throw new ArgumentException($"There is already a bookmark {bookmark.Id}.", nameof(bookmark));
        }

        return FindCategory(bookmark.CategoryId) is null
            ? throw new ArgumentException($"There is no bookmark category {bookmark.CategoryId}.", nameof(bookmark))
            : this with { Bookmarks = [.. Bookmarks, bookmark] };
    }

    /// <summary>The project with one bookmark changed by <paramref name="change"/>, keeping its id and place in the order.</summary>
    /// <exception cref="KeyNotFoundException">No bookmark has the id.</exception>
    /// <exception cref="ArgumentException">The change moved it to a category that is not there, or changed its id.</exception>
    public Project ChangeBookmark(string id, Func<Bookmark, Bookmark> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Bookmark changed = change(FindBookmark(id) ?? throw new KeyNotFoundException($"There is no bookmark {id}."));
        if (changed.Id != id)
        {
            throw new ArgumentException("A bookmark keeps its id.", nameof(change));
        }

        return FindCategory(changed.CategoryId) is null
            ? throw new ArgumentException($"There is no bookmark category {changed.CategoryId}.", nameof(change))
            : this with { Bookmarks = [.. Bookmarks.Select(bookmark => bookmark.Id == id ? changed : bookmark)] };
    }

    /// <summary>The project with the bookmark moved to <paramref name="index"/> in the player's order.</summary>
    /// <exception cref="KeyNotFoundException">No bookmark has the id.</exception>
    public Project MoveBookmark(string id, int index)
    {
        Bookmark moved = FindBookmark(id) ?? throw new KeyNotFoundException($"There is no bookmark {id}.");
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Bookmarks.Count);
        var order = Bookmarks.Where(bookmark => bookmark.Id != id).ToList();
        order.Insert(index, moved);
        return this with { Bookmarks = order };
    }

    /// <exception cref="KeyNotFoundException">No bookmark has the id.</exception>
    public Project RemoveBookmark(string id)
    {
        _ = FindBookmark(id) ?? throw new KeyNotFoundException($"There is no bookmark {id}.");
        return this with { Bookmarks = [.. Bookmarks.Where(bookmark => bookmark.Id != id)] };
    }

    /// <summary>
    /// The project with its bookmarks where they belong after the phrases changed (<see cref="Bookmark.Follow"/>):
    /// called with the phrases of the level a bookmark was made from, after a merge or a split.
    /// </summary>
    public Project FollowPhrases(IEnumerable<TickSpan> phrases)
    {
        TickSpan[] spans = [.. phrases ?? throw new ArgumentNullException(nameof(phrases))];
        return this with { Bookmarks = [.. Bookmarks.Select(bookmark => bookmark.Follow(spans))] };
    }

    /// <summary>
    /// The project with the detected form written into the Structure category (0018, section 5: "Structure for
    /// free"): one bookmark per sentence, "Sentence A, bars 1–8", after the player's own. A sentence already
    /// bookmarked in Structure over the same bars is left as it is, so a second call adds nothing.
    /// </summary>
    public Project AddStructureBookmarks(IEnumerable<Sentence> sentences, Words words, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sentences);
        Project project = this;
        foreach (Sentence sentence in sentences)
        {
            if (project.Bookmarks.Any(bookmark => bookmark.CategoryId == BookmarkCategory.StructureId && bookmark.Span == sentence.Span))
            {
                continue;
            }

            string bars = sentence.FirstBar == sentence.LastBar
                ? words.T($"bar {sentence.FirstBar}", $"такт {sentence.FirstBar}")
                : words.T($"bars {sentence.FirstBar}–{sentence.LastBar}", $"такты {sentence.FirstBar}–{sentence.LastBar}");
            string title = words.T($"Sentence {sentence.Letter}, {bars}", $"Предложение {sentence.Letter}, {bars}");
            project = project.AddBookmark(new Bookmark(project.NextBookmarkId(), title, BookmarkCategory.StructureId, sentence.Span, FromPhrase: false, now));
        }

        return project;
    }

    /// <summary>
    /// The project with the form of Piece DNA written into the Structure category (testing item 22, Add Structure
    /// bookmarks on the map): one bookmark per section, "Section A′, bars 17–32", after the player's own. A section already
    /// bookmarked in Structure over the same bars is left as it is, so a second call adds nothing.
    /// </summary>
    public Project AddFormBookmarks(IEnumerable<DnaSection> sections, Words words, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sections);
        Project project = this;
        foreach (DnaSection section in sections)
        {
            if (project.Bookmarks.Any(bookmark => bookmark.CategoryId == BookmarkCategory.StructureId && bookmark.Span == section.Span))
            {
                continue;
            }

            string bars = section.FirstBar == section.LastBar
                ? words.T($"bar {section.FirstBar}", $"такт {section.FirstBar}")
                : words.T($"bars {section.FirstBar}–{section.LastBar}", $"такты {section.FirstBar}–{section.LastBar}");
            string title = words.T($"Section {section.Label}, {bars}", $"Раздел {section.Label}, {bars}");
            project = project.AddBookmark(new Bookmark(project.NextBookmarkId(), title, BookmarkCategory.StructureId, section.Span, FromPhrase: false, now));
        }

        return project;
    }

    /// <summary>The project with a category of the player's own after the others.</summary>
    /// <exception cref="ArgumentException">A category with the same id is already there.</exception>
    public Project AddCategory(BookmarkCategory category)
    {
        ArgumentNullException.ThrowIfNull(category);
        return FindCategory(category.Id) is not null
            ? throw new ArgumentException($"There is already a category {category.Id}.", nameof(category))
            : this with { Categories = [.. Categories, category] };
    }

    /// <summary>The project with the category renamed; blank gives a built-in one its own name back.</summary>
    /// <exception cref="KeyNotFoundException">No category has the id.</exception>
    public Project RenameCategory(string id, string? name)
    {
        BookmarkCategory renamed = (FindCategory(id) ?? throw new KeyNotFoundException($"There is no category {id}.")).Rename(name);
        return this with { Categories = [.. Categories.Select(category => category.Id == id ? renamed : category)] };
    }

    /// <summary>
    /// The project without a category of the player's own; its bookmarks move to Practise. The built-in
    /// categories stay.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No category has the id.</exception>
    /// <exception cref="ArgumentException">The category is built in.</exception>
    public Project RemoveCategory(string id)
    {
        BookmarkCategory category = FindCategory(id) ?? throw new KeyNotFoundException($"There is no category {id}.");
        if (category.IsBuiltIn)
        {
            throw new ArgumentException("The four built-in categories stay.", nameof(id));
        }

        return this with
        {
            Categories = [.. Categories.Where(kept => kept.Id != id)],
            Bookmarks = [.. Bookmarks.Select(bookmark => bookmark.CategoryId == id ? bookmark with { CategoryId = BookmarkCategory.PractiseId } : bookmark)],
        };
    }

    /// <summary>"b_0004": one more than the highest bookmark number.</summary>
    public string NextBookmarkId() => NextId("b_", Bookmarks.Select(bookmark => bookmark.Id).Concat(UnknownIds(Unknown, "bookmarks.items")));

    /// <summary>"c_0004": one more than the highest number among the player's own categories.</summary>
    public string NextCategoryId() => NextId("c_", Categories.Select(category => category.Id).Concat(UnknownIds(Unknown, "bookmarks.categories")));

    /// <summary>Where the player left off (0018, section 9), in the order the points were made.</summary>
    public IReadOnlyList<ResumePoint> ResumePoints { get; init; } = [];

    /// <summary>The resume points most recently active first; Resume opens the first.</summary>
    public IReadOnlyList<ResumePoint> RecentResumePoints => [.. ResumePoints.OrderByDescending(point => point.LastActive).ThenByDescending(point => point.CreatedAt)];

    public ResumePoint? FindResumePoint(string id) => ResumePoints.FirstOrDefault(point => point.Id == id);

    /// <summary>The project with the resume point added after the others.</summary>
    /// <exception cref="ArgumentException">A resume point with the same id is already there.</exception>
    public Project AddResumePoint(ResumePoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return ResumePoints.Any(kept => kept.Id == point.Id)
            ? throw new ArgumentException($"The project already has a resume point {point.Id}.", nameof(point))
            : this with { ResumePoints = [.. ResumePoints, point] };
    }

    /// <summary>The project with one resume point changed by <paramref name="change"/>; the others stay as they are.</summary>
    /// <exception cref="KeyNotFoundException">No resume point has the id.</exception>
    public Project ChangeResumePoint(string id, Func<ResumePoint, ResumePoint> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        ResumePoint changed = change(FindResumePoint(id) ?? throw new KeyNotFoundException($"The project has no resume point {id}."));
        return this with { ResumePoints = [.. ResumePoints.Select(point => point.Id == id ? changed : point)] };
    }

    /// <exception cref="KeyNotFoundException">No resume point has the id.</exception>
    public Project RemoveResumePoint(string id)
    {
        _ = FindResumePoint(id) ?? throw new KeyNotFoundException($"The project has no resume point {id}.");
        return this with { ResumePoints = [.. ResumePoints.Where(point => point.Id != id)] };
    }

    /// <summary>"r_0004": one more than the highest resume point number, so ids stay stable for the life of the project.</summary>
    public string NextResumePointId() => NextId("r_", ResumePoints.Select(point => point.Id).Concat(UnknownIds(Unknown, "resumePoints")));

    /// <summary>"o_0004": one more than the highest observation number.</summary>
    public string NextObservationId() => NextId("o_", Observations.Select(observation => observation.Id).Concat(UnknownIds(Unknown, "observations")));

    /// <summary>
    /// The Check flags (0018, section 9): the doubtful bars of a scan or a transcription, with whether the player
    /// resolved each. Empty for a file the player imported. Null only for a project read from a file before
    /// version 12, until it is opened: <see cref="OpenedProject.Open"/> works the flags out from the piece then.
    /// </summary>
    public IReadOnlyList<CheckFlag>? Check { get; init; } = [];

    /// <summary>The performances compared with the piece (decision record 0022), in the order they were added.</summary>
    public IReadOnlyList<SavedPerformance> Performances { get; init; } = [];

    public SavedPerformance? FindPerformance(string id) => Performances.FirstOrDefault(performance => performance.Id == id);

    /// <summary>The next free performance id: "perf_" and one more than the highest number used.</summary>
    public string NextPerformanceId() =>
        $"perf_{Performances.Select(performance => int.TryParse(performance.Id.AsSpan(5), out int number) ? number : 0).DefaultIfEmpty(0).Max() + 1}";

    /// <exception cref="ArgumentException">A performance with the same id is already there.</exception>
    public Project AddPerformance(SavedPerformance performance)
    {
        ArgumentNullException.ThrowIfNull(performance);
        return FindPerformance(performance.Id) is not null
            ? throw new ArgumentException($"There is already a performance {performance.Id}.", nameof(performance))
            : this with { Performances = [.. Performances, performance] };
    }

    /// <exception cref="KeyNotFoundException">No performance has the id.</exception>
    public Project ChangePerformance(string id, Func<SavedPerformance, SavedPerformance> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        SavedPerformance changed = change(FindPerformance(id) ?? throw new KeyNotFoundException($"There is no performance {id}."));
        return changed.Id != id
            ? throw new ArgumentException("A performance keeps its id.", nameof(change))
            : this with { Performances = [.. Performances.Select(performance => performance.Id == id ? changed : performance)] };
    }

    /// <summary>The project without the performance, the bars taken from it and its Check flags.</summary>
    /// <exception cref="KeyNotFoundException">No performance has the id.</exception>
    public Project RemovePerformance(string id)
    {
        _ = FindPerformance(id) ?? throw new KeyNotFoundException($"There is no performance {id}.");
        List<SavedPerformance> left = [.. Performances.Where(performance => performance.Id != id)];
        return this with
        {
            Performances = left,
            KeptOriginals = [.. KeptOriginals.Where(original => original.Id != id)],
            Check = left.Count == 0 ? [.. (Check ?? []).Where(flag => flag.Source != CheckSource.Performance)] : Check,
        };
    }

    /// <summary>
    /// "Версия убрана · Вернуть" (testing item 32): a performance removed put back where it was, with the Check flags from
    /// performances the project had before it was removed.
    /// </summary>
    /// <exception cref="ArgumentException">A performance with the same id is already there.</exception>
    public Project PutBackPerformance(SavedPerformance performance, int index, IEnumerable<CheckFlag> flags)
    {
        ArgumentNullException.ThrowIfNull(performance);
        ArgumentNullException.ThrowIfNull(flags);
        if (FindPerformance(performance.Id) is not null)
        {
            throw new ArgumentException($"There is already a performance {performance.Id}.", nameof(performance));
        }

        List<SavedPerformance> all = [.. Performances];
        all.Insert(Math.Clamp(index, 0, all.Count), performance);
        return (this with { Performances = all }).WithPerformanceChecks(flags);
    }

    /// <summary>The project with a performance's Check flags (0022, section 6) in place of earlier ones from performances; flags the player resolved stay resolved.</summary>
    public Project WithPerformanceChecks(IEnumerable<CheckFlag> flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        List<CheckFlag> earlier = [.. (Check ?? []).Where(flag => flag.Source == CheckSource.Performance)];
        IEnumerable<CheckFlag> fresh = flags.Select(flag => earlier.Any(old => old.Bar == flag.Bar && old.Reason == flag.Reason && old.Resolved) ? flag with { Resolved = true } : flag);
        return this with { Check = [.. (Check ?? []).Where(flag => flag.Source != CheckSource.Performance).Concat(fresh).OrderBy(flag => flag.Bar).ThenBy(flag => flag.Reason)] };
    }

    /// <summary>The bars taken from every performance into the score.</summary>
    public IEnumerable<Performance.TakenBar> TakenBars => Performances.SelectMany(performance => performance.Taken);

    /// <summary>Whether the notes differ from the imported ones: corrections, bars taken from a performance, or an assembly applied.</summary>
    public bool ChangesNotes => !Edits.IsEmpty || TakenBars.Any() || Assembly.Applied is not null;

    /// <summary>The assembly of the player's own version from the piece's sources (decision record 0024).</summary>
    public PieceAssembly Assembly { get; init; } = PieceAssembly.Empty;

    /// <summary>The project with the assembly's journal as it now stands; nothing in the piece's notes changes.</summary>
    public Project WithJournal(IEnumerable<global::MotiffLab.MusicCore.Assembly.AssemblyStep> steps, int next) =>
        this with { Assembly = Assembly with { Steps = [.. steps], Next = next } };

    /// <summary>
    /// «Сделать нотами», the one rule that changes the piece's notes (decision record 0024, section 7): the notes the
    /// journal builds become the piece's notes. The notes before the first apply are kept as «Ноты до сборки» and stay the
    /// base the journal builds from; corrections made since the last apply stay where they still fit.
    /// </summary>
    public Project ApplyAssembly(global::MotiffLab.MusicCore.Assembly.SourceMusic notes, global::MotiffLab.MusicCore.Assembly.SourceMusic before, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(before);
        bool first = Assembly.Before is null;
        return this with
        {
            Assembly = Assembly with { Applied = notes, Before = Assembly.Before ?? before, AppliedAt = at },
            Edits = first ? NoteEditLog.Empty : PieceAssembly.Fitting(Edits, notes.Notes),
        };
    }

    /// <summary>The flags the player has not resolved, in bar order.</summary>
    public IReadOnlyList<CheckFlag> OpenChecks => [.. (Check ?? []).Where(flag => !flag.Resolved).OrderBy(flag => flag.Bar)];

    /// <summary>
    /// The project with every flag on the bar resolved, or open again. For a scan the bar is confirmed with
    /// it (<see cref="ScanOrigin.ConfirmedBars"/>), so its notes read as confirmed by the player.
    /// </summary>
    public Project ResolveCheck(int bar, bool resolved = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bar);
        if (!(Check ?? []).Any(flag => flag.Bar == bar))
        {
            throw new KeyNotFoundException($"Check has nothing about bar {bar}.");
        }

        return this with
        {
            Check = [.. (Check ?? []).Select(flag => flag.Bar == bar ? flag with { Resolved = resolved } : flag)],
            Scan = Scan is { } scan ? resolved ? scan.Confirm([bar]) : scan.Unconfirm(bar) : null,
        };
    }

    /// <summary>
    /// Whether a transcription can still be written in other bars or at another tempo (0018, section 9: the head
    /// of the Check drawer). Only before the player has put anything on the score: corrections, phrase changes,
    /// marks, bookmarks, ideas, observations and checked bars are kept by their place in the bars, which would move.
    /// </summary>
    public bool CanChangeBars => Transcription is not null && Edits.IsEmpty && !TakenBars.Any() && Phrases.IsEmpty && ShortPhrases.IsEmpty && Sentences.IsEmpty
        && Marks.IsEmpty && Bookmarks.Count == 0 && Motifs.Count == 0 && Variations.Count == 0 && Observations.Count == 0
        && Unknown.Count == 0 && Marks.Unknown.Count == 0
        && !(Check ?? []).Any(flag => flag.Resolved);

    /// <summary>
    /// The bars that fit the transcription's notes best (decision record 0016), in the signature and with a beat of
    /// as many 16ths as asked when given; null when the notes follow no beat.
    /// </summary>
    /// <exception cref="InvalidOperationException">The project is not a transcription.</exception>
    public BarGrid? FindBars(TimeSignature? signature = null, int? stepsPerBeat = null)
    {
        if (Transcription is null)
        {
            throw new InvalidOperationException("Only a transcription has bars worked out from its notes.");
        }

        MidiImport heard = PieceImporter.Import(FileName, Source).Piece?.Midi
            ?? throw new ProjectFileException("This project says it was transcribed from a recording, but its piece is not MIDI.");
        return BarGrid.Find(heard, signature, stepsPerBeat);
    }

    /// <summary>
    /// The transcription written in other bars, or as the engine wrote it when <paramref name="bars"/> is null. Its
    /// Check flags are worked out again for the new bars, and its resume points go back to the start of the piece,
    /// since every position moves.
    /// </summary>
    /// <exception cref="InvalidOperationException">The project is not a transcription, or <see cref="CanChangeBars"/> is false.</exception>
    public Project ChangeBars(BarGrid? bars)
    {
        if (Transcription is not { } heard)
        {
            throw new InvalidOperationException("Only a transcription's bars can be changed.");
        }

        if (!CanChangeBars)
        {
            throw new InvalidOperationException("The bars cannot change once something has been placed in them.");
        }

        TranscriptionOrigin origin = new(heard.Engine, bars) { Seams = heard.Seams, From = heard.From, At = heard.At };
        Project changed = this with
        {
            Transcription = origin,
            Check = [],
            ResumePoints = [.. ResumePoints.Select(point => point with { At = Tick.Zero, Passage = null, Focused = false, Loop = false })],
        };
        ImportedPiece piece = OpenedProject.Open(changed).Piece;
        return changed with { Check = CheckFlag.FromTranscription(origin, Source, piece) };
    }

    /// <summary>
    /// The piece's overrides of the settings (0018, section 10): only settings of <see cref="SettingScope.Piece"/>
    /// scope, by id, each with a value the setting allows. They travel with the project.
    /// </summary>
    public IReadOnlyDictionary<string, string> SettingOverrides { get; init; } = s_noSettings;

    private static readonly IReadOnlyDictionary<string, string> s_noSettings = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The project with the setting overridden for this piece; null clears the override.</summary>
    /// <exception cref="KeyNotFoundException">No setting has the id.</exception>
    /// <exception cref="ArgumentException">The setting cannot be overridden for a piece, or does not allow the value.</exception>
    public Project OverrideSetting(string id, string? value)
    {
        Setting setting = SettingsCatalogue.Get(id);
        if (setting.Scope != SettingScope.Piece)
        {
            throw new ArgumentException($"\"{id}\" is the same for every piece.", nameof(id));
        }

        if (value is not null && !setting.Allows(value))
        {
            throw new ArgumentException($"\"{id}\" cannot be \"{value}\"; it is one of {string.Join(", ", setting.Choices)}.", nameof(value));
        }

        var overrides = new Dictionary<string, string>(SettingOverrides, StringComparer.Ordinal);
        if (value is null)
        {
            overrides.Remove(id);
        }
        else
        {
            overrides[id] = value;
        }

        return this with { SettingOverrides = overrides };
    }

    /// <summary>The ids of the unknown entities of a list, so a new id never takes one (S6).</summary>
    internal static IEnumerable<string> UnknownIds(IEnumerable<UnknownEntity> unknown, string list) =>
        unknown.Where(entity => entity.List == list && entity.Id is not null).Select(entity => entity.Id!);

    internal static string NextId(string prefix, IEnumerable<string> ids)
    {
        int highest = ids
            .Select(id => id.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(id.AsSpan(prefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{prefix}{highest + 1:0000}");
    }

    /// <summary>What the player kept about the piece's phrases (#78), the ideas, in the order it was kept.</summary>
    public IReadOnlyList<Observation> Observations { get; init; } = [];

    /// <summary>The observations about the phrase, in the order they were kept (<see cref="Observation.IsAbout"/>).</summary>
    public IReadOnlyList<Observation> ObservationsAbout(TickSpan phrase) => [.. Observations.Where(observation => observation.IsAbout(phrase))];

    /// <summary>The project with the observation kept after the others.</summary>
    /// <exception cref="ArgumentException">An observation with the same id is already kept.</exception>
    public Project Keep(Observation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return Observations.Any(kept => kept.Id == observation.Id)
            ? throw new ArgumentException($"The project already keeps an observation {observation.Id}.", nameof(observation))
            : this with { Observations = [.. Observations, observation] };
    }

    /// <summary>The project with one observation renamed; the others stay as they are.</summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    /// <exception cref="KeyNotFoundException">No observation has the id.</exception>
    public Project RenameObservation(string id, string name)
    {
        Observation renamed = FindObservation(id).Rename(name);
        return this with { Observations = [.. Observations.Select(kept => kept.Id == id ? renamed : kept)] };
    }

    /// <summary>The project without the observation; the others keep their order.</summary>
    /// <exception cref="KeyNotFoundException">No observation has the id.</exception>
    public Project DeleteObservation(string id)
    {
        _ = FindObservation(id);
        return this with { Observations = [.. Observations.Where(kept => kept.Id != id)] };
    }

    /// <summary>The project with the observation's note changed to <paramref name="note"/>; blank takes the note away.</summary>
    /// <exception cref="KeyNotFoundException">No observation has the id.</exception>
    /// <exception cref="ArgumentException">The note is blank and the observation keeps no variation.</exception>
    public Project ChangeObservationNote(string id, string? note)
    {
        Observation changed = FindObservation(id).WithNote(note);
        return this with { Observations = [.. Observations.Select(kept => kept.Id == id ? changed : kept)] };
    }

    private Observation FindObservation(string id) =>
        Observations.FirstOrDefault(observation => observation.Id == id) ?? throw new KeyNotFoundException($"The project keeps no observation {id}.");
}

/// <summary>
/// A project's card in the library (0018, section 9): the piece, its resume points and ideas, when it was last opened,
/// where it came from, how many bars Check still asks about, and the latest resume point, without loading the project.
/// </summary>
public sealed record ProjectSummary(string Id, string FileName, int MotifCount, int VariationCount, DateTimeOffset LastOpened, int ResumeCount = 0, int IdeaCount = 0)
{
    /// <summary>A file, a scan or a recording; summaries saved before it was kept read as a file.</summary>
    public PieceOrigin Origin { get; init; }

    /// <summary>The bars with Check flags not yet resolved.</summary>
    public int CheckBars { get; init; }

    /// <summary>The resume point worked at last, or null when there is none (or the summary was saved before it was kept).</summary>
    public ResumeSummary? Latest { get; init; }

    /// <summary>For notes read from a PDF piece, that piece's id (0023), so the PDF view finds its notes; null otherwise.</summary>
    public string? ReadFrom { get; init; }

    /// <summary>The title, composer and tags the card shows (<see cref="Project.Details"/>); null for a summary saved before they were kept.</summary>
    public PieceDetails? Details { get; init; }
}

/// <summary>Where a piece came from: a file opened on the device, a page scanned (0011) or a recording heard (0014).</summary>
public enum PieceOrigin
{
    File,
    Scan,
    Recording,

    /// <summary>A PDF added to read as it is, with no notes (0023).</summary>
    Pdf,
}

/// <summary>A resume point as its piece's card shows it: its name, the position and passage in ticks, and when it was last active.</summary>
public sealed record ResumeSummary(string Id, string? Name, long At, long? From, long? To, DateTimeOffset LastActive);

/// <summary>A motif as saved: what the player picked, from which the motif is rebuilt on open.</summary>
/// <param name="NoteIds">The selected notes, checked against the source when the project opens.</param>
public sealed record SavedMotif(
    string Id,
    string Name,
    MotifSelectionKind Kind,
    MotifMode Mode,
    TickSpan Span,
    SelectionFilter Filter,
    IReadOnlyList<string> NoteIds)
{
    public static SavedMotif From(Motif motif)
    {
        ArgumentNullException.ThrowIfNull(motif);
        return new(motif.Id, motif.Name, motif.Kind, motif.Mode, motif.Span, motif.Filter, motif.NoteIds);
    }
}

/// <summary>One step of a saved variation: the operation's catalogue id and its parameters.</summary>
public sealed record SavedStep(string Operation, OperationParameters Parameters)
{
    public static SavedStep From(VariationStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return new(step.Operation.Id, step.Parameters);
    }
}

/// <summary>
/// A variation as saved: the chain of steps that made it from a motif (S1-E5). Steps are pure, so
/// they are run again on open. Undone steps are not saved.
/// </summary>
/// <param name="Steps">The accepted steps, oldest first.</param>
/// <param name="Pending">The step that was being heard and not yet accepted or rejected, or null.</param>
/// <param name="Keep">The properties the player asked to keep, in Keep table order.</param>
public sealed record SavedVariation(
    string Id,
    string MotifId,
    IReadOnlyList<SavedStep> Steps,
    SavedStep? Pending,
    IReadOnlyList<KeepProperty> Keep)
{
    /// <summary>
    /// The key the steps read, when the chain had one, so a step that counts in the key's scale (passing notes) can be
    /// made again on opening; null for a file that has none, which opens as before.
    /// </summary>
    public MusicalKey? Key { get; init; }

    public static SavedVariation Create(string id, string motifId, VariationChain chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        return new(id, motifId, [.. chain.Steps.Select(SavedStep.From)], chain.Pending is { } pending ? SavedStep.From(pending) : null, [.. chain.Context.Keep.Distinct().Order()])
        {
            Key = chain.Context.Key,
        };
    }
}

/// <summary>A project opened for work: the source imported again, the edit log applied, and its motifs and variations rebuilt.</summary>
/// <param name="Project">The project, with its Check flags worked out when the file it came from had none (before version 12).</param>
/// <param name="Notes">The piece's notes with the project's corrections applied; <see cref="ImportedPiece.Notes"/> stays as imported.</param>
public sealed record OpenedProject(
    Project Project,
    ImportedPiece Piece,
    IReadOnlyList<Note> Notes,
    IReadOnlyList<Motif> Motifs,
    IReadOnlyList<(SavedVariation Saved, VariationChain Chain)> Variations)
{
    /// <summary>
    /// Imports the project's source and rebuilds what was made from it. Throws
    /// <see cref="ProjectFileException"/> when the source no longer imports or no longer matches.
    /// </summary>
    public static OpenedProject Open(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!project.HasNotes)
        {
            throw new ProjectFileException("This piece is a PDF read as it is: it has no notes to open.");
        }

        PieceImportResult imported = PieceImporter.Import(project.FileName, project.Source);
        if (imported.Piece is not { } piece)
        {
            throw new ProjectFileException($"The piece in this project cannot be read: {imported.Rejection?.Message}");
        }

        if (project.Scan is { } scan)
        {
            if (piece.MusicXml is null)
            {
                throw new ProjectFileException("This project says it was scanned, but its piece is not MusicXML.");
            }

            piece = ScanCheck.Of(piece).Mark(piece, scan.ConfirmedBars);
        }

        if (project.Transcription is not null)
        {
            if (piece.Midi is null)
            {
                throw new ProjectFileException("This project says it was transcribed from a recording, but its piece is not MIDI.");
            }

            if (project.Transcription.Bars is { } bars)
            {
                try
                {
                    piece = bars.Apply(piece);
                }
                catch (ArgumentException exception)
                {
                    throw new ProjectFileException($"The bars worked out for this transcription no longer fit its notes: {exception.Message}");
                }
            }

            piece = Transcribed.Mark(piece);
        }

        piece = project.Assembly.Over(piece);
        bool assembled = project.Assembly.Applied is not null;
        IReadOnlyList<Note> notes;
        try
        {
            notes = assembled ? project.Edits.Apply(piece.Notes) : Performance.PerformanceTake.Apply(project.Edits.Apply(piece.Notes), project.TakenBars);
        }
        catch (InvalidOperationException exception)
        {
            throw new ProjectFileException($"The note corrections in this project no longer fit the piece: {exception.Message}");
        }

        var motifs = new List<Motif>();
        foreach (SavedMotif saved in project.Motifs)
        {
            MotifSelection selection = saved.Kind == MotifSelectionKind.Notes
                ? MotifSelector.SelectNotes(notes, piece.Meter, saved.NoteIds)
                : MotifSelector.Select(notes, piece.Meter, saved.Span, saved.Filter, MotifSelection.MaxStructureBars);
            if (!selection.NoteIds.SequenceEqual(saved.NoteIds, StringComparer.Ordinal))
            {
                if (assembled)
                {
                    // An applied assembly may have changed the motif's notes (decision record 0024, section 7): it leaves the list.
                    continue;
                }

                throw new ProjectFileException($"\"{saved.Name}\" no longer matches the notes of the piece.");
            }

            motifs.Add(Motif.Create(saved.Id, motifs.Count + 1, selection, saved.Kind, saved.Mode, notes) with { Name = saved.Name });
        }

        var variations = new List<(SavedVariation, VariationChain)>();
        foreach (SavedVariation saved in project.Variations)
        {
            Motif? found = motifs.FirstOrDefault(motif => motif.Id == saved.MotifId);
            if (found is null && assembled)
            {
                continue;
            }

            Motif motif = found
                ?? throw new ProjectFileException($"A variation refers to a motif ({saved.MotifId}) that is not in the project.");
            VariationChain chain = VariationChain.Start(
                VariationFacts.MotifNotes(notes, motif.Selection),
                VariationContext.For(motif.Selection, saved.Keep.ToHashSet(), key: saved.Key));
            foreach (SavedStep step in saved.Steps)
            {
                chain = Propose(chain, step).Accept();
            }

            if (saved.Pending is { } pending)
            {
                chain = Propose(chain, pending);
            }

            variations.Add((saved, chain));
        }

        var observations = new List<(Observation, VariationChain?)>();
        foreach (Observation observation in project.Observations)
        {
            observations.Add((observation, observation.Restore(notes, piece.Meter)));
        }

        if (project.Check is null)
        {
            // A file from before version 12: the Check flags are worked out from the piece, a scan's resolved where the player confirmed the bar.
            project = project with
            {
                Check = project.Scan is { } scanned ? CheckFlag.FromScan(ScanCheck.Of(piece), scanned.ConfirmedBars)
                    : project.Transcription is { } heard ? CheckFlag.FromTranscription(heard, project.Source, piece)
                    : [],
            };
        }

        return new OpenedProject(project, piece, notes, motifs, variations) { Observations = observations };
    }

    /// <summary>The observations, in the project's order, each with its variation's chain rebuilt; null for a note alone.</summary>
    public IReadOnlyList<(Observation Saved, VariationChain? Chain)> Observations { get; init; } = [];

    /// <summary>A saved step proposed on the chain again; shared with <see cref="Observation.Restore"/>.</summary>
    internal static VariationChain Propose(VariationChain chain, SavedStep step)
    {
        IVariationOperation operation = VariationOperations.Find(step.Operation)
            ?? throw new ProjectFileException($"A variation uses an operation ({step.Operation}) this version of Motiff Lab does not have.");
        if (chain.UnavailableReason(operation, step.Parameters) is { } reason)
        {
            throw new ProjectFileException($"A variation can no longer be made: {reason}");
        }

        return chain.Propose(operation, step.Parameters);
    }
}

/// <summary>A project file or saved project that cannot be opened. The message is a plain reason for the player.</summary>
public sealed class ProjectFileException(string message) : Exception(message);