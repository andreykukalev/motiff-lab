using System.Security.Cryptography;

namespace MotiffLab.MusicCore.Projects;

/// <summary>What an original is (version 0.3, local-first originals).</summary>
public enum OriginalKind
{
    Pdf,

    /// <summary>Photos of pages.</summary>
    Images,

    Audio,
    Midi,
    MusicXml,
}

/// <summary>Where an original's bytes are kept.</summary>
public enum OriginalKept
{
    /// <summary>Inside the project file on the device (the PDF in <c>document</c>, an imported file in <c>source</c>); never online.</summary>
    File,

    /// <summary>Beside the project on the device only (a recording's sound); never in the project file.</summary>
    Device,

    /// <summary>
    /// In the player's own folder, where it stays (version 0.5, «Импорт папки»): the device links the file there and reads
    /// it in place, so its bytes are never in the project file, and the file is never changed, moved or deleted.
    /// </summary>
    Link,
}

/// <summary>A kind of device that can hold an original's bytes.</summary>
public enum HoldingDevice
{
    Ipad,
    Web,
}

/// <summary>A device's own mark that it holds an original's bytes, written and taken back only by that device.</summary>
public sealed record OriginalHeld(HoldingDevice Device, DateTimeOffset At);

/// <summary>Where an original's bytes are, as one device sees them: the badge both apps show on it.</summary>
public enum OriginalWhere
{
    /// <summary>In the project file on this device (the PDF, the imported file); it never goes online.</summary>
    InFile,

    /// <summary>On this device, beside the project file (a recording's sound).</summary>
    Here,

    /// <summary>Only on an iPad.</summary>
    OnIpad,

    /// <summary>Only in a browser: the web app on a computer, or another browser than this one.</summary>
    InBrowser,

    /// <summary>No device says it holds them.</summary>
    Nowhere,
}

/// <summary>An original as a library row keeps it (version 0.3): what it is and where its bytes are, without loading the project.</summary>
/// <param name="InFile">The bytes are in this project file, here.</param>
public sealed record OriginalSummary(string Id, OriginalKind Kind, OriginalKept Kept, bool InFile, IReadOnlyList<HoldingDevice> Held)
{
    /// <summary>
    /// Where the bytes are as <paramref name="self"/> sees them; <paramref name="heldHere"/> when this device has them beside
    /// the file. Another kind of device comes first: a mark of this device's own kind is another iPad or another browser.
    /// </summary>
    public OriginalWhere Where(HoldingDevice self, bool heldHere)
    {
        if (InFile || heldHere)
        {
            return InFile ? OriginalWhere.InFile : OriginalWhere.Here;
        }

        HoldingDevice other = self == HoldingDevice.Web ? HoldingDevice.Ipad : HoldingDevice.Web;
        HoldingDevice? holder = Held.Contains(other) ? other : Held.Contains(self) ? self : null;
        return holder switch
        {
            HoldingDevice.Ipad => OriginalWhere.OnIpad,
            HoldingDevice.Web => OriginalWhere.InBrowser,
            _ => OriginalWhere.Nowhere,
        };
    }

    public bool Equals(OriginalSummary? other) =>
        other is not null && Id == other.Id && Kind == other.Kind && Kept == other.Kept && InFile == other.InFile && Held.SequenceEqual(other.Held);

    public override int GetHashCode() => HashCode.Combine(Id, Kind, Kept, InFile, Held.Count);
}

/// <summary>
/// The description of one original of a piece (version 0.3, Andrei 2026-11-11 21:20 UTC: local-first): what it is, its
/// name, size and fingerprint, and which devices hold its bytes. Only the description is in the project file and goes
/// online with it; the bytes stay on the devices (<see cref="Kept"/>). The fingerprint lets a device link a file the
/// player picks again to the same original, without a copy.
/// </summary>
/// <param name="Id">Stable within the project: "src_pdf" for the PDF kept in the piece, "src_file" for the file the notes were imported from, and a recording version's id for its sound, so the web's stored sound keeps its key.</param>
/// <param name="FileName">The file's name as the player picked it.</param>
/// <param name="AddedAt">When it was added to the piece.</param>
public sealed record PieceOriginal(string Id, OriginalKind Kind, string FileName, OriginalKept Kept, DateTimeOffset AddedAt)
{
    /// <summary>The id of the PDF kept inside the piece.</summary>
    public const string DocumentId = "src_pdf";

    /// <summary>The id of the MIDI or MusicXML file the piece's notes were imported from.</summary>
    public const string SourceFileId = "src_file";

    public string Id { get; } = !string.IsNullOrWhiteSpace(Id) ? Id : throw new ArgumentException("An original needs an id.", nameof(Id));

    public string FileName { get; } = !string.IsNullOrWhiteSpace(FileName) ? FileName : throw new ArgumentException("An original needs a file name.", nameof(FileName));

    /// <summary>What the player calls it; null for its file name.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// Where the file was inside the folder the player imported (version 0.5, «Импорт папки»): the folders, "/" between
    /// them, "Бах/ХТК"; empty for a file right in the chosen folder; null for a file not imported from a folder.
    /// </summary>
    public string? Folder
    {
        get;
        init => field = value is null || IsFolder(value) ? value : throw new ArgumentException("A folder is a path inside the chosen folder: names with \"/\" between them.", nameof(Folder));
    }

    /// <summary>The SHA-256 of the file's bytes as picked, 64 lower-case hex digits; null when not known (a file saved before it was kept).</summary>
    public string? Sha256
    {
        get;
        init => field = value is null || (value.Length == 64 && value.All(char.IsAsciiHexDigitLower)) ? value : throw new ArgumentException("A fingerprint is 64 lower-case hex digits.", nameof(Sha256));
    }

    /// <summary>The file's size in bytes; null when not known.</summary>
    public long? Bytes { get; init => field = value is null or >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(Bytes)); }

    /// <summary>A PDF's or a set of photos' pages; null when not known.</summary>
    public int? Pages { get; init => field = value is null or > 0 ? value : throw new ArgumentOutOfRangeException(nameof(Pages)); }

    /// <summary>A recording's length in milliseconds; null when not known.</summary>
    public long? DurationMs { get; init => field = value is null or >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(DurationMs)); }

    /// <summary>The devices that said they hold the bytes, one mark each, in device order.</summary>
    public IReadOnlyList<OriginalHeld> Held
    {
        get;
        init => field = [.. value.GroupBy(static mark => mark.Device).Select(static marks => marks.MaxBy(static mark => mark.At)!).OrderBy(static mark => mark.Device)];
    } = [];

    /// <summary>The name to show: the player's, else the file's.</summary>
    public string Shown => Name ?? FileName;

    /// <summary>Whether a device has its mark.</summary>
    public bool IsHeldBy(HoldingDevice device) => Held.Any(mark => mark.Device == device);

    /// <summary>The original with a device's mark set at <paramref name="at"/>.</summary>
    public PieceOriginal HeldBy(HoldingDevice device, DateTimeOffset at) =>
        this with { Held = [.. Held.Where(mark => mark.Device != device), new OriginalHeld(device, at)] };

    /// <summary>The original without a device's mark.</summary>
    public PieceOriginal NotHeldBy(HoldingDevice device) => this with { Held = [.. Held.Where(mark => mark.Device != device)] };

    /// <summary>Whether a file the player picked is this original: the fingerprints match.</summary>
    public bool Matches(string sha256) => Sha256 is { } known && string.Equals(known, sha256, StringComparison.Ordinal);

    /// <summary>Whether a path names folders inside the chosen one: empty, or names with "/" between them, none empty, "." or "..".</summary>
    public static bool IsFolder(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Length == 0 || (!path.Contains('\\') && path.Split('/').All(static name => name.Trim().Length > 0 && name is not ("." or "..")));
    }

    /// <summary>The SHA-256 of some bytes, as <see cref="Sha256"/> keeps it.</summary>
    public static string Fingerprint(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public bool Equals(PieceOriginal? other) =>
        other is not null && Id == other.Id && Kind == other.Kind && FileName == other.FileName && Kept == other.Kept && AddedAt == other.AddedAt
        && Name == other.Name && Folder == other.Folder && Sha256 == other.Sha256 && Bytes == other.Bytes && Pages == other.Pages && DurationMs == other.DurationMs
        && Held.SequenceEqual(other.Held);

    public override int GetHashCode() => HashCode.Combine(Id, Kind, FileName, Kept, AddedAt, Sha256, Held.Count);
}

/// <summary>
/// The originals of a piece: the ones its own data shows (the PDF in it, the file it was imported from, each recording
/// and MIDI version), with the descriptions the file keeps laid over them, and how two copies' descriptions merge.
/// </summary>
public static class PieceOriginals
{
    /// <summary>
    /// The originals the piece's own data shows, as a file written before originals were kept reads (version 0.3): the PDF
    /// in <c>document</c> (<see cref="PieceOriginal.DocumentId"/>), the MIDI or MusicXML file the notes were imported from
    /// (<see cref="PieceOriginal.SourceFileId"/>; not a scan's or a transcription's notes), and each recording or MIDI
    /// version under its own id, so the web keeps its stored sound under the same key. No fingerprint is worked out here:
    /// hashing a PDF of tens of megabytes on every read is too slow, so <see cref="Project.Fingerprinted"/> does it once.
    /// </summary>
    public static IReadOnlyList<PieceOriginal> Derived(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        List<PieceOriginal> originals = [];
        if (project.Document is { } document)
        {
            originals.Add(new PieceOriginal(PieceOriginal.DocumentId, OriginalKind.Pdf, project.DocumentFileName, OriginalKept.File, project.CreatedAt) { Bytes = document.Length });
        }

        if (project.HasNotes && project.Scan is null && project.Transcription is null && project.ReadFrom is null && FileKind(project.FileName) is { } kind)
        {
            originals.Add(new PieceOriginal(PieceOriginal.SourceFileId, kind, project.FileName, OriginalKept.File, project.CreatedAt) { Bytes = project.Source.Length });
        }

        foreach (SavedPerformance performance in project.Performances)
        {
            if (performance.Source is PerformanceSource.Recording or PerformanceSource.Midi)
            {
                originals.Add(new PieceOriginal(
                    performance.Id,
                    performance.Source == PerformanceSource.Recording ? OriginalKind.Audio : OriginalKind.Midi,
                    performance.Name,
                    performance.Source == PerformanceSource.Recording ? OriginalKept.Device : OriginalKept.File,
                    performance.At ?? project.CreatedAt)
                {
                    Bytes = performance.Source == PerformanceSource.Midi ? performance.Midi.Length : null,
                });
            }
        }

        return originals;
    }

    /// <summary>
    /// The kept descriptions laid over the derived ones: a kept one takes the place of the derived one with its id, and
    /// the kept ones with no derived one (a PDF whose bytes stayed on another device) follow in their order; the PDF comes
    /// first, then the imported file, so every device lists them alike.
    /// </summary>
    public static IReadOnlyList<PieceOriginal> Over(IReadOnlyList<PieceOriginal> derived, IReadOnlyList<PieceOriginal> kept)
    {
        ArgumentNullException.ThrowIfNull(derived);
        ArgumentNullException.ThrowIfNull(kept);
        Dictionary<string, PieceOriginal> byId = kept.ToDictionary(static original => original.Id, StringComparer.Ordinal);
        HashSet<string> derivedIds = [.. derived.Select(static original => original.Id)];
        IEnumerable<PieceOriginal> all = [.. derived.Select(original => byId.GetValueOrDefault(original.Id, original)), .. kept.Where(original => !derivedIds.Contains(original.Id))];
        return [.. all.OrderBy(static original => original.Id switch { PieceOriginal.DocumentId => 0, PieceOriginal.SourceFileId => 1, _ => 2 })];
    }

