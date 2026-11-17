import CryptoKit
import Foundation

/// What this iPad knows of one project's online copy (version 0.2 sync), kept beside the projects and never in the project
/// file: the ETag and save time of the online copy it last saw, the hash of the project file as it was then, and whether
/// the online copy was seen deleted or found no room in a free account.
struct SyncRecord: Codable, Equatable, Sendable {
    var etag: String?
    var savedAt: String?
    var hash: String?
    var deletedOnline = false
    var overQuota = false

    /// The iPad synced it before: it saw the online copy, or saw it deleted.
    var known: Bool { etag != nil || deletedOnline }
}

/// The records, by project id, in one file in Application Support (not in Documents, so the Files app does not show it),
/// one file for each server environment: sync.json for Main, sync-test.json for Test (`ServerEnvironment`).
enum SyncRecords {
    static var url: URL {
        EnvironmentSetup.current().syncRecordsURL
    }

    static func load(from url: URL = url) -> [String: SyncRecord] {
        guard let data = try? Data(contentsOf: url) else { return [:] }
        // Records that cannot be read are no records: the next pass compares the two copies and keeps both.
        return (try? JSONDecoder().decode([String: SyncRecord].self, from: data)) ?? [:]
    }

    static func save(_ records: [String: SyncRecord], to url: URL = url) {
        try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        if let data = try? JSONEncoder().encode(records) { try? data.write(to: url, options: .atomic) }
    }
}

/// Sync between the player's devices (version 0.2; Andrei, 2026-11-11), as the web app does it (Account/CloudSync.cs):
/// new pieces and their changes go between the iPad and the web by themselves, and when both changed one, the last save
/// wins and the previous copy is kept beside as a piece of its own. One pass compares each project file here with the
/// API's list by music-core's rules (`project.sync`, `ProjectSync`): it sends what changed here (the copy without the PDF,
/// `project.online`), fetches what changed online or is new there (keeping this iPad's PDF, `project.pulled`), and
/// resolves a change on both (`project.resolve`). A piece that is only a PDF stays here (0026 C3). The library runs it
/// when it opens, comes back to the screen, after a piece closes and on sign-in, never inside a piece. Where the app may
/// not write (Main in a version build, `OnlineProjects.writable`) a pass only fetches: it sends nothing and leaves a piece
/// changed on both sides as it is here, to be resolved once writing is allowed.
struct CloudSync {
    /// The Settings switch (`account.saveOnline`), on until the player turns it off (0014 item 13 as 0.2 amends it).
    static let onKey = "account.saveOnline"

    let online: OnlineProjects
    var folder: URL = ProjectFiles.folder
    var recordsURL: URL = SyncRecords.url
    /// The title of a previous copy made at a time, "{0} (previous copy, 11/11/26, 20:40)", "{0}" standing for the piece's title.
    let copyTitle: (Date) -> String

    struct Outcome: Equatable, Sendable {
        /// A project file here was added or changed, so the library reads them again.
        var changed = false
        /// How many pieces stay here because the free account's room online is full.
        var overQuota = 0
        /// The player cleared everything from another device (`TestReset`): the pass synced nothing, and the library
        /// clears Test's files here before the next one (`TestLibrary.applyReset`).
        var reset: Int64?
    }

    /// A project file here as the pass sees it: its bytes are read again only for the piece being sent or merged, so a
    /// pass never holds the whole library in memory (core and iPad review v0.6.5, finding 4).
    private struct Local {
        let url: URL
        let hash: String
        let modified: Date

        var data: Data? { try? Data(contentsOf: url) }
    }

    /// One pass; nothing happens while signed out or offline, or when everything was cleared from another device since
    /// this iPad last applied a reset: then nothing here is sent, and the outcome says so.
    func run() async -> Outcome {
        guard let listing = await online.listing() else { return Outcome() }
        if let reset = listing.resetDue { return Outcome(reset: reset) }
        let rows = listing.projects
        let listed = Dictionary(rows.map { ($0.id, $0) }, uniquingKeysWith: { first, _ in first })
        var here: [String: Local] = [:]
        // The most recently changed file first, so a copy made in Files does not stand for the piece.
        for entry in ProjectFiles.list(in: folder) {
            guard let data = try? Data(contentsOf: entry.url), let id = await PieceSummary.read(data)?.id, here[id] == nil else { continue }
            here[id] = Local(url: entry.url, hash: Self.hash(data), modified: entry.modified)
        }
        var records = SyncRecords.load(from: recordsURL)
        var outcome = Outcome()
        for id in Set(here.keys).union(listed.keys).union(records.keys).sorted() {
            outcome.changed = await sync(id, here[id], listed[id], &records, roomy: rows.count < Self.freeProjectsOnline) || outcome.changed
            // A piece that is not synced (only a PDF) keeps no record.
            if records[id] == SyncRecord() { records[id] = nil }
        }
        for id in await join(&records) {
            here[id] = nil
            outcome.changed = true
        }
        SyncRecords.save(records, to: recordsURL)
        outcome.overQuota = records.filter { here[$0.key] != nil && $0.value.overQuota }.count
        return outcome
    }

    /// How many pieces a free account keeps online (`Plans.FreeProjectsOnline` in music-core, 0025 section 8).
    static let freeProjectsOnline = 300

    private func sync(_ id: String, _ local: Local?, _ row: OnlineProject?, _ records: inout [String: SyncRecord], roomy: Bool) async -> Bool {
        let record = records[id] ?? SyncRecord()
        let hash = local.map(\.hash)
        let facts: [String: Any] = [
            "here": local != nil, "online": row != nil, "known": record.known,
            "changedHere": !record.known || hash != record.hash,
            "changedOnline": row != nil && record.known && record.savedAt != row?.savedAt,
            "deletedOnline": record.deletedOnline,
        ]
        switch await Self.core("project.sync", facts)?["step"] as? String {
        case "upload":
            guard let local, online.writable else { return false }
            // Not sent again over the quota until it changes here or the account has room: the library says so meanwhile.
            if record.overQuota && hash == record.hash && !roomy { return false }
            records[id] = await upload(id, local, ifMatch: row == nil ? nil : record.etag, record)
            return false
        case "download":
            guard let row else { return false }
            return await download(id, local, row, &records)
        case "resolve":
            // Resolving may send this iPad's copy, and a copy kept beside would be made again on every pass: not while read only.
            guard let local, let row, online.writable else { return false }
            return await resolve(id, local, row, &records)
        case "forget":
            records[id] = local == nil ? nil : SyncRecord(hash: hash, deletedOnline: true)
            return false
        default:
            return false
        }
    }

    /// Sends the copy without the PDF; a piece that is only a PDF is not sent.
    private func upload(_ id: String, _ local: Local, ifMatch: String?, _ record: SyncRecord) async -> SyncRecord {
        guard let data = local.data, let file = String(data: data, encoding: .utf8),
              let copy = await Self.core("project.online", ["file": file])?["file"] as? String
        else { return record }
        var noted = record
        switch await online.upload(id, file: copy, ifMatch: ifMatch) {
        case .saved(let etag):
            return SyncRecord(etag: etag, savedAt: nil, hash: Self.hash(data))
        case .overQuota:
            noted.overQuota = true
            noted.hash = Self.hash(data)
        case .changedOnline:
            // Changed on another device since this one saw it: the next pass fetches it and keeps both.
            noted.savedAt = nil
        case .failed:
            break
        }
        return noted
    }

    private func download(_ id: String, _ local: Local?, _ row: OnlineProject, _ records: inout [String: SyncRecord]) async -> Bool {
        guard case .success(let fetched) = await online.fetch(id), let remote = String(data: fetched.data, encoding: .utf8),
              await PieceSummary.read(fetched.data)?.id == id
        else { return false }
        var record = records[id] ?? SyncRecord()
        if let etag = fetched.etag, etag == record.etag {
            // This iPad's own save coming back: only its time online was not known yet.
            record.savedAt = row.savedAt
            records[id] = record
            return false
        }
        var fields: [String: Any] = ["file": remote]
        if let data = local?.data, let text = String(data: data, encoding: .utf8) { fields["here"] = text }
        guard let kept = await Self.core("project.pulled", fields)?["file"] as? String,
              write(kept, to: local?.url, title: row.title) != nil
        else { return false }
        records[id] = SyncRecord(etag: fetched.etag, savedAt: row.savedAt, hash: Self.hash(Data(kept.utf8)))
        return true
    }

    private func resolve(_ id: String, _ local: Local, _ row: OnlineProject, _ records: inout [String: SyncRecord]) async -> Bool {
        guard case .success(let fetched) = await online.fetch(id), let remote = String(data: fetched.data, encoding: .utf8),
              let data = local.data, let here = String(data: data, encoding: .utf8)
        else { return false }
        let record = records[id] ?? SyncRecord()
        if let etag = fetched.etag, etag == record.etag {
            // The online copy is the one this iPad sent last, so only this iPad changed it.
            records[id] = await upload(id, local, ifMatch: etag, record)
            return false
        }
        let answer = await Self.core("project.resolve", [
            "here": here, "savedHere": Self.iso(local.modified),
            "online": remote, "savedOnline": row.savedAt ?? Self.iso(Date()),
            "copyTitle": copyTitle(Date()),
        ])
        guard let answer, let kept = answer["file"] as? String else { return false }
        var changed = false
        if let copy = answer["copy"] as? String {
            // Saved as any other piece, so the next pass sends it online too and nothing is lost on either device.
            changed = write(copy, to: nil, title: await PieceSummary.read(Data(copy.utf8))?.title ?? row.title) != nil
        }
        if answer["upload"] as? Bool == true {
            records[id] = await upload(id, local, ifMatch: fetched.etag, record)
        } else {
            if kept != here { changed = write(kept, to: local.url, title: row.title) != nil || changed }
            records[id] = SyncRecord(etag: fetched.etag, savedAt: row.savedAt, hash: Self.hash(Data(kept.utf8)))
        }
        return changed
    }

    /// One piece, one entry (version 0.6.1, #582 item 1), as the web app does it (Account/CloudSync.cs): the project files
    /// here that are the same piece by the files they were made from (`pieceKeys`, grouped by `project.same`), such as the
    /// same PDF added on the web and on this iPad, become one when one of them carries nothing of the player's own
    /// (`project.join`, music-core's `SamePiece`). The other is written in place with its PDF and details, and the copy that
    /// joined it is deleted here and online, so no device fetches it again; where the app may not write online, what this
    /// iPad knows of that online copy stays, so this iPad does not fetch it again either. A copy whose bytes are kept beside
    /// its file on this iPad (a linked PDF, a copy for playing offline, a recording's sound) stays, though another may join
    /// it. Answers the ids of the projects that went.
    private func join(_ records: inout [String: SyncRecord]) async -> [String] {
        var pieces: [(url: URL, id: String, keys: [String])] = []
        // The most recently changed file first, as the pass reads them.
        for entry in ProjectFiles.list(in: folder) {
            guard let summary = await PieceSummary.read(entry.url), let id = summary.id, !pieces.contains(where: { $0.id == id }) else { continue }
            pieces.append((url: entry.url, id: id, keys: summary.pieceKeys))
        }
        let rows: [[String: Any]] = pieces.filter { !$0.keys.isEmpty }.map { ["id": $0.id, "keys": $0.keys] }
        guard rows.count > 1, let groups = await Self.core("project.same", ["pieces": rows])?["groups"] as? [[String]] else { return [] }
        var gone: [String] = []
        for group in groups {
            let members = group.compactMap { id in pieces.first { $0.id == id } }
            let files = members.compactMap { try? String(contentsOf: $0.url, encoding: .utf8) }
            guard members.count > 1, files.count == members.count else { continue }
            let stay = members.indices.filter { Self.keptBeside(members[$0].id) }
            guard let joined = await Self.core("project.join", ["files": files, "stay": stay])?["joined"] as? [[String: Any]] else { continue }
            for piece in joined {
                guard let keeper = (piece["keeper"] as? NSNumber)?.intValue, members.indices.contains(keeper),
                      let file = piece["file"] as? String, write(file, to: members[keeper].url, title: members[keeper].id) != nil
                else { continue }
                for index in (piece["from"] as? [NSNumber] ?? []).map(\.intValue) where members.indices.contains(index) && index != keeper {
                    let id = members[index].id
                    do {
                        try FileManager.default.removeItem(at: members[index].url)
                    } catch {
                        continue
                    }
                    gone.append(id)
                    if online.writable, await online.delete(id) { records[id] = nil }
                }
            }
        }
        return gone
    }

    /// Whether the project has bytes kept beside its file on this iPad: a linked PDF, a copy of one kept for playing with no
    /// connection, or a recording's sound.
    private static func keptBeside(_ id: String) -> Bool {
        let links = OriginalLinks.folder.appendingPathComponent(OriginalLinks.safe(id), isDirectory: true)
        return FileManager.default.fileExists(atPath: links.path) || KeptCopies.has(project: id) || !OriginalSounds.ids(project: id).isEmpty
    }

    /// Writes a project file in place, or as a new file named by its title; nil when it could not be written.
    private func write(_ file: String, to url: URL?, title: String) -> URL? {
        let target = url ?? ProjectFiles.newURL(for: title, in: folder)
        do {
            try Data(file.utf8).write(to: target, options: .atomic)
            return target
        } catch {
            return nil
        }
    }

    /// A call on music-core's piece 0 with JSON fields; nil when it answers an error.
    private static func core(_ name: String, _ fields: [String: Any]) async -> [String: Any]? {
        let request = facadeRequest(fields)
        let answer = await MusicCore.run { MusicCore.call(piece: 0, name, request) }
        guard let object = facadeObject(answer), object["error"] == nil else { return nil }
        return object
    }

    static func hash(_ data: Data) -> String {
        SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
    }

    private static func iso(_ date: Date) -> String {
        ISO8601DateFormatter().string(from: date)
    }
}
