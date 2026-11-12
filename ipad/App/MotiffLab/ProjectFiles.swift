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

    /// The title the player set in the library, if they did.
    var playersTitle: String? { titleFrom == "player" ? title : nil }

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
    }

    /// Reads a project file's summary through music-core; nil when the file cannot be read.
    static func read(_ url: URL) async -> PieceSummary? {
        guard let data = try? Data(contentsOf: url) else { return nil }
        return await read(data)
    }

    /// Reads the summary of a project file's bytes through music-core; nil when they are not a project file it can read.
    static func read(_ data: Data) async -> PieceSummary? {
        guard let file = String(data: data, encoding: .utf8),
              let body = try? JSONSerialization.data(withJSONObject: ["file": file]),
              let request = String(data: body, encoding: .utf8) else { return nil }
        let answer = await MusicCore.run { MusicCore.call(piece: 0, "project.summary", request) }
        return PieceSummary(answer: answer)
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
        let modified = (try? url.resourceValues(forKeys: [.contentModificationDateKey]))?.contentModificationDate
        try Data(changed.file.utf8).write(to: url, options: .atomic)
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
        let encoded = try JSONSerialization.data(withJSONObject: request)
        let body = String(decoding: encoded, as: UTF8.self)
        let answer = await MusicCore.run { MusicCore.call(piece: 0, "project.metadata", body) }
        guard let object = facadeObject(answer), object["error"] == nil,
              let changed = object["file"] as? String, let summary = PieceSummary(answer: answer) else {
            throw ProjectError(message: facadeObject(answer)?["message"] as? String ?? "music-core did not change the project.")
        }
        return (summary, changed)
    }
}
