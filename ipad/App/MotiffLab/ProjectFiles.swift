import Foundation

/// Projects are files on the device (decision record 0019 section 2.3): one `.motiff.json` project file per piece in
/// the app's Documents, shown in the Files app. music-core writes the file (`project.save`), so a project saved here
/// opens in the web app and back.
enum ProjectFiles {
    static let suffix = ".motiff.json"

    struct Entry: Identifiable, Hashable, Sendable {
        let url: URL
        let modified: Date
        var id: URL { url }
        /// The file's name without `.motiff.json`.
        var title: String { ProjectFiles.title(of: url.lastPathComponent) }
    }

    static var folder: URL {
        FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0]
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
