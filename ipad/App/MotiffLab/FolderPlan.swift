import Foundation

/// What the folders become in the library, the player's choice on «Что нашлось» (music-core's `FolderMapping`).
enum FolderMapping: String, CaseIterable, Codable, Sendable {
    case playlists, tags, composers, nothing
}

/// A suggested title or composer: a guess until the player adds the piece with it, and where it comes from ("fileName",
/// "folder", "pdfInfo", "scoreHeader", "folderChoice").
struct Suggested: Equatable, Sendable {
    let value: String
    let from: String
}

/// One found file in music-core's import plan (`library.plan`): what the import does with it and what its piece is
/// suggested to be.
struct PlannedFile: Identifiable, Equatable, Sendable {
    let path: String
    /// "new", "inLibrary", "duplicate", "unsupported", "tooBig" or "unreadable".
    let status: String
    /// For a duplicate, the earlier file with its fingerprint.
    let sameAs: String?
    let title: Suggested?
    let composer: Suggested?
    let playlists: [String]
    let tags: [String]
    /// A PDF of more than 20 pages: perhaps several pieces in one score.
    let collection: Bool
    /// Another file or a piece of the library with the same title and composer: possibly the same piece, never merged.
    let possiblySame: Bool

    var id: String { path }
    var isNew: Bool { status == "new" }
    var isUnsuitable: Bool { status == "unsupported" || status == "tooBig" || status == "unreadable" }

    init?(_ object: [String: Any]) {
        guard let path = object["path"] as? String, let status = object["status"] as? String else { return nil }
        self.path = path
        self.status = status
        sameAs = object["sameAs"] as? String
        title = Self.suggested(object["title"])
        composer = Self.suggested(object["composer"])
        playlists = object["playlists"] as? [String] ?? []
        tags = object["tags"] as? [String] ?? []
        collection = object["collection"] as? Bool ?? false
        let same = object["possiblySame"] as? [String: Any]
        possiblySame = !((same?["files"] as? [String] ?? []).isEmpty && (same?["pieces"] as? [String] ?? []).isEmpty)
    }

    private static func suggested(_ value: Any?) -> Suggested? {
        guard let object = value as? [String: Any], let text = object["value"] as? String else { return nil }
        return Suggested(value: text, from: object["from"] as? String ?? "fileName")
    }
}

/// The import plan of a folder as music-core answers it: every found file in path order, the files to fingerprint before
/// the plan is sure, and the counts the screen's filters show.
struct FolderPlan: Equatable, Sendable {
    var files: [PlannedFile] = []
    var fingerprint: [String] = []
    var counts = Counts()

    struct Counts: Equatable, Sendable {
        var found = 0
        var new = 0
        var inLibrary = 0
        var duplicates = 0
        var unsuitable = 0
    }

    init() {}

    init?(answer: String) {
        guard let object = facadeObject(answer), object["error"] == nil else { return nil }
        files = (object["files"] as? [[String: Any]] ?? []).compactMap(PlannedFile.init)
        fingerprint = object["fingerprint"] as? [String] ?? []
        let numbers = object["counts"] as? [String: Any] ?? [:]
        counts = Counts(
            found: Self.number(numbers["found"]), new: Self.number(numbers["new"]), inLibrary: Self.number(numbers["inLibrary"]),
            duplicates: Self.number(numbers["duplicates"]), unsuitable: Self.number(numbers["unsuitable"])
        )
    }

    private static func number(_ value: Any?) -> Int { (value as? NSNumber)?.intValue ?? 0 }
}

/// Where one of the library's linked originals is in a fresh listing of its folder (`library.relink`).
struct RelinkedOriginal: Equatable, Sendable {
    let project: String
    let original: String
    /// "linked", "unconfirmed", "moved", "pending", "changed" or "missing".
    let status: String
    let path: String?
    /// The files that are possibly the same piece, the likeliest first.
    let candidates: [String]

    /// Linked where it is now, with no question.
    var isFound: Bool { status == "linked" || status == "unconfirmed" || status == "moved" }
}

