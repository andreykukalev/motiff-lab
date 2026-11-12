import FileProvider
import Foundation

/// One original of a piece as music-core describes it (version 0.3, local-first; "originals" in a summary and in
/// `sources`): what it is, its name, size and fingerprint, whether its bytes are in this project file, and which devices
/// said they hold them. Only the description is in the project file; the bytes stay where the player keeps them.
struct OriginalEntry: Identifiable, Equatable, Sendable {
    var id: String
    /// "pdf", "images", "audio", "midi" or "musicxml".
    var kind: String
    /// The player's name for it, else its file's.
    var shown: String
    var fileName: String
    /// The SHA-256 of its bytes, 64 lower-case hex digits; nil when not known.
    var sha256: String?
    var bytes: Int64?
    var pages: Int?
    /// "file" (in the project file on the device that has it) or "device" (beside it).
    var kept: String
    /// The bytes are in this project file, here: the PDF kept in the piece.
    var inFile: Bool
    /// The devices that said they hold the bytes: "ipad", "web".
    var held: [String]

    /// The PDF kept in the piece (`PieceOriginal.DocumentId` in music-core).
    static let documentId = "src_pdf"

    /// What a badge is shown for, as on the web: a PDF, photos or a recording. A MIDI or MusicXML file is the notes.
    var isShown: Bool { kind == "pdf" || kind == "images" || kind == "audio" }

    /// The piece's PDF, the one original the iPad links again.
    var isDocument: Bool { id == Self.documentId && kind == "pdf" }

    /// The originals in a facade answer's "originals".
    static func list(_ value: Any?) -> [OriginalEntry] {
        (value as? [[String: Any]] ?? []).compactMap(entry)
    }

    private static func entry(_ object: [String: Any]) -> OriginalEntry? {
        guard let id = object["id"] as? String, let kind = object["kind"] as? String else { return nil }
        let fileName = object["fileName"] as? String ?? ""
        return OriginalEntry(
            id: id, kind: kind, shown: object["shown"] as? String ?? fileName, fileName: fileName,
            sha256: object["sha256"] as? String, bytes: (object["bytes"] as? NSNumber)?.int64Value,
            pages: (object["pages"] as? NSNumber)?.intValue, kept: object["kept"] as? String ?? "file",
            inFile: object["inFile"] as? Bool ?? false,
            held: (object["held"] as? [[String: Any]] ?? []).compactMap { $0["device"] as? String }
        )
    }
}

/// Where an original's bytes are, as this iPad sees them: the badge on it in Sources and, when it is not here, on the
/// piece's line in the library, worded as the web's (`OriginalPlaces`).
enum OriginalBadge: Equatable, Sendable {
    /// In the piece's file on this iPad: a copy.
    case here
    /// A link to the file where it lies in Files (iCloud Drive, Google Drive), kept on this iPad only.
    case linked
    /// Linked, but the file in Files is another one now: its fingerprint does not match.
    case changed
    /// Only on an iPad: a mark of this device's kind with no link here is another iPad's.
    case otherIpad
    /// Only in a browser, the web app on a computer.
    case inBrowser
    /// No device says it holds it.
    case nowhere

    /// The badge of an original with this iPad's link to it, if it keeps one. Another kind of device comes first, as in
    /// music-core's `OriginalSummary.Where`.
    static func of(_ original: OriginalEntry, link: OriginalLink?) -> OriginalBadge {
        if original.inFile { return .here }
        if let link { return link.changed ? .changed : .linked }
        if original.held.contains("web") { return .inBrowser }
        if original.held.contains("ipad") { return .otherIpad }
        return .nowhere
    }

    /// The bytes can be opened on this iPad.
    var isHere: Bool { self == .here || self == .linked }

    /// «На этом iPad», «Ссылка на файл в «Файлах»», «Оригинал изменился», «Только в браузере на компьютере», …
    func text(_ text: UIText) -> String {
        switch self {
        case .here: text["originals.onThisIpad"]
        case .linked: text["originals.linkFiles"]
        case .changed: text["originals.changed"]
        case .otherIpad: text["originals.otherIpad"]
        case .inBrowser: text["originals.inBrowserComputer"]
        case .nowhere: text["originals.nowhere"]
        }
    }
}

/// The library's line about a piece whose PDF, photos or recording is not on this iPad («PDF: только в браузере на
/// компьютере»), so a line never looks whole when its original is elsewhere (Andrei, 2026-11-11 21:16 UTC).
struct OriginalAway: Equatable, Sendable {
    var kind: String
    var badge: OriginalBadge

    /// The first shown original whose bytes cannot be opened here; nil when every one can, or the file keeps none.
    static func of(_ originals: [OriginalEntry], link: (OriginalEntry) -> OriginalLink?) -> OriginalAway? {
        for original in originals where original.isShown {
            let badge = OriginalBadge.of(original, link: link(original))
            if !badge.isHere { return OriginalAway(kind: original.kind, badge: badge) }
        }
        return nil
    }

    /// The piece's line in the library: its links are read from this iPad's store.
    static func of(_ summary: PieceSummary?, folder: URL = OriginalLinks.folder) -> OriginalAway? {
        guard let summary, let project = summary.id else { return nil }
        return of(summary.originals) { OriginalLinks.load(project: project, original: $0.id, in: folder) }
    }

    func text(_ text: UIText) -> String {
        let place = badge.text(text)
        return Self.kindText(kind, text) + ": " + String(place.prefix(1)).lowercased() + String(place.dropFirst())
    }

    /// «PDF», «Фото», «Запись», as the web names them.
    static func kindText(_ kind: String, _ text: UIText) -> String {
        switch kind {
        case "pdf": text["originals.kindPdf"]
        case "images": text["originals.kindImages"]
        default: text["originals.kindAudio"]
        }
    }
}

/// This iPad's link to an original where it lies in Files (Andrei, 2026-11-12: his scores stay in his Google Drive
/// folder): a security-scoped bookmark of the file the player picked, kept in Application Support by project and
/// original, never in the project file, which gets only the iPad's mark that it holds the original. The file is taken
/// as the same while its size and date are; when either changes it is read again and its fingerprint checked.
struct OriginalLink: Codable, Equatable, Sendable {
    /// The bookmark iOS made of the picked file.
    var bookmark: Data
    var fileName: String
    /// The fingerprint of the original, as its description has it.
    var sha256: String
    /// The file's size and date when its bytes last matched.
    var bytes: Int64?
    var modified: Date?
    /// The fingerprint did not match when the file was last read: the file in Files is another one now.
    var changed = false
    /// The changed file's size and date, so it is not read again while it stays as it is.
    var changedBytes: Int64?
    var changedModified: Date?
    var linkedAt: Date

    /// What the file's size and date say without reading it.
    enum Check: Equatable, Sendable {
        /// The same size and date as when it last matched.
        case same
        /// The size and date of the file last found changed.
        case changed
        /// Neither: it is read and its fingerprint checked.
        case unknown
    }

    func check(bytes: Int64?, modified: Date?) -> Check {
        guard let bytes, let modified else { return .unknown }
        if bytes == self.bytes, Self.same(modified, self.modified) { return .same }
        if changed, bytes == changedBytes, Self.same(modified, changedModified) { return .changed }
        return .unknown
    }

    /// The link once the file was read: its bytes are the original's at this size and date, or the file is another one.
    func verified(sha256 read: String, bytes: Int64?, modified: Date?) -> OriginalLink {
        var next = self
        if read == sha256 {
            next.bytes = bytes
            next.modified = modified
            next.changed = false
            next.changedBytes = nil
            next.changedModified = nil
        } else {
            next.changed = true
            next.changedBytes = bytes
            next.changedModified = modified
        }
        return next
    }

    /// Dates within a millisecond, as a date kept in JSON comes back.
    private static func same(_ date: Date, _ kept: Date?) -> Bool {
        guard let kept else { return false }
        return abs(date.timeIntervalSince(kept)) < 0.001
    }
}

/// The links, one small file each in Application Support/OriginalLinks/<project>/<original>.json (not in Documents, so
/// the Files app does not show them), shared by the environments: a link is checked against the description's
/// fingerprint wherever it is used.
enum OriginalLinks {
    static var folder: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("OriginalLinks", isDirectory: true)
    }

    static func url(project: String, original: String, in folder: URL) -> URL {
        folder.appendingPathComponent(safe(project), isDirectory: true).appendingPathComponent(safe(original) + ".json")
    }

    static func load(project: String, original: String, in folder: URL = folder) -> OriginalLink? {
        guard let data = try? Data(contentsOf: url(project: project, original: original, in: folder)) else { return nil }
        return try? JSONDecoder().decode(OriginalLink.self, from: data)
    }

    static func save(_ link: OriginalLink, project: String, original: String, in folder: URL = folder) {
        let target = url(project: project, original: original, in: folder)
        try? FileManager.default.createDirectory(at: target.deletingLastPathComponent(), withIntermediateDirectories: true)
        if let data = try? JSONEncoder().encode(link) { try? data.write(to: target, options: .atomic) }
    }

    static func remove(project: String, original: String, in folder: URL = folder) {
        try? FileManager.default.removeItem(at: url(project: project, original: original, in: folder))
    }

    /// An id as a file name: letters, digits, "_" and "-" stay, anything else becomes "_".
    static func safe(_ id: String) -> String {
        String(id.map { $0.isLetter || $0.isNumber || $0 == "_" || $0 == "-" ? $0 : "_" })
    }
}

/// Why a linked or picked file could not be read.
enum LinkProblem: Error, Equatable, Sendable {
    /// The file is no longer where the link points.
    case missing
    /// Its provider cannot be reached to download it: no connection.
    case offline
    case unreadable

    func text(_ text: UIText) -> String {
        switch self {
        case .missing: text["originals.missing"]
        case .offline: text["originals.offline"]
        case .unreadable: text["originals.unreadable"]
        }
    }

    /// What an error from Files means for the player.
    static func of(_ error: Error) -> LinkProblem {
        let error = error as NSError
        if isOffline(error) { return .offline }
        if isMissing(error) { return .missing }
        return .unreadable
    }

    /// No connection: the URL system's errors, or a file provider that cannot reach its server, here or underneath.
    static func isOffline(_ error: NSError) -> Bool {
        if error.domain == NSURLErrorDomain { return true }
        if error.domain == NSFileProviderErrorDomain && error.code == NSFileProviderError.Code.serverUnreachable.rawValue { return true }
        if let underlying = error.userInfo[NSUnderlyingErrorKey] as? NSError { return isOffline(underlying) }
        return false
    }

    static func isMissing(_ error: NSError) -> Bool {
        if error.domain == NSCocoaErrorDomain { return error.code == NSFileReadNoSuchFileError || error.code == NSFileNoSuchFileError }
        return error.domain == NSFileProviderErrorDomain && error.code == NSFileProviderError.Code.noSuchItem.rawValue
    }
}

/// A file in Files that the app links to: its bookmark, where it points now, its size and date, and its bytes, which iOS
/// first downloads from the provider (iCloud Drive, Google Drive) when they are not on the iPad.
enum LinkedFile {
    /// A bookmark of a picked file, made while the app may read it. On iOS the bookmark of a file picked in Files keeps
    /// the right to open it again (`withSecurityScope` is macOS only); minimal, as Apple's own sample makes it.
    static func bookmark(of url: URL) throws -> Data {
        try url.bookmarkData(options: .minimalBookmark, includingResourceValuesForKeys: nil, relativeTo: nil)
    }

    /// Where a link points now, with a new bookmark when iOS says the one kept is stale (the file was moved or renamed);
    /// nil when the file is gone.
    static func resolve(_ bookmark: Data) -> (url: URL, fresh: Data?)? {
        var stale = false
        guard let url = try? URL(resolvingBookmarkData: bookmark, options: [], relativeTo: nil, bookmarkDataIsStale: &stale) else { return nil }
        guard stale else { return (url, nil) }
        let lent = url.startAccessingSecurityScopedResource()
        defer { if lent { url.stopAccessingSecurityScopedResource() } }
        return (url, try? Self.bookmark(of: url))
    }

    /// The file's size and date as Files knows them, without downloading it.
    static func attributes(of url: URL) -> (bytes: Int64?, modified: Date?) {
        let lent = url.startAccessingSecurityScopedResource()
        defer { if lent { url.stopAccessingSecurityScopedResource() } }
        let values = try? url.resourceValues(forKeys: [.fileSizeKey, .contentModificationDateKey])
        return (values?.fileSize.map { Int64($0) }, values?.contentModificationDate)
    }

    /// The file's bytes: a coordinated read, for which iOS first downloads a file that is only in the cloud, so it can
    /// take a while; off the main thread.
    static func read(_ url: URL) async -> Result<Data, LinkProblem> {
        await withCheckedContinuation { continuation in
            DispatchQueue.global(qos: .userInitiated).async {
                continuation.resume(returning: readNow(url))
            }
        }
    }

    private static func readNow(_ url: URL) -> Result<Data, LinkProblem> {
        let lent = url.startAccessingSecurityScopedResource()
        defer { if lent { url.stopAccessingSecurityScopedResource() } }
        var failure: NSError?
        var result = Result<Data, LinkProblem>.failure(.unreadable)
        NSFileCoordinator(filePresenter: nil).coordinate(readingItemAt: url, options: [], error: &failure) { readable in
            do {
                result = .success(try Data(contentsOf: readable))
            } catch {
                result = .failure(LinkProblem.of(error))
            }
        }
        if let failure { return .failure(LinkProblem.of(failure)) }
        return result
    }
}
