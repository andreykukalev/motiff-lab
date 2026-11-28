import Foundation

/// The library's playlists file as the sync reads and writes it (version 1.7, p-3; docs/playlists-file.md): the library's
/// playlists record (p-4) keeps the file and implements this. The library makes its file (moving the pieces' playlists in,
/// `playlists.migrate`) before it runs a pass, so none here means the player has no playlists on this iPad yet.
protocol PlaylistsFileStore: Sendable {
    /// The playlists file here and when it was last saved here; nil while there is none.
    func current() async -> PlaylistsCopy?

    /// Writes `file` in place of `over`, the file `current` gave (nil: there was none), and only while that is still the
    /// one here, so an edit the player made while the pass ran is never written over; false when nothing was written.
    func replace(_ over: String?, with file: String) async -> Bool
}

/// The playlists file here, as `PlaylistsFileStore.current` gives it.
struct PlaylistsCopy: Equatable, Sendable {
    var file: String
    var saved: Date
}

/// What this iPad knows of the player's playlists online (v1.7 p-3), beside the environment's sync records (sync.json →
/// sync-playlists.json, sync-test.json → sync-test-playlists.json): the ETag of the online copy it last saw, the copy
/// both last agreed on (`playlists.resolve`'s "base") and whether it saw the online copy go.
struct PlaylistsSyncRecord: Codable, Equatable, Sendable {
    var etag: String?
    var base: String?
    var deletedOnline = false

    /// It synced the playlists before: it saw the online copy, or saw it go.
    var known: Bool { etag != nil || deletedOnline }

    static func url(for records: URL) -> URL {
        records.deletingLastPathComponent().appendingPathComponent(records.deletingPathExtension().lastPathComponent + "-playlists.json")
    }

    static func load(for records: URL) -> PlaylistsSyncRecord {
        guard let data = try? Data(contentsOf: url(for: records)) else { return PlaylistsSyncRecord() }
        // A record that cannot be read is none: the next pass merges the two copies with no base and loses nothing.
        return (try? JSONDecoder().decode(PlaylistsSyncRecord.self, from: data)) ?? PlaylistsSyncRecord()
    }

    func save(for records: URL) {
        try? FileManager.default.createDirectory(at: records.deletingLastPathComponent(), withIntermediateDirectories: true)
        if let data = try? JSONEncoder().encode(self) { try? data.write(to: Self.url(for: records), options: .atomic) }
    }
}

/// The playlists' step of a sync pass (v1.7 p-3; the 1.7 plan's question 2), after the pieces: the player's one playlists
/// document online (`GET`/`PUT /playlists`) and the file here go between the devices by the rules a piece's copies do
/// (music-core's `project.sync` picks the step from the same facts): changed on one side, that side's copy goes to the
/// other; changed on both, `playlists.resolve` merges them three-way against the copy both last agreed on, re-keying the
/// pieces known by other ids before, and its "file" is kept here and, when "upload" says so, sent over the copy fetched
/// (`If-Match` its ETag; 412 → merged again next pass). A later build's copy is never merged nor sent; the online copy then
/// stays as it came. Where the app may not write (`OnlineProjects.writable`), the step only fetches.
struct PlaylistsSync {
    let online: OnlineProjects
    let store: any PlaylistsFileStore
    /// The environment's sync records (`SyncRecords`); the playlists' record sits beside them.
    let recordsURL: URL

    /// The online copy as it came: its text, ETag and save time (Last-Modified).
    private typealias Fetched = (file: String, etag: String?, saved: Date?)

