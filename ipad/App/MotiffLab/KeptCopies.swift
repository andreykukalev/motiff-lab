import Foundation

/// «Держать на этом iPad» (version 0.5, «Импорт папки»): for a playlist the player wants at the instrument without a
/// connection, a copy of each linked PDF read ahead of time from its provider, kept on this iPad only, in Application
/// Support (never in the project file, never online, never in the player's folder). A piece opens from its folder as
/// always; only when the provider cannot be reached does it open from this copy, once its fingerprint is checked.
enum KeptCopies {
    static var folder: URL { ImportFolders.root.appendingPathComponent("Kept", isDirectory: true) }

    /// The playlists kept on this iPad, by name, in this environment.
    static func playlists(in root: URL = ImportFolders.root) -> Set<String> {
        guard let data = try? Data(contentsOf: root.appendingPathComponent("kept-playlists.json")),
              let names = try? JSONDecoder().decode([String].self, from: data) else { return [] }
        return Set(names)
    }

    static func setPlaylists(_ names: Set<String>, in root: URL = ImportFolders.root) {
        try? FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        if let data = try? JSONEncoder().encode(names.sorted()) {
            try? data.write(to: root.appendingPathComponent("kept-playlists.json"), options: .atomic)
        }
    }

    static func url(project: String, in folder: URL = folder) -> URL {
        folder.appendingPathComponent(OriginalLinks.safe(project) + ".pdf")
    }

    static func has(project: String, in folder: URL = folder) -> Bool {
        FileManager.default.fileExists(atPath: url(project: project, in: folder).path)
    }

    /// The kept copy, when there is one with the fingerprint given.
    static func data(project: String, sha256: String, in folder: URL = folder) -> Data? {
        guard let data = try? Data(contentsOf: url(project: project, in: folder)), CloudSync.hash(data) == sha256 else { return nil }
        return data
    }

    static func keep(_ data: Data, project: String, in folder: URL = folder) {
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        try? data.write(to: url(project: project, in: folder), options: .atomic)
    }

    static func remove(project: String, in folder: URL = folder) {
        try? FileManager.default.removeItem(at: url(project: project, in: folder))
    }
}
