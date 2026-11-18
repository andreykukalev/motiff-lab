using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Pieces;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// The operations on a piece's originals and Notes (decision record 0030, section 4): add an original, revive a removed
/// one with the same file, remove one with or without its notes, recognise again beside the notes there are, remove Notes,
/// and make other Notes the working ones (Andrei, 2026-11-14, amending section 1). Each is pure: the same piece and
/// arguments give the same piece. Nothing here touches a file of the player's: a linked original is only unlinked.
/// </summary>
public static class SourceOperations
{
    /// <summary>
    /// A free id for a new original: <paramref name="stem"/> itself ("src_pdf") while the piece uses it nowhere (an original,
    /// a removed one, a version, an unknown original), else the stem with the first free number ("src_pdf_2").
    /// </summary>
    public static string NextOriginalId(this Project project, string stem)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(stem);
        HashSet<string> taken = TakenOriginalIds(project);
        if (!taken.Contains(stem))
        {
            return stem;
        }

        for (int number = 2; ; number++)
        {
            string candidate = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{stem}_{number}");
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static HashSet<string> TakenOriginalIds(Project project) =>
    [
        .. project.Originals.Select(static original => original.Id),
        .. project.Sources.Removed.Select(static removed => removed.Original.Id),
        .. project.Performances.Select(static performance => performance.Id),
        .. Project.UnknownIds(project.Unknown, "originals"),
    ];

    /// <summary>
    /// The piece with an original added without recognising it (0030, section 4): a PDF (any number of them), photos, a
    /// recording, MIDI or MusicXML. Its fingerprint and size are worked out from <paramref name="data"/> when the
    /// description has none. Kept <see cref="OriginalKept.File"/>, the bytes go into the file (the first PDF as
    /// <see cref="Project.Document"/>); kept on the device or linked, only the description does. The same file as a removed
    /// original (the same fingerprint) revives it under its own id, with the marks on its pages; a revived first PDF of a
    /// piece with notes needs <paramref name="pages"/>, where its pages start in the working notes.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The id is taken, the first PDF's bytes are not a PDF, a file kept in the file has no bytes, or a revived first PDF
    /// of a piece with notes has no pages.
    /// </exception>
    public static Project AddOriginal(this Project project, PieceOriginal original, byte[]? data, PageMap? pages = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(original);
        if (data is not null)
        {
            original = original with { Sha256 = original.Sha256 ?? PieceOriginal.Fingerprint(data), Bytes = original.Bytes ?? data.Length };
        }

        (project, RemovedOriginal? tombstone) = Reviving(project, ref original);
        if (tombstone is null && TakenOriginalIds(project).Contains(original.Id))
        {
            throw new ArgumentException($"The piece has an original {original.Id} already.", nameof(original));
        }

        if (original.Kept == OriginalKept.File && data is null)
        {
            throw new ArgumentException("An original kept in the file comes with its bytes.", nameof(data));
        }

        bool firstPdf = original.Id == PieceOriginal.DocumentId && original.Kept == OriginalKept.File;
        if (firstPdf)
        {
            return WithFirstPdf(project, original, data!, tombstone?.Marks ?? DocumentMarks.Empty, pages);
        }

        project = original.Kept == OriginalKept.File ? project with { Sources = project.Sources with { Data = With(project.Sources.Data, original.Id, data!) } } : project;
        project = project.WithOriginal(original);
        return tombstone is null ? project : project.WithPageMarks(original.Id, tombstone.Marks);
    }

    /// <summary>
    /// The marks on the pages of the original <paramref name="id"/>: the first PDF's (<see cref="Project.DocumentMarks"/>), another
    /// PDF's or photos' (version 0.6); none for an original without them.
    /// </summary>
    public static DocumentMarks PageMarksOf(this Project project, string id)
    {
        ArgumentNullException.ThrowIfNull(project);
        return id == PieceOriginal.DocumentId ? project.DocumentMarks : project.Sources.Marks.GetValueOrDefault(id) ?? DocumentMarks.Empty;
    }

    /// <summary>The piece with the marks on the pages of the original <paramref name="id"/> in place of the ones it had.</summary>
    /// <exception cref="KeyNotFoundException">No live original has the id.</exception>
    public static Project WithPageMarks(this Project project, string id, DocumentMarks marks)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(marks);
        _ = project.FindOriginal(id) ?? throw new KeyNotFoundException($"There is no original {id}.");
        if (id == PieceOriginal.DocumentId)
        {
            return project with { DocumentMarks = marks };
        }

        Dictionary<string, DocumentMarks> all = new(project.Sources.Marks.Where(pair => pair.Key != id), StringComparer.Ordinal);
        if (!marks.IsEmpty || marks.Unknown.Count > 0)
        {
            all[id] = marks;
        }

        return project with { Sources = project.Sources with { Marks = all } };
    }

