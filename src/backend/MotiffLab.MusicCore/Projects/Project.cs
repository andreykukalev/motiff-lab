using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Settings;
using MotiffLab.MusicCore.Time;

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
public sealed partial record Project(
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
    public Import.Transcription.TranscriptionOrigin? Transcription { get; init; }

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

    /// <summary>
    /// Where the piece is inside the collection it was made from (version 0.4, «Пьесы в файле», within version 15): the
    /// collection's original and the pages or the stretch of the recording; null for a piece of its own.
    /// </summary>
    public PartOf? PartOf { get; init; }

    /// <summary>
    /// Whether the player turned the corrections off for this piece (version 0.5, «Мои правки: вкл / выкл», within version
    /// 15): the piece opens with the notes as imported, its edit log kept. A build before 0.5 opens it with the corrections
    /// on and drops the field when it writes the file.
    /// </summary>
    public bool EditsOff { get; init; }

    /// <summary>
    /// The tempo the player set for the piece, in quarter notes a minute (version 0.5.4, #511 section 17, within version
    /// 15), such as "♩ = 92" from the printed notes when the scan could not read it: the piece plays and is marked at it
    /// from its start (<see cref="Playback.PlayerTempo"/>); null to play as the piece states. A build before 0.5.4 plays
    /// the piece as it states and drops the field when it writes the file.
    /// </summary>
    public int? PlayerTempo { get; init; }

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
    public Project Recognised(byte[] musicXml, string engine, PageMap pages, DateTimeOffset? at = null) => Recognised(musicXml, engine, pages, at, null);

    /// <summary>
    /// The PDF piece with the notes the scan read from its pages, as <see cref="Recognised(byte[], string, PageMap, DateTimeOffset?)"/>,
    /// and with <paramref name="reports"/>, the scan server's report on each page of the notes (its <c>X-Page-Report</c>,
    /// version 0.4), kept by the scan (<see cref="ScanOrigin.Pages"/>) numbered by their place in the notes, so its check
    /// (<see cref="ScanOrigin.CheckOf"/>) counts each page's quality on every open; the caller raises the Check flags from it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The piece is not a PDF with no notes yet.</exception>
    /// <exception cref="ArgumentException">No notes, or not one report per page of the notes (<see cref="PageReport.ForScore"/>).</exception>
    public Project Recognised(byte[] musicXml, string engine, PageMap pages, DateTimeOffset? at, IReadOnlyList<PageReport>? reports)
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
            Scan = ScanOrigin.Read(engine) with { From = PieceOriginal.DocumentId, At = at, Pages = reports is null ? [] : PageReport.ForScore(reports, musicXml) },
            DocumentPages = pages,
        };
    }

    /// <summary>
    /// The piece with a PDF of it kept inside it, to read as it is in the "PDF" view (testing item 16: one piece, many
    /// sources). The pages are linked to the bars roughly, <paramref name="pages"/> when the PDF's notes were read and lined
    /// up, else the bars shared evenly. A PDF kept before is replaced, and its marks with it; Notes read from it keep its
    /// fingerprint (version 16), so they show the file changed.
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
        // Version 16 (0030, section 4): the Notes keep the fingerprint of the PDF they were read from, so another PDF in its
        // place shows them as "file changed" instead of taking them over.
        Project changed = (same ? this : Fingerprinted().WithProvenanceKept()) with
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
    /// The copy saved online (0014): the notes and everything made from them, without the PDF, which can run to tens of
    /// megabytes and stays on the device. The originals' descriptions go with it (version 0.3), so another device shows
    /// where the PDF is, and so do the marks on the PDF's pages (version 0.4; Andrei, 2026-11-12), so another device that
    /// has the same PDF shows them. A piece that is only a PDF goes online too (version 0.5; Andrei, 2026-11-12, amending
    /// 0026 C3): its title, composer, tags, playlists, the PDF's description with its fingerprint and the page marks,
    /// without the PDF, so another device lists it and opens it once the same file is linked there. Never null since 0.5.
    /// </summary>
    /// <remarks>The bytes of the other originals kept in the file (version 16) stay on the device as the PDF does.</remarks>
    public Project? ForOnline()
    {
        Project copy = Sources.Data.Count == 0 ? this : this with { Sources = Sources with { Data = SourcesLayer.None.Data } };
        return copy.Document is null ? copy : WithoutDocument(copy.HasNotes ? copy : copy.Fingerprinted());
    }

    /// <summary>
    /// Whether <paramref name="device"/> writes the marks on the PDF's pages (version 0.4; Andrei, 2026-11-12: only a
    /// device that has the PDF writes them): it has the PDF, and it is the iPad, or the iPad does not hold the PDF. So the
    /// marks have one writer; another device that has the same PDF shows them as the online copy brings them, read only.
    /// With no device named, a device that has the PDF writes them, as before.
    /// </summary>
    public bool WritesDocumentMarks(HoldingDevice? device) =>
        Document is not null
        && (device != HoldingDevice.Web || FindOriginal(PieceOriginal.DocumentId) is not { } pdf || !pdf.IsHeldBy(HoldingDevice.Ipad));

    /// <summary>The PDF's file name: the piece's, as a PDF.</summary>
    public string DocumentFileName => Path.ChangeExtension(FileName, PdfDocument.Extension);

    /// <summary>The row on the projects screen (N1).</summary>
    public ProjectSummary Summary => new(Id, FileName, Motifs.Count, Variations.Count, LastOpened, ResumePoints.Count, Observations.Count)
    {
        Origin = IsOnlyRecording ? PieceOrigin.Audio : !HasNotes ? PieceOrigin.Pdf : Scan is not null ? PieceOrigin.Scan : Transcription is not null ? PieceOrigin.Recording : PieceOrigin.File,
        CheckBars = OpenChecks.Select(flag => flag.Bar).Distinct().Count(),
        ReadFrom = ReadFrom?.ProjectId,
        Details = Details,
        Originals = OriginalSummaries,
        Part = FindOriginal(PartOf?.Original ?? PieceParts.OriginalId) is { } collection ? new PartSummary(collection, PartOf) : null,
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

    /// <summary>"o_0004": one more than the highest observation number.</summary>
    public string NextObservationId() => NextId("o_", Observations.Select(observation => observation.Id).Concat(UnknownIds(Unknown, "observations")));

    /// <summary>
    /// The Check flags (0018, section 9): the doubtful bars of a scan or a transcription, with whether the player
    /// resolved each. Empty for a file the player imported. Null only for a project read from a file before
    /// version 12, until it is opened: <see cref="OpenedProject.Open"/> works the flags out from the piece then.
    /// </summary>
    public IReadOnlyList<CheckFlag>? Check { get; init; } = [];

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

        Import.Transcription.TranscriptionOrigin origin = new(heard.Engine, bars) { Seams = heard.Seams, From = heard.From, At = heard.At };
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