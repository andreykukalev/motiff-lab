import Foundation

/// Projects are files on the device (decision record 0019 section 2.3): one `.motiff.json` project file per piece in
/// the app's Documents, shown in the Files app. music-core writes the file (`project.save`), so a project saved here
/// opens in the web app and back. Each server environment has its own folder (`ServerEnvironment.projectsFolder`):
/// Main's is Documents itself, Test's Documents/Test.
enum ProjectFiles {
    static let suffix = ".motiff.json"

    struct Entry: Identifiable, Hashable, Sendable {
        let url: URL
        let modified: Date
        var id: URL { url }
        /// The file's name without `.motiff.json`.
        var title: String { ProjectFiles.title(of: url.lastPathComponent) }
    }

    /// The folder of the environment the app works in now.
    static var folder: URL {
        EnvironmentSetup.current().projectsFolder
    }

    /// The projects in the folder, the most recently changed first.
    static func list(in folder: URL = folder) -> [Entry] {
        let urls = (try? FileManager.default.contentsOfDirectory(
            at: folder, includingPropertiesForKeys: [.contentModificationDateKey], options: [.skipsHiddenFiles]
        )) ?? []
        return urls
            .filter { $0.lastPathComponent.lowercased().hasSuffix(suffix) }
            .map { url in
                Entry(url: url, modified: (try? url.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast)
            }
            .sorted { $0.modified > $1.modified }
    }

    static func isProject(_ fileName: String) -> Bool {
        fileName.lowercased().hasSuffix(suffix)
    }

    /// Whether the file is one of the folder's projects (then it is saved in place).
    static func isInFolder(_ url: URL, folder: URL = folder) -> Bool {
        url.deletingLastPathComponent().standardizedFileURL.path == folder.standardizedFileURL.path
    }

    static func title(of fileName: String) -> String {
        if isProject(fileName) {
            return String(fileName.dropLast(suffix.count))
        }
        return (fileName as NSString).deletingPathExtension
    }

    /// A new file for a piece of this title: "Title.motiff.json", or "Title 2.motiff.json" when that is taken.
    static func newURL(for title: String, in folder: URL = folder) -> URL {
        let safe = title
            .components(separatedBy: CharacterSet(charactersIn: "/\\:?*\"<>|").union(.newlines).union(.controlCharacters))
            .joined(separator: " ")
            .trimmingCharacters(in: .whitespaces)
        let base = safe.isEmpty ? "Piece" : String(safe.prefix(120))
        var url = folder.appendingPathComponent(base + suffix)
        var number = 2
        while FileManager.default.fileExists(atPath: url.path) {
            url = folder.appendingPathComponent("\(base) \(number)\(suffix)")
            number += 1
        }
        return url
    }

    /// Asks music-core for the piece's project file and writes it, whole, to the url.
    static func save(piece: Int32, to url: URL, now: Date = Date()) async throws {
        let request = "{\"now\": \"\(ISO8601DateFormatter().string(from: now))\"}"
        let text = await MusicCore.run { MusicCore.call(piece: piece, "project.save", request) }
        if let failure = facadeObject(text), failure["error"] != nil {
            throw ProjectError(message: failure["message"] as? String ?? "music-core did not save the project.")
        }
        try Data(text.utf8).write(to: url, options: .atomic)
    }

    /// A piece just made in music-core saved to a new project file here, titled `title`, and closed again, for the library
    /// to open as any saved piece: photos of pages (`PhotoPiece`) or, since v1.1, a recording added (`RecordingPiece`).
    static func saveNew(piece: Int32, title: String) async -> Result<URL, ProjectError> {
        let url = newURL(for: title)
        let saved: Result<URL, ProjectError>
        do {
            try await save(piece: piece, to: url)
            saved = .success(url)
        } catch {
            saved = .failure((error as? ProjectError) ?? ProjectError(message: error.localizedDescription))
        }
        _ = await MusicCore.run { MusicCore.close(piece: piece) }
        return saved
    }
}

struct ProjectError: Error {
    let message: String
}

/// What a library card says of a piece, read by music-core from its project file (`project.summary`, facade contract 4).
struct PieceSummary: Equatable, Sendable {
    /// "imported", "scanned" or "transcribed".
    var origin: String
    var hasNotes: Bool
    var ideas: Int
    var checkBars: Int
    /// The resume point worked from last, by its name when it has one.
    var latest: Latest?
    /// The project's id, and when it was last opened (ISO 8601, as music-core writes it).
    var id: String?
    var lastOpened = ""
    /// For notes read from a PDF piece: that piece's id, and where each of its pages starts in the notes (PDF | Notes, 0023).
    var readFrom: String?
    var readFromPages: [Double] = []
    /// For a piece with notes and a PDF kept inside it: where each of the PDF's pages starts in the notes (contract 6).
    var documentPages: [Double] = []
    /// Whether the piece keeps a PDF.
    var document = false
    /// The title, composer, tags and playlists a line shows (contract 12): "titleFrom" is "player", "file" (the score names
    /// it) or "fileName"; "composerFrom" is "player" or "file", or nil when neither names one.
    var title: String?
    var titleFrom: String?
    var composer: String?
    var composerFrom: String?
    var tags: [String] = []
    var playlists: [String] = []
    /// The piece's originals and where their bytes are (version 0.3, contract 12), for the library's line about one that
    /// is not here.
    var originals: [OriginalEntry] = []
    /// Where a piece made with «Пьесы в файле» is in its collection (version 0.4, within contract 12); nil for a piece of
    /// its own.
    var partOf: PartOfEntry?
    /// What the piece is known by on every device (version 0.6.1, #582 item 1; music-core's `SamePiece.Keys`, within
    /// contract 12): the fingerprints of the files it was made from, so a sync pass finds the projects that are one piece.
    var pieceKeys: [String] = []

    /// The title the player set in the library, if they did.
    var playersTitle: String? { titleFrom == "player" ? title : nil }

    /// The title a piece's line shows: the one the player set or the score names, else `fileTitle`, the file's name.
    func shownTitle(else fileTitle: String) -> String {
        titleFrom == "player" || titleFrom == "file" ? title ?? fileTitle : fileTitle
    }

    /// A piece that is only its recording, with no notes yet (v1.1, finding 29): no PDF here or described, and a recording.
    var isRecordingOnly: Bool {
        !hasNotes && !document && !originals.contains(where: \.isDocument) && originals.contains { $0.kind == "audio" }
    }

    /// The composer the player set in the library, if they did.
    var playersComposer: String? { composerFrom == "player" ? composer : nil }

    struct Latest: Equatable, Sendable {
        var name: String?
    }

    /// The summary in a facade answer: {"project": {…}}.
    init?(answer: String) {
        guard let project = facadeObject(answer)?["project"] as? [String: Any] else { return nil }
        origin = project["origin"] as? String ?? "imported"
        hasNotes = project["hasNotes"] as? Bool ?? true
        ideas = (project["ideas"] as? NSNumber)?.intValue ?? 0
        checkBars = (project["checkBars"] as? NSNumber)?.intValue ?? 0
        latest = (project["latest"] as? [String: Any]).map { Latest(name: $0["name"] as? String) }
        id = project["id"] as? String
        lastOpened = project["lastOpened"] as? String ?? ""
        readFrom = project["readFrom"] as? String
        readFromPages = (project["readFromPages"] as? [NSNumber] ?? []).map(\.doubleValue)
        documentPages = (project["documentPages"] as? [NSNumber] ?? []).map(\.doubleValue)
        document = project["document"] as? Bool ?? false
        title = project["title"] as? String
        titleFrom = project["titleFrom"] as? String
        composer = project["composer"] as? String
        composerFrom = project["composerFrom"] as? String
        tags = project["tags"] as? [String] ?? []
        playlists = project["playlists"] as? [String] ?? []
        originals = OriginalEntry.list(project["originals"])
        partOf = PartOfEntry(project["partOf"])
        pieceKeys = project["pieceKeys"] as? [String] ?? []
    }

    /// Reads a project file's summary through music-core; nil when the file cannot be read.
    static func read(_ url: URL) async -> PieceSummary? {
        guard let data = try? Data(contentsOf: url) else { return nil }
        return await read(data)
    }

    /// Reads the summary of a project file's bytes through music-core; nil when they are not a project file it can read.
    static func read(_ data: Data) async -> PieceSummary? {
        guard let file = String(data: data, encoding: .utf8) else { return nil }
        let request = facadeRequest(["file": file])
        let answer = await MusicCore.run { MusicCore.call(piece: 0, "project.summary", request) }
        ProbeCounts.shared.mark("summary")
        return PieceSummary(answer: answer)
    }
}

/// What music-core read from each project file, kept by the file's bytes, so it is asked again only for the files that
/// changed since it last looked (core and iPad review v0.6.5, finding 6): a Back used to read every project in Documents
/// through music-core, one after another. The bytes are read off the main thread; a file whose details were written in
/// place keeps its date but not its bytes, so it is read again. `reading` is what is read: the library's summaries
/// (`PieceSummary.read`), the «В черновик» sheet's other pieces' drafts (v1.2, `OtherPieceDrafts`).
@MainActor
final class ProjectFileCache<Value: Sendable> {
    private let reading: @Sendable (Data) async -> Value?
    private var kept: [URL: (hash: String, value: Value)] = [:]

    init(reading: @escaping @Sendable (Data) async -> Value?) {
        self.reading = reading
    }

    /// What music-core read from the file, from what was kept while its bytes are the same.
    func value(_ url: URL) async -> Value? {
        let data = await Task.detached(priority: .userInitiated) { try? Data(contentsOf: url) }.value
        guard let data else {
            kept[url] = nil
            return nil
        }
        let hash = CloudSync.hash(data)
        if let known = kept[url], known.hash == hash { return known.value }
        guard let value = await reading(data) else {
            kept[url] = nil
            return nil
        }
        kept[url] = (hash, value)
        return value
    }

    /// What music-core read from these files, the ones it can read; the files no longer listed are forgotten.
    func values(_ urls: [URL]) async -> [URL: Value] {
        var read: [URL: Value] = [:]
        for url in urls {
            if let value = await value(url) { read[url] = value }
        }
        let listed = Set(urls)
        kept = kept.filter { listed.contains($0.key) }
        return read
    }
}

/// A piece's title, composer, tags and playlists as the player sets them in the library (version 0.2, facade contract 12):
/// `project.metadata` on piece 0 with the project file's text as "file", which music-core answers changed; the app writes
/// it back in place. A field sent as null is cleared, so the card shows what the file says again.
enum PieceDetailsFile {
    /// What the file itself says: its summary with the player's title and composer taken off, written nowhere.
    static func fromFile(_ url: URL) async -> PieceSummary? {
        (try? await change(url, ["title": NSNull(), "composer": NSNull()]))?.summary
    }

    /// Writes the player's title, composer, tags and playlists into the project file. The file keeps its date, so the piece
    /// keeps its place among the ones opened last.
    static func save(_ url: URL, title: String?, composer: String?, tags: [String], playlists: [String]) async throws {
        let changed = try await change(url, [
            "title": title.map { $0 as Any } ?? NSNull(),
            "composer": composer.map { $0 as Any } ?? NSNull(),
            "tags": tags,
            "playlists": playlists,
        ])
        try write(changed.file, to: url)
    }

    /// Writes one field the player typed on the piece's line, "title" or "composer" (version 0.5.1, «Распознать ноты» for
    /// several pieces); the others stay as they are. Empty clears it, so the file's own shows again.
    static func set(_ url: URL, _ field: String, to value: String?) async throws {
        let changed = try await change(url, [field: value.map { $0 as Any } ?? NSNull()])
        try write(changed.file, to: url)
    }

    /// The project file written in place, keeping its date.
    private static func write(_ file: String, to url: URL) throws {
        let modified = (try? url.resourceValues(forKeys: [.contentModificationDateKey]))?.contentModificationDate
        try Data(file.utf8).write(to: url, options: .atomic)
        if let modified {
            try? FileManager.default.setAttributes([.modificationDate: modified], ofItemAtPath: url.path)
        }
    }

    /// The project file at the url with the fields changed, as music-core answers it: its summary and its whole text.
    private static func change(_ url: URL, _ fields: [String: Any]) async throws -> (summary: PieceSummary, file: String) {
        let data = try Data(contentsOf: url)
        guard let file = String(data: data, encoding: .utf8) else {
            throw ProjectError(message: "The project file could not be read.")
        }
        var request = fields
        request["file"] = file
        let body = facadeRequest(request)
        let answer = await MusicCore.run { MusicCore.call(piece: 0, "project.metadata", body) }
        guard let object = facadeObject(answer), object["error"] == nil,
              let changed = object["file"] as? String, let summary = PieceSummary(answer: answer) else {
            throw ProjectError(message: facadeObject(answer)?["message"] as? String ?? "music-core did not change the project.")
        }
        return (summary, changed)
    }
}