    /// <summary>The piece without the tombstone of the same file as <paramref name="original"/>, which takes its id and name; the tombstone, or null when none.</summary>
    private static (Project Project, RemovedOriginal? Tombstone) Reviving(Project project, ref PieceOriginal original)
    {
        string? sha = original.Sha256;
        RemovedOriginal? tombstone = sha is null ? null : project.Sources.Removed.FirstOrDefault(removed => removed.Original.Matches(sha));
        if (tombstone is null)
        {
            return (project, null);
        }

        original = (original.Id == tombstone.Original.Id ? original : PieceOriginals.Renamed(original, tombstone.Original.Id)) with { Name = original.Name ?? tombstone.Original.Name };
        return (project with { Sources = project.Sources with { Removed = [.. project.Sources.Removed.Where(removed => removed != tombstone)] } }, tombstone);
    }

    private static Project WithFirstPdf(Project project, PieceOriginal original, byte[] pdf, DocumentMarks marks, PageMap? pages)
    {
        if (!PdfDocument.IsPdf(pdf))
        {
            throw new ArgumentException("The file is not a PDF.", nameof(pdf));
        }

        if (project.HasNotes && pages is null)
        {
            throw new ArgumentException("The PDF of a piece with notes comes with where its pages start in them.", nameof(pages));
        }

        // A page map kept as the file held it while the PDF was gone gives way to the one given now.
        Dictionary<string, IReadOnlyList<UnknownField>> fields = new(project.UnknownFields, StringComparer.Ordinal);
        foreach (string path in fields.Keys.Where(static path => path.StartsWith(ProjectFile.NotesPath, StringComparison.Ordinal)).ToList())
        {
            fields[path] = [.. fields[path].Where(static field => field.Name != "pageMap")];
        }

        return project with
        {
            Document = pdf,
            DocumentMarks = marks,
            DocumentPages = project.HasNotes ? pages : null,
            UnknownFields = fields,
            KeptOriginals = [.. project.KeptOriginals.Where(static kept => kept.Id != PieceOriginal.DocumentId), original],
        };
    }

    /// <summary>
    /// The piece with an original removed (0030, section 4). By default its notes stay: the bytes leave the piece and a
    /// tombstone keeps its description and the marks on its pages, so the Notes made from it show it removed and the same
    /// file added again revives it. <paramref name="withNotes"/> removes the Notes made from it too, with their corrections.
    /// A linked original is only unlinked: the player's own file is never touched.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No original has the id.</exception>
    /// <exception cref="InvalidOperationException">
    /// The original is the only thing a piece with no notes has (remove the piece instead), or it is to go with its notes
    /// and the working notes were made from it.
    /// </exception>
    public static Project RemoveOriginal(this Project project, string id, DateTimeOffset at, bool withNotes = false)
    {
        ArgumentNullException.ThrowIfNull(project);
        PieceOriginal original = project.FindOriginal(id) ?? throw new KeyNotFoundException($"There is no original {id}.");
        if (!project.HasNotes && project.Sources.Notes.Count == 0 && id == PieceOriginal.DocumentId)
        {
            throw new InvalidOperationException("A piece that is only its PDF is removed as a piece.");
        }

        if (withNotes && project.HasNotes && MadeFrom(project.WorkingProvenance, id))
        {
            throw new InvalidOperationException("The working notes were made from this original; they cannot be removed while they are working.");
        }

        Project removed = Gone(project, original.Id) with
        {
            HiddenViews = [.. project.HiddenViews.Where(view => view != id)],
        };
        RemovedOriginal tombstone = new(original, at) { Marks = project.PageMarksOf(id) };
        removed = removed with { Sources = removed.Sources with { Removed = [.. removed.Sources.Removed.Where(each => each.Original.Id != id), tombstone] } };
        return withNotes ? NotesFromGone(removed, id) : removed;
    }