    /// <summary>
    /// Two copies' originals as one (0026 S8): a union by fingerprint, else by id, with no deletions. The descriptions are
    /// the player's data, the first copy's (<paramref name="online"/>) kept and filled in from the second where it knows
    /// more; the holding marks are bookkeeping that only their own device writes, so <paramref name="device"/>'s mark is
    /// taken from <paramref name="here"/> and every other device's from <paramref name="online"/>. With no device, the
    /// online marks stand.
    /// </summary>
    public static IReadOnlyList<PieceOriginal> Merge(IReadOnlyList<PieceOriginal> online, IReadOnlyList<PieceOriginal> here, HoldingDevice? device)
    {
        ArgumentNullException.ThrowIfNull(online);
        ArgumentNullException.ThrowIfNull(here);
        List<PieceOriginal> merged = [];
        HashSet<PieceOriginal> matched = new(ReferenceEqualityComparer.Instance);
        foreach (PieceOriginal original in online)
        {
            PieceOriginal? mine = here.FirstOrDefault(each => !matched.Contains(each) && each.Sha256 is { } sha && original.Sha256 == sha)
                ?? here.FirstOrDefault(each => !matched.Contains(each) && each.Id == original.Id && (each.Sha256 is null || original.Sha256 is null));
            if (mine is null)
            {
                merged.Add(device is { } self ? original.NotHeldBy(self) : original);
                continue;
            }

            matched.Add(mine);
            merged.Add(Combined(original, mine, device));
        }

        HashSet<string> ids = [.. merged.Select(static original => original.Id)];
        foreach (PieceOriginal mine in here.Where(each => !matched.Contains(each)))
        {
            // A different original that only carries an id the online copy uses keeps a free id of its own.
            string id = ids.Contains(mine.Id) ? FreeId(mine.Id, ids) : mine.Id;
            ids.Add(id);
            merged.Add(id == mine.Id ? mine : Renamed(mine, id));
        }

        return merged;
    }

    /// <summary>
    /// One original from both copies: the online description, or this device's when only it has the fingerprint (the
    /// online copy was written again by a build that drops the descriptions, version 0.2, and shows only what its data
    /// does), filled in from the other; the marks as <see cref="Merge"/> says.
    /// </summary>
    private static PieceOriginal Combined(PieceOriginal online, PieceOriginal mine, HoldingDevice? device)
    {
        (PieceOriginal described, PieceOriginal other) = online.Sha256 is null && mine.Sha256 is not null ? (mine, online) : (online, mine);
        return described with
        {
            Name = described.Name ?? other.Name,
            Folder = described.Folder ?? other.Folder,
            Sha256 = described.Sha256 ?? other.Sha256,
            Bytes = described.Bytes ?? other.Bytes,
            Pages = described.Pages ?? other.Pages,
            DurationMs = described.DurationMs ?? other.DurationMs,
            Held = device is { } thisDevice
                ? [.. online.Held.Where(mark => mark.Device != thisDevice), .. mine.Held.Where(mark => mark.Device == thisDevice)]
                : online.Held,
        };
    }

    private static PieceOriginal Renamed(PieceOriginal original, string id) => new(id, original.Kind, original.FileName, original.Kept, original.AddedAt)
    {
        Name = original.Name,
        Folder = original.Folder,
        Sha256 = original.Sha256,
        Bytes = original.Bytes,
        Pages = original.Pages,
        DurationMs = original.DurationMs,
        Held = original.Held,
    };

    /// <summary>MIDI or MusicXML by the file's extension, as the importers take it; null for anything else.</summary>
    public static OriginalKind? FileKind(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".mid" or ".midi" => OriginalKind.Midi,
            ".musicxml" or ".mxl" or ".xml" => OriginalKind.MusicXml,
            _ => null,
        };
    }

    private static string FreeId(string id, IReadOnlySet<string> taken)
    {
        for (int number = 2; ; number++)
        {
            string candidate = $"{id}_{number}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}