/// The calls of the folder import on music-core (`library.plan`, `library.relink`, `library.confirm`): the files found as
/// the plan takes them, and the library's originals, from the cards' summaries.
enum FolderPlanning {
    /// A found file as music-core takes it, with what was read of it.
    static func found(_ file: FolderFile, _ facts: FileFacts?) -> [String: Any] {
        var object: [String: Any] = ["path": file.path, "bytes": file.bytes]
        guard let facts else { return object }
        if let sha256 = facts.sha256 { object["sha256"] = sha256 }
        if let pages = facts.pages { object["pages"] = pages }
        if let title = facts.pdfTitle { object["pdfTitle"] = title }
        if let author = facts.pdfAuthor { object["pdfAuthor"] = author }
        if let musicXml = facts.musicXml { object["musicXml"] = musicXml }
        if facts.unreadable { object["unreadable"] = true }
        return object
    }

    /// The library's originals, each with its piece's title and composer: by fingerprint music-core knows a file the
    /// library has. `known` adds the fingerprints this iPad kept of files it imported whose piece does not say it (a MIDI
    /// or MusicXML file's notes), by project.
    static func library(_ summaries: [PieceSummary], known: [String: String] = [:]) -> [[String: Any]] {
        var originals: [[String: Any]] = []
        for summary in summaries {
            guard let project = summary.id else { continue }
            for original in summary.originals {
                var object: [String: Any] = ["project": project, "original": original.id, "fileName": original.fileName]
                if let sha256 = original.sha256 ?? known[project] { object["sha256"] = sha256 }
                if let bytes = original.bytes { object["bytes"] = bytes }
                if let pages = original.pages, pages > 0 { object["pages"] = pages }
                if let folder = original.folder { object["folder"] = folder }
                if let title = summary.title { object["title"] = title }
                if let composer = summary.composer { object["composer"] = composer }
                originals.append(object)
            }
        }
        return originals
    }

    static func plan(_ files: [FolderFile], facts: [String: FileFacts], library: [[String: Any]], folders: FolderMapping, linked: Bool) async -> FolderPlan? {
        let request: [String: Any] = [
            "files": files.map { found($0, facts[$0.path]) },
            "library": library,
            "folders": folders.rawValue,
            "linked": linked,
        ]
        guard let answer = await call("library.plan", request) else { return nil }
        return FolderPlan(answer: answer)
    }

    /// Where the library's originals from this folder are now; nil when music-core refused.
    static func relink(_ library: [[String: Any]], files: [FolderFile], facts: [String: FileFacts]) async -> (originals: [RelinkedOriginal], fingerprint: [String])? {
        let request: [String: Any] = ["library": library, "files": files.map { found($0, facts[$0.path]) }]
        guard let answer = await call("library.relink", request), let object = facadeObject(answer), object["error"] == nil else { return nil }
        let originals = (object["originals"] as? [[String: Any]] ?? []).compactMap { each -> RelinkedOriginal? in
            guard let project = each["project"] as? String, let original = each["original"] as? String, let status = each["status"] as? String else { return nil }
            let candidates = (each["candidates"] as? [[String: Any]] ?? []).compactMap { $0["path"] as? String }
            return RelinkedOriginal(project: project, original: original, status: status, path: each["path"] as? String, candidates: candidates)
        }
        return (originals, object["fingerprint"] as? [String] ?? [])
    }

    /// The piece's metadata once the player added it with the suggestions as they stood (`library.confirm`): the title and
    /// composer fill only what the player had not set, the tags and playlists are added. The project file to save.
    static func confirm(_ file: String, title: String?, composer: String?, tags: [String], playlists: [String]) async -> (file: String, project: String)? {
        var request: [String: Any] = ["file": file, "tags": tags, "playlists": playlists]
        if let title, !title.isEmpty { request["title"] = title }
        if let composer, !composer.isEmpty { request["composer"] = composer }
        guard let answer = await call("library.confirm", request), let object = facadeObject(answer),
              let changed = object["file"] as? String, let project = (object["project"] as? [String: Any])?["id"] as? String else { return nil }
        return (changed, project)
    }

    /// One call on piece 0; nil for an error.
    static func call(_ name: String, _ request: [String: Any]) async -> String? {
        guard let body = try? JSONSerialization.data(withJSONObject: request) else { return nil }
        let json = String(decoding: body, as: UTF8.self)
        let answer = await MusicCore.run { MusicCore.call(piece: 0, name, json) }
        guard let object = facadeObject(answer), object["error"] == nil else { return nil }
        return answer
    }
}