    /// <summary>
    /// The piece without an original's description and bytes, as its tombstone leaves it: the first PDF's bytes, page map
    /// and marks go with it (the tombstone keeps the marks), and the page maps other Notes have of it.
    /// </summary>
    internal static Project Gone(Project project, string id)
    {
        Project gone = project with
        {
            KeptOriginals = [.. project.KeptOriginals.Where(kept => kept.Id != id)],
            Sources = project.Sources with
            {
                Data = Without(project.Sources.Data, id),
                Marks = Without(project.Sources.Marks, id),
                Notes = id == PieceOriginal.DocumentId ? [.. project.Sources.Notes.Select(static notes => notes with { Pages = null })] : project.Sources.Notes,
            },
        };
        return id == PieceOriginal.DocumentId ? gone with { Document = null, DocumentMarks = DocumentMarks.Empty, DocumentPages = null } : gone;
    }

    /// <summary>Whether Notes of this <paramref name="provenance"/> were made from the original <paramref name="id"/> of their own piece.</summary>
    public static bool MadeFrom(NotesProvenance provenance, string id)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        return provenance.From.Any(source => source.Project is null && source.Original == id);
    }

    private static Project NotesFromGone(Project project, string id)
    {
        foreach (OtherNotes notes in project.Sources.Notes.Where(notes => MadeFrom(notes.Provenance, id)).ToList())
        {
            project = project.DeleteNotes(notes.Id);
        }

        foreach (SavedPerformance performance in project.Performances.Where(performance => MadeFrom(project.ProvenanceOf(performance), id) || performance.Id == id).ToList())
        {
            project = project.DeleteNotes(performance.Id);
        }

        return project;
    }

    /// <summary>
    /// The piece without the Notes <paramref name="id"/> (0030, section 4): other Notes or a compared version, with their
    /// corrections, and the motifs, variations and ideas made on them while they were working; the originals stay.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The piece has no Notes with the id.</exception>
    /// <exception cref="InvalidOperationException">They are the working notes.</exception>
    public static Project DeleteNotes(this Project project, string id)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.HasNotes && id == project.NotesId)
        {
            throw new InvalidOperationException("The working notes cannot be removed while they are working.");
        }

        Project left;
        if (project.Sources.Notes.Any(notes => notes.Id == id))
        {
            left = project with { Sources = project.Sources with { Notes = [.. project.Sources.Notes.Where(notes => notes.Id != id)] } };
        }
        else if (project.FindPerformance(id) is not null)
        {
            // The version's recording or MIDI file stays an original of the piece.
            PieceOriginal? original = project.FindOriginal(id);
            left = project.RemovePerformance(id);
            left = original is null ? left : left.WithOriginal(original);
        }
        else
        {
            throw new KeyNotFoundException($"There are no Notes {id}.");
        }

        HashSet<string> motifs = [.. left.Motifs.Where(motif => motif.Notes == id).Select(static motif => motif.Id)];
        return left with
        {
            HiddenViews = [.. left.HiddenViews.Where(view => view != id)],
            Motifs = [.. left.Motifs.Where(motif => !motifs.Contains(motif.Id))],
            Variations = [.. left.Variations.Where(variation => !motifs.Contains(variation.MotifId))],
            Observations = [.. left.Observations.Where(observation => observation.Notes != id)],
            UnknownFields = Without(left.UnknownFields, ProjectFile.NotesPath + id),
        };
    }

    /// <summary>
    /// The piece with the notes a scan read again from one of its PDFs (0030, section 4: another engine, other parameters)
    /// as other Notes beside the working ones, which keep their corrections; the scan server's page reports go with them as
    /// with <see cref="Project.Recognised(byte[], string, PageMap, DateTimeOffset?, IReadOnlyList{PageReport}?)"/>. Where the
    /// pages start in them is kept for the first PDF only, as the piece's PDF view reads it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The piece has no working notes yet: it is recognised into them instead.</exception>
    /// <exception cref="KeyNotFoundException">No original has the id.</exception>
    /// <exception cref="ArgumentException">No notes, or not one report per page of the notes.</exception>
    public static Project RecognisedAgain(this Project project, string original, byte[] musicXml, string engine, PageMap? pages, DateTimeOffset at, IReadOnlyList<PageReport>? reports = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(musicXml);
        if (!project.HasNotes)
        {
            throw new InvalidOperationException("A piece with no notes yet is recognised into its working notes.");
        }

        PieceOriginal read = project.FindOriginal(original) ?? throw new KeyNotFoundException($"There is no original {original}.");
        if (musicXml.Length == 0)
        {
            throw new ArgumentException("The scan read no notes.", nameof(musicXml));
        }

        string id = NextNotesId(project);
        var provenance = new NotesProvenance(NotesProvenance.PdfRecognition) { From = [new ProvenanceSource(read.Id) { Sha256 = read.Sha256 }], Engine = engine, At = at };
        var notes = new OtherNotes(id, Path.ChangeExtension(project.FileName, ".musicxml"), musicXml, provenance)
        {
            Check = null,
            Scan = ScanOrigin.Read(engine) with { From = read.Id, At = at, Pages = reports is null ? [] : PageReport.ForScore(reports, musicXml) },
            Pages = read.Id == PieceOriginal.DocumentId ? pages : null,
        };
        return project.AddNotes(notes);
    }

    /// <summary>
    /// The piece with the notes a transcription heard in one of its recordings (0030, section 4, «Распознать» on a recording
    /// already added): with <paramref name="stretch"/>, the part of it sent («Что распознать»), in milliseconds from its
    /// start. A piece with no notes yet takes them as its working notes; otherwise they are other Notes beside the working
    /// ones, which keep their corrections. Marked heard by <paramref name="origin"/>, with the Check flags it raises.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No original has the id.</exception>
    /// <exception cref="ArgumentException">The original is not a recording, or no notes were heard.</exception>
    public static Project HeardFrom(this Project project, string original, byte[] midi, TranscriptionOrigin origin, IReadOnlyList<CheckFlag> check, DateTimeOffset at, (long From, long To)? stretch = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(midi);
        ArgumentNullException.ThrowIfNull(origin);
        PieceOriginal heard = project.FindOriginal(original) ?? throw new KeyNotFoundException($"There is no original {original}.");
        if (heard.Kind != OriginalKind.Audio || midi.Length == 0)
        {
            throw new ArgumentException(midi.Length == 0 ? "No notes were heard." : $"{original} is not a recording.", nameof(original));
        }

        TranscriptionOrigin from = origin with { From = heard.Id, At = at };
        var provenance = new NotesProvenance(NotesProvenance.AudioRecognition)
        {
            From = [new ProvenanceSource(heard.Id) { Sha256 = heard.Sha256, Milliseconds = stretch }],
            Engine = origin.Engine,
            At = at,
        };
        string fileName = Path.ChangeExtension(project.FileName, ".mid");
        if (!project.HasNotes)
        {
            return project with { FileName = fileName, Source = midi, Transcription = from, Check = [.. check], KeptProvenance = provenance, DocumentPages = null };
        }

        return project.AddNotes(new OtherNotes(project.NextNotesId(), fileName, midi, provenance) { Transcription = from, Check = [.. check] });
    }

    /// <summary>The piece with other Notes added at the end of its Notes.</summary>
    /// <exception cref="ArgumentException">The piece has Notes with the id already.</exception>
    public static Project AddNotes(this Project project, OtherNotes notes)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(notes);
        bool taken = notes.Id == project.NotesId || project.Sources.Notes.Any(each => each.Id == notes.Id) || project.FindPerformance(notes.Id) is not null
            || Project.UnknownIds(project.Unknown, "notes").Contains(notes.Id, StringComparer.Ordinal);
        return taken
            ? throw new ArgumentException($"The piece has Notes {notes.Id} already.", nameof(notes))
            : project with { Sources = project.Sources with { Notes = [.. project.Sources.Notes, notes] } };
    }

    /// <summary>
    /// The next free Notes id: "n_" and one more than the highest number the piece's Notes use, the working ones, the other
    /// ones and those kept unknown, as the first is "n_1".
    /// </summary>
    public static string NextNotesId(this Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        IEnumerable<string> ids = [project.NotesId, .. project.Sources.Notes.Select(static notes => notes.Id), .. Project.UnknownIds(project.Unknown, "notes")];
        int highest = ids.Select(static id => id.StartsWith("n_", StringComparison.Ordinal)
                && int.TryParse(id.AsSpan(2), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int number) ? number : 0)
            .DefaultIfEmpty(0).Max();
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"n_{highest + 1}");
    }

    /// <summary>
    /// The piece with other Notes made the working notes (Andrei, 2026-11-14: «Сделать рабочими» on the iPad, amending
    /// 0030, section 1): the working notes until now stay as other Notes with everything that is theirs (corrections, Check
    /// flags, scan or transcription, page map), so they can be made working again with nothing lost. The work stays in
    /// ticks: phrases, bookmarks, resume points and ideas without a variation stay with the piece; the marks on the score
    /// stay with the notes they were drawn on (sources sheet, 2026-11-14). Motifs, their
    /// variations and kept variations are made on particular notes: those of the notes until now wait with them, kept in the
    /// file and hidden while the piece opens, and those of the Notes made working come back. The Check flags of the
    /// comparisons with versions stay with the piece. The piece's title and name stay; only the music's kind (MusicXML or
    /// MIDI) follows the new working notes.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The piece has no other Notes with the id (a compared version is made the working notes through the assembly).</exception>
    /// <exception cref="InvalidOperationException">Bars are taken from versions into the working notes or an assembly is applied: undo them first.</exception>
    public static Project MakeWorking(this Project project, string id)
    {
        ArgumentNullException.ThrowIfNull(project);
        OtherNotes notes = project.Sources.Notes.FirstOrDefault(each => each.Id == id) ?? throw new KeyNotFoundException($"There are no other Notes {id}.");
        if (project.TakenBars.Any() || project.Assembly.Applied is not null)
        {
            throw new InvalidOperationException("The working notes take bars from versions or an assembly: undo it before other Notes become the working ones.");
        }

        string was = project.NotesId;
        List<OtherNotes> others = [.. project.Sources.Notes.Where(each => each.Id != id)];
        if (project.HasNotes)
        {
            others.Add(new OtherNotes(was, project.FileName, project.Source, project.WorkingProvenance)
            {
                Edits = project.Edits,
                Check = project.Check is { } check ? [.. check.Where(static flag => flag.Source != CheckSource.Performance)] : null,
                Scan = project.Scan,
                Transcription = project.Transcription,
                Pages = project.DocumentPages,
                ReadFrom = project.ReadFrom,
                Marks = project.Marks,
            });
        }

        CheckFlag[] compared = [.. (project.Check ?? []).Where(static flag => flag.Source == CheckSource.Performance)];
        return project with
        {
            NotesId = id,
            FileName = Path.ChangeExtension(project.FileName, Path.GetExtension(notes.FileName)),
            Source = notes.Music,
            KeptProvenance = notes.Provenance,
            Edits = notes.Edits,
            Check = notes.Check is null && compared.Length == 0 ? null : [.. (notes.Check ?? []).Concat(compared).OrderBy(static flag => flag.Bar).ThenBy(static flag => flag.Reason)],
            Scan = notes.Scan,
            Transcription = notes.Transcription,
            DocumentPages = project.Document is null ? null : notes.Pages,
            ReadFrom = notes.ReadFrom,
            Marks = notes.Marks,
            HiddenViews = [.. project.HiddenViews.Where(view => view != id)],
            Sources = project.Sources with { Notes = others },
            Motifs = [.. project.Motifs.Select(motif => motif with { Notes = Swapped(motif.Notes, was, id) })],
            Observations = [.. project.Observations.Select(observation => observation.Variation is null ? observation : observation with { Notes = Swapped(observation.Notes, was, id) })],
        };
    }

    /// <summary>
    /// The piece as it reads with the Notes <paramref name="id"/> as its notes, only to show them (sources sheet, «Открыть»
    /// on Notes that are not working): other Notes as made working on a copy without the versions, the assembly and the
    /// player's motifs, variations and ideas; a version's MIDI as notes of their own. The piece itself does not change.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The piece has no Notes with the id.</exception>
    public static Project ViewOf(this Project project, string id)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.HasNotes && id == project.NotesId)
        {
            return project;
        }

        if (project.FindPerformance(id) is { } version)
        {
            return Project.Create(project.Id, Path.ChangeExtension(project.FileName, ".mid"), version.Midi, project.CreatedAt) with { Metadata = project.Metadata, LastOpened = project.LastOpened };
        }

        Project bare = project with { Performances = [], Assembly = PieceAssembly.Empty, Motifs = [], Variations = [], Observations = [] };
        return bare.MakeWorking(id) with { Sources = SourcesLayer.None, KeptOriginals = project.KeptOriginals };
    }

    /// <summary>The Notes a motif or kept variation names once <paramref name="now"/> are the working notes instead of <paramref name="was"/>.</summary>
    private static string? Swapped(string? notes, string was, string now) => notes is null ? was : notes == now ? null : notes;

    private static Dictionary<string, T> With<T>(IReadOnlyDictionary<string, T> values, string key, T value) =>
        new(values.Where(pair => pair.Key != key), StringComparer.Ordinal) { [key] = value };

    private static Dictionary<string, T> Without<T>(IReadOnlyDictionary<string, T> values, string key) =>
        new(values.Where(pair => pair.Key != key), StringComparer.Ordinal);
}