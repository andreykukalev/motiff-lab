import Foundation

/// A folder the player chose in Files to add pieces from (version 0.5, «Импорт папки», behind `experimental.libraryImport`):
/// iCloud Drive, Google Drive, OneDrive, Dropbox or the iPad itself, as Files shows it. Kept on this iPad only, never in a
/// project file: its name, the provider it is in, and the security-scoped bookmark that lets the app read it again after
/// a relaunch. The app only reads the folder: it never writes, renames, moves or deletes a file or a folder in it.
struct ImportFolder: Codable, Equatable, Identifiable, Sendable {
    var id: String
    /// The folder's own name, «Ноты».
    var name: String
    /// The provider Files shows it under, «Google Drive»; nil when the app cannot tell.
    var provider: String?
    /// The bookmark iOS made of the folder when the player picked it, made again whenever iOS says it went stale.
    var bookmark: Data
    var addedAt: Date
    /// When the folder was last read whole (picked, or «Проверить папку»).
    var checkedAt: Date?
    /// What the last try to open the folder from its bookmark found: nil when it opened, else why not.
    var problem: FolderProblem?
    /// When the bookmark was last opened, and when iOS last said it was stale and it was made again.
    var openedAt: Date?
    var refreshedAt: Date?

    /// «Google Drive › Ноты», as the import's screens name the place.
    var place: String { [provider, name].compactMap { $0 }.joined(separator: " › ") }
}

/// Why the app could not get into a folder it was given.
enum FolderProblem: String, Codable, Equatable, Sendable {
    /// The bookmark no longer opens: iOS (or the provider) did not keep the app's right to the folder after a relaunch.
    case lost
    /// The bookmark opens, but the folder cannot be listed: the provider refuses the app, or the folder is gone.
    case unreadable
    /// The provider cannot be reached: no connection.
    case offline
}

/// The folders the player imported from, one small JSON file per environment in Application Support (not in Documents,
/// so the Files app does not show it): `LibraryImport/<environment>/folders.json`.
enum ImportFolders {
    /// This environment's own place for the import's records: each environment has its own library.
    static var root: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("LibraryImport", isDirectory: true)
            .appendingPathComponent(EnvironmentSetup.current().rawValue, isDirectory: true)
    }

    static func load(in root: URL = root) -> [ImportFolder] {
        guard let data = try? Data(contentsOf: root.appendingPathComponent("folders.json")) else { return [] }
        return (try? decoder.decode([ImportFolder].self, from: data)) ?? []
    }

    static func save(_ folders: [ImportFolder], in root: URL = root) {
        try? FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        if let data = try? encoder.encode(folders) { try? data.write(to: root.appendingPathComponent("folders.json"), options: .atomic) }
    }

    /// Keeps a folder: in place of the one with its id, else added.
    static func keep(_ folder: ImportFolder, in root: URL = root) {
        var folders = load(in: root)
        if let index = folders.firstIndex(where: { $0.id == folder.id }) {
            folders[index] = folder
        } else {
            folders.append(folder)
        }
        save(folders, in: root)
    }

    static func folder(_ id: String?, in root: URL = root) -> ImportFolder? {
        guard let id else { return nil }
        return load(in: root).first { $0.id == id }
    }

    /// The provider a folder is in, by where Files keeps it on the iPad; nil when the path does not say.
    static func provider(of url: URL) -> String? {
        let path = url.path.lowercased()
        let known: [(String, String)] = [
            ("com.google.drive", "Google Drive"),
            ("googledrive", "Google Drive"),
            ("onedrive", "OneDrive"),
            ("dropbox", "Dropbox"),
            ("mobile documents", "iCloud Drive"),
            ("com~apple~clouddocs", "iCloud Drive"),
        ]
        return known.first { path.contains($0.0) }?.1
    }

    static let encoder: JSONEncoder = {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        return encoder
    }()

    static let decoder: JSONDecoder = {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return decoder
    }()
}

/// Getting into a folder the player gave: its bookmark resolved (made again when iOS says it is stale) and its security
/// scope held while the app reads in it. What happened is written on the folder, so a lost bookmark is said in words and
/// never silently ignored (Andrei's first check of version 0.5: does Google Drive keep the folder between launches?).
enum FolderAccess {
    /// A bookmark of a folder picked in Files, made while the app may read it.
    static func bookmark(of url: URL) throws -> Data {
        try url.bookmarkData(options: .minimalBookmark, includingResourceValuesForKeys: nil, relativeTo: nil)
    }

    /// The folder's place on the iPad now, with the folder as it should be kept (a fresh bookmark, the time it opened, or
    /// what went wrong); nil for the place when the bookmark no longer opens or the folder cannot be listed.
    static func open(_ folder: ImportFolder, now: Date = Date()) -> (url: URL?, folder: ImportFolder) {
        var next = folder
        var stale = false
        guard let url = try? URL(resolvingBookmarkData: folder.bookmark, options: [], relativeTo: nil, bookmarkDataIsStale: &stale) else {
            next.problem = .lost
            return (nil, next)
        }
        let lent = url.startAccessingSecurityScopedResource()
        defer { if lent { url.stopAccessingSecurityScopedResource() } }
        if stale, let fresh = try? bookmark(of: url) {
            next.bookmark = fresh
            next.refreshedAt = now
        }
        switch listable(url) {
        case nil:
            next.problem = nil
            next.openedAt = now
            return (url, next)
        case let problem?:
            next.problem = problem
            return (nil, next)
        }
    }

    /// Opens the folder, keeps what was found on it, and runs `work` inside it while the app holds the folder's scope.
    static func within<T>(_ folder: ImportFolder, root: URL = ImportFolders.root, _ work: (URL) async -> T) async -> Result<T, FolderProblem> {
        let opened = open(folder)
        if opened.folder != folder { ImportFolders.keep(opened.folder, in: root) }
        guard let url = opened.url else { return .failure(opened.folder.problem ?? .lost) }
        let lent = url.startAccessingSecurityScopedResource()
        defer { if lent { url.stopAccessingSecurityScopedResource() } }
        return .success(await work(url))
    }

    /// Nil when the folder's own list can be read (no file is downloaded for it), else why not.
    static func listable(_ url: URL) -> FolderProblem? {
        do {
            _ = try FileManager.default.contentsOfDirectory(at: url, includingPropertiesForKeys: nil, options: [.skipsHiddenFiles])
            return nil
        } catch {
            return LinkProblem.isOffline(error as NSError) ? .offline : .unreadable
        }
    }

    /// A file inside the folder by its path there, "Шопен/Баллады/Ballade No. 1.pdf".
    static func file(_ path: String, in folder: URL) -> URL {
        path.split(separator: "/").reduce(folder) { $0.appendingPathComponent(String($1), isDirectory: false) }
    }
}
