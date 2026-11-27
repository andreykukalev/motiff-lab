using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Pieces;

namespace MotiffLab.MusicCore.Projects;

/// <summary>The piece's originals (version 0.3): their descriptions as the file keeps them and where their bytes are.</summary>
public sealed partial record Project
{
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

    /// <summary>Whether an original's bytes are in this project file: the PDF here, the imported file, a MIDI version.</summary>
    public bool HasInFile(PieceOriginal original)
    {
        ArgumentNullException.ThrowIfNull(original);
        return original.Kept == OriginalKept.File && original.Id switch
        {
            PieceOriginal.DocumentId => Document is not null,
            PieceOriginal.SourceFileId => HasNotes,
            _ => FindPerformance(original.Id) is { Source: Performance.PerformanceSource.Midi } || Sources.Data.ContainsKey(original.Id),
        };
    }

    /// <summary>The originals as a library row keeps them: what each is and where its bytes are.</summary>
    public IReadOnlyList<OriginalSummary> OriginalSummaries =>
        [.. Originals.Select(original => new OriginalSummary(original.Id, original.Kind, original.Kept, HasInFile(original), [.. original.Held.Select(static mark => mark.Device)]))];

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
}