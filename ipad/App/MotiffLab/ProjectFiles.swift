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
