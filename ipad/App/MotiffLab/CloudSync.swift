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

/// The records, by project id, in one file in Application Support (not in Documents, so the Files app does not show it).
enum SyncRecords {
    static var url: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("sync.json")
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
/// when it opens, comes back to the screen, after a piece closes and on sign-in, never inside a piece.
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
    }

    /// A project file here as the pass sees it.
    private struct Local {
        let url: URL
        let data: Data
        let modified: Date
    }

    /// One pass; nothing happens while signed out or offline.
    func run() async -> Outcome {
        guard let rows = await online.list() else { return Outcome() }
        let listed = Dictionary(rows.map { ($0.id, $0) }, uniquingKeysWith: { first, _ in first })
        var here: [String: Local] = [:]
        // The most recently changed file first, so a copy made in Files does not stand for the piece.
        for entry in ProjectFiles.list(in: folder) {
            guard let data = try? Data(contentsOf: entry.url), let id = await PieceSummary.read(data)?.id, here[id] == nil else { continue }
            here[id] = Local(url: entry.url, data: data, modified: entry.modified)
        }
        var records = SyncRecords.load(from: recordsURL)
        var outcome = Outcome()
        for id in Set(here.keys).union(listed.keys).union(records.keys).sorted() {
            outcome.changed = await sync(id, here[id], listed[id], &records, roomy: rows.count < Self.freeProjectsOnline) || outcome.changed
            // A piece that is not synced (only a PDF) keeps no record.
            if records[id] == SyncRecord() { records[id] = nil }
        }
        SyncRecords.save(records, to: recordsURL)
        outcome.overQuota = records.filter { here[$0.key] != nil && $0.value.overQuota }.count
        return outcome
    }

    /// How many pieces a free account keeps online (`Plans.FreeProjectsOnline` in music-core, 0025 section 8).
    static let freeProjectsOnline = 300

    private func sync(_ id: String, _ local: Local?, _ row: OnlineProject?, _ records: inout [String: SyncRecord], roomy: Bool) async -> Bool {
        let record = records[id] ?? SyncRecord()
        let hash = local.map { Self.hash($0.data) }
        let facts: [String: Any] = [
            "here": local != nil, "online": row != nil, "known": record.known,
            "changedHere": !record.known || hash != record.hash,
            "changedOnline": row != nil && record.known && record.savedAt != row?.savedAt,
            "deletedOnline": record.deletedOnline,
        ]
        switch await Self.core("project.sync", facts)?["step"] as? String {
        case "upload":
            guard let local else { return false }
            // Not sent again over the quota until it changes here or the account has room: the library says so meanwhile.
            if record.overQuota && hash == record.hash && !roomy { return false }
            records[id] = await upload(id, local, ifMatch: row == nil ? nil : record.etag, record)
            return false
        case "download":
            guard let row else { return false }
            return await download(id, local, row, &records)
        case "resolve":
            guard let local, let row else { return false }
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
        guard let file = String(data: local.data, encoding: .utf8),
              let copy = await Self.core("project.online", ["file": file])?["file"] as? String
        else { return record }
        var noted = record
        switch await online.upload(id, file: copy, ifMatch: ifMatch) {
        case .saved(let etag):
            return SyncRecord(etag: etag, savedAt: nil, hash: Self.hash(local.data))
        case .overQuota:
            noted.overQuota = true
            noted.hash = Self.hash(local.data)
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
        if let local, let text = String(data: local.data, encoding: .utf8) { fields["here"] = text }
        guard let kept = await Self.core("project.pulled", fields)?["file"] as? String,
              write(kept, to: local?.url, title: row.title) != nil
        else { return false }
        records[id] = SyncRecord(etag: fetched.etag, savedAt: row.savedAt, hash: Self.hash(Data(kept.utf8)))
        return true
    }

    private func resolve(_ id: String, _ local: Local, _ row: OnlineProject, _ records: inout [String: SyncRecord]) async -> Bool {
        guard case .success(let fetched) = await online.fetch(id), let remote = String(data: fetched.data, encoding: .utf8),
              let here = String(data: local.data, encoding: .utf8)
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
        guard let body = try? JSONSerialization.data(withJSONObject: fields) else { return nil }
        let request = String(decoding: body, as: UTF8.self)
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