    /// One step. `pieces`: the library's pieces known by other ids before, [{"id", "formerIds"}] (`rekeyed`).
    func run(pieces: [[String: Any]]) async {
        let fetched: Fetched?
        switch await online.fetchPlaylists() {
        case .success(let got):
            guard let file = String(data: got.data, encoding: .utf8) else { return }
            fetched = (file, got.etag, got.saved)
        case .failure(.notFound): fetched = nil
        case .failure: return
        }
        let here = await store.current()
        var record = PlaylistsSyncRecord.load(for: recordsURL)
        let facts: [String: Any] = [
            "here": here != nil, "online": fetched != nil, "known": record.known,
            "changedHere": !record.known || here?.file != record.base,
            "changedOnline": fetched != nil && record.known && fetched?.etag != record.etag,
            "deletedOnline": record.deletedOnline, "deletedHere": false,
        ]
        switch await CloudSync.core(.projectSync, facts)?["step"] as? String {
        case "upload":
            guard let here, online.writable, await Self.sendable(here.file) else { return }
            // None online yet (or gone since): only as the first copy, so another device's is never written over.
            await send(here.file, ifMatch: fetched == nil ? nil : record.etag, &record)
        case "download":
            guard let fetched, await store.replace(here?.file, with: fetched.file) else { return }
            record = PlaylistsSyncRecord(etag: fetched.etag, base: fetched.file)
        case "resolve":
            guard let here, let fetched, online.writable else { return }
            await resolve(here, fetched, pieces: pieces, &record)
        case "forget":
            // Gone online (everything erased from another device) and unchanged here since: kept here, not sent again
            // until it changes here, as a piece is.
            record = PlaylistsSyncRecord(base: here?.file, deletedOnline: here != nil)
        default:
            return
        }
        record.save(for: recordsURL)
    }

    /// Both copies changed: music-core merges them and this iPad keeps its "file", then sends it when "upload" says so.
    /// When the file here changed meanwhile nothing is kept or sent, and the next pass merges again.
    private func resolve(_ here: PlaylistsCopy, _ fetched: Fetched, pieces: [[String: Any]], _ record: inout PlaylistsSyncRecord) async {
        var fields: [String: Any] = [
            "here": here.file, "savedHere": CloudSync.iso(here.saved),
            "online": fetched.file, "savedOnline": CloudSync.iso(fetched.saved ?? Date()),
            "pieces": pieces,
        ]
        if record.known, let base = record.base { fields["base"] = base }
        guard let answer = await CloudSync.core(.playlistsResolve, fields), let kept = answer["file"] as? String else { return }
        if kept != here.file {
            guard await store.replace(here.file, with: kept) else { return }
        }
        // The copy fetched is what both sides agree on now, until "file" goes over it.
        record = PlaylistsSyncRecord(etag: fetched.etag, base: fetched.file)
        if answer["upload"] as? Bool == true { await send(kept, ifMatch: fetched.etag, &record) }
    }

    /// Sends the file; once it is online it is what both sides agree on. Refused (412: changed online since) or failed,
    /// the record stays, so the next pass fetches and merges again, or sends again.
    private func send(_ file: String, ifMatch: String?, _ record: inout PlaylistsSyncRecord) async {
        guard case .saved(let etag) = await online.uploadPlaylists(file, ifMatch: ifMatch) else { return }
        record = PlaylistsSyncRecord(etag: etag, base: file)
    }

    /// Whether the file may go online: music-core reads it, and it is not a later build's (read-only, never sent).
    private static func sendable(_ file: String) async -> Bool {
        await CloudSync.core(.playlists, ["file": file])?["readOnly"] as? Bool == false
    }

    /// The pieces in the folder known by other ids before (`formerIds`: re-keyed, or merged under the online id, #317), as
    /// [{"id", "formerIds"}], read after the pass changed the files, so the merge keeps them on their playlists.
    static func rekeyed(in folder: URL) async -> [[String: Any]] {
        var rows: [[String: Any]] = []
        for entry in ProjectFiles.list(in: folder) {
            guard let summary = await PieceSummary.read(entry.url), let id = summary.id, !summary.formerIds.isEmpty else { continue }
            rows.append(["id": id, "formerIds": summary.formerIds])
        }
        return rows
    }
}
