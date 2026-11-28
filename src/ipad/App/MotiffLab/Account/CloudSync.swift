import CryptoKit
import Foundation

/// What this iPad knows of one project's online copy (version 0.2 sync), kept beside the projects and never in the project
/// file: the ETag and save time of the online copy it last saw, the hash of the project file as it was then, whether
/// the online copy was seen deleted or found no room in a free account, and whether the player deleted the piece here.
struct SyncRecord: Codable, Equatable, Sendable {
    var etag: String?
    var savedAt: String?
    var hash: String?
    var deletedOnline = false
    var overQuota = false
    /// The player deleted the piece here (the library's Delete, v1.6 c-17b): the next pass deletes its online copy with the
    /// ETag above, unless it changed online since (`project.sync` "deleteOnline"). Optional, so that the records an earlier
    /// build wrote, without it, still read: a key missing for a plain field makes the whole file unreadable.
    var deletedHere: Bool?

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

/// The copy of each synced piece that this iPad and the online copy last agreed on (v1.6 c-17b, 0026 S5): the copy it sent
/// last (`project.online`'s, without the PDF) or fetched last, the base `project.resolve` merges two changed copies
/// against, so that what one side removed since goes and the marks on a PDF's pages merge mark by mark. One file per project
/// beside the environment's records, in a folder named after them (`sync-bases` for sync.json, `sync-test-bases` for
/// sync-test.json), kept only while the piece has a record.
enum SyncBases {
    static func folder(for records: URL) -> URL {
        records.deletingLastPathComponent().appendingPathComponent(records.deletingPathExtension().lastPathComponent + "-bases", isDirectory: true)
    }

    static func has(_ id: String, records: URL) -> Bool {
        FileManager.default.fileExists(atPath: url(id, records: records).path)
    }

    static func load(_ id: String, records: URL) -> String? {
        try? String(contentsOf: url(id, records: records), encoding: .utf8)
    }

    static func save(_ file: String, for id: String, records: URL) {
        try? FileManager.default.createDirectory(at: folder(for: records), withIntermediateDirectories: true)
        try? Data(file.utf8).write(to: url(id, records: records), options: .atomic)
    }

    /// Removes the bases of the pieces with no record left (deleted online, forgotten, joined into another).
    static func prune(keeping ids: [String], records: URL) {
        let folder = folder(for: records)
        let kept = Set(ids.map { url($0, records: records).lastPathComponent })
        for name in (try? FileManager.default.contentsOfDirectory(atPath: folder.path)) ?? [] where !kept.contains(name) {
            try? FileManager.default.removeItem(at: folder.appendingPathComponent(name))
        }
    }

    private static func url(_ id: String, records: URL) -> URL {
        folder(for: records).appendingPathComponent(OriginalLinks.safe(id) + ".json")
    }
}

/// Sync between the player's iPads through the Motiff Lab API (version 0.2; Andrei, 2026-11-11; the web app, which
/// synced the same way, is archived, 0032): new pieces and their changes go between the devices by themselves, and when
/// both changed one, music-core merges the two copies entity by entity, and what of the other the merge does not hold
/// is kept beside as a piece of its own. One pass compares each project file here with the API's list by music-core's
/// rules (`project.sync`, `ProjectSync`): it sends what changed here (the copy without the PDF and without a
/// recording's sound, `project.online`), a piece new here only as a new one online (no `If-Match`), fetches what
/// changed online or is new there (keeping this iPad's PDF, `project.pulled`), and resolves a change on both
/// (`project.resolve` against the copy both last agreed on, `SyncBases`; v1.6 c-17b). A piece the player deletes here
/// goes online too, unless it changed online since (`markDeletedHere`, the 1.6 plan's decisions 14 and 26). A later
/// build's copy (project file 18) is kept read-only and never sent: one online stays the piece and this iPad's goes
/// beside as a new piece (decision 39), one here goes beside and the online copy takes the piece (decision 36). The
/// library runs it when it opens, comes back to the screen, after a piece closes and on sign-in, never inside a piece.
/// After the pieces it syncs the library's playlists file, when the library gives one (`PlaylistsSync`, v1.7 p-3).
/// Where the app may not write (Main in a version build, `OnlineProjects.writable`) a pass only fetches: it sends
/// nothing and leaves a piece changed on both sides as it is here, to be resolved once writing is allowed.
struct CloudSync {
    /// The Settings switch (`account.saveOnline`), on until the player turns it off (0014 item 13 as 0.2 amends it).
    static let onKey = "account.saveOnline"

    let online: OnlineProjects
    var folder: URL = ProjectFiles.folder
    var recordsURL: URL = SyncRecords.url
    /// The library's playlists file (p-4's record): after the pieces the pass syncs it too (`PlaylistsSync`, v1.7 p-3); none
    /// until the library gives one.
    var playlists: (any PlaylistsFileStore)?
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
        /// The pieces resolved with a conflict in this pass (v1.6 c-17b), for the library to say so.
        var conflicts: [SyncConflict] = []
    }

    /// A project file here as the pass sees it: its bytes are read again only for the piece being sent or merged, so a
    /// pass never holds the whole library in memory (core and iPad review v0.6.5, finding 4).
    private struct Local {
        let url: URL
        let hash: String
        let modified: Date
        /// A later build's copy, kept here read-only (project file 18): never sent, as `project.online` refuses it.
        var readOnly = false

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
            guard let data = try? Data(contentsOf: entry.url), let summary = await PieceSummary.read(data), let id = summary.id, here[id] == nil else { continue }
            here[id] = Local(url: entry.url, hash: Self.hash(data), modified: entry.modified, readOnly: summary.readOnly)
        }
        var records = SyncRecords.load(from: recordsURL)
        var outcome = Outcome()
        for id in Set(here.keys).union(listed.keys).union(records.keys).sorted() {
            await sync(id, here[id], listed[id], &records, &outcome, roomy: rows.count < Self.freeProjectsOnline)
            // A piece that is not synced (only a PDF) keeps no record.
            if records[id] == SyncRecord() { records[id] = nil }
        }
        for id in await join(&records) {
            here[id] = nil
            outcome.changed = true
            // The copy that joined another goes online in this pass, as one the player deleted; after a failure its mark
            // sends it with the next.
            if online.writable, let record = records[id] { await deleteOnline(id, record, &records) }
        }
        // A piece the library deleted while this pass ran keeps its mark for the next one (`markDeletedHere`, E-sync-1;
        // one marked between this read and the write below is not, a known limit there).
        for (id, mark) in SyncRecords.load(from: recordsURL) where mark.deletedHere == true && here[id] != nil {
            records[id]?.deletedHere = true
        }
        SyncRecords.save(records, to: recordsURL)
        SyncBases.prune(keeping: Array(records.keys), records: recordsURL)
        if let playlists {
            await PlaylistsSync(online: online, store: playlists, recordsURL: recordsURL).run(pieces: await PlaylistsSync.rekeyed(in: folder))
        }
        outcome.overQuota = records.filter { here[$0.key] != nil && $0.value.overQuota }.count
        return outcome
    }

    /// How many pieces a free account keeps online (`Plans.FreeProjectsOnline` in music-core, 0025 section 8).
    static let freeProjectsOnline = 300

    private func sync(_ id: String, _ local: Local?, _ row: OnlineProject?, _ records: inout [String: SyncRecord], _ outcome: inout Outcome, roomy: Bool) async {
        let record = records[id] ?? SyncRecord()
        let hash = local.map(\.hash)
        let facts: [String: Any] = [
            "here": local != nil, "online": row != nil, "known": record.known,
            "changedHere": !record.known || hash != record.hash,
            "changedOnline": row != nil && record.known && record.savedAt != row?.savedAt,
            "deletedOnline": record.deletedOnline,
            "deletedHere": local == nil && record.deletedHere == true,
        ]
        switch await Self.core(.projectSync, facts)?["step"] as? String {
        case "upload":
            // A later build's copy is never sent: project.online refuses it, so it is not asked again every pass (E-sync-5).
            guard let local, !local.readOnly, online.writable else { return }
            // Not sent again over the quota until it changes here or the account has room: the library says so meanwhile.
            if record.overQuota && hash == record.hash && !roomy { return }
            // A piece not online yet goes only as a new one: without If-Match the API never writes over a copy it keeps (#321).
            records[id] = await upload(id, local, ifMatch: row == nil ? nil : record.etag, record)
        case "download":
            guard let row else { return }
            if await download(id, local, row, &records) { outcome.changed = true }
        case "resolve":
            // Resolving may send this iPad's copy, and a copy kept beside would be made again on every pass: not while read only.
            guard let local, let row, online.writable else { return }
            if await resolve(id, local, row, &records, &outcome.conflicts) { outcome.changed = true }
        case "deleteOnline":
            guard online.writable else { return }
            await deleteOnline(id, record, &records)
        case "forget":
            records[id] = local == nil ? nil : SyncRecord(hash: hash, deletedOnline: true)
        default:
            await seedBase(id, local, row, record)
        }
    }

    /// A piece synced by a build before c-17b has no base (`SyncBases`): while neither side changed it since, its copy here
    /// as it goes online is the one both hold, so the next merge has a base too.
    private func seedBase(_ id: String, _ local: Local?, _ row: OnlineProject?, _ record: SyncRecord) async {
        guard let local, !local.readOnly, let row, record.etag != nil, local.hash == record.hash, row.savedAt == record.savedAt,
              !SyncBases.has(id, records: recordsURL), let sent = await Self.sent(local)
        else { return }
        SyncBases.save(sent.copy, for: id, records: recordsURL)
    }

    /// The online copy of a piece the player deleted here, only as this iPad last saw it (`If-Match`): once it is gone (or
    /// was gone already) the record goes; saved on another device since (412), the record stays, so the next pass fetches
    /// it again and nothing another device saved is lost (the 1.6 plan's decision 26).
    private func deleteOnline(_ id: String, _ record: SyncRecord, _ records: inout [String: SyncRecord]) async {
        guard let etag = record.etag else { return }
        if await online.delete(id, ifMatch: etag) == .gone { records[id] = nil }
    }

    /// Marks a piece the player deleted here (the library's Delete) for the next pass to delete online. It writes the
    /// environment's records: a pass under way keeps the mark (`run`), also for a piece it sends for the first time; on a
    /// piece this iPad never synced the next pass forgets it ("forget").
    static func markDeletedHere(_ id: String, recordsURL: URL = SyncRecords.url) {
        var records = SyncRecords.load(from: recordsURL)
        records[id, default: SyncRecord()].deletedHere = true
        SyncRecords.save(records, to: recordsURL)
    }

    /// The projects listed online to offer as «Нет на этом iPad»: in no project file here, and not deleted here with their
    /// online copy still to go (E-sync-2).
    static func offered(_ listed: [OnlineProject], here: Set<String>, recordsURL: URL = SyncRecords.url) -> [OnlineProject] {
        let deleting = SyncRecords.load(from: recordsURL).filter { $0.value.deletedHere == true }.keys
        return OnlineProjects.notHere(listed, here: here.union(deleting))
    }

    /// The hash of the project file here and the copy of it that goes online (`project.online`: without the PDF); nil for a
    /// piece that is only a PDF, or a later build's.
    private static func sent(_ local: Local) async -> (hash: String, copy: String)? {
        guard let data = local.data, let file = String(data: data, encoding: .utf8),
              let copy = await core(.projectOnline, ["file": file])?["file"] as? String
        else { return nil }
        return (hash(data), copy)
    }

    /// Sends the copy without the PDF; a piece that is only a PDF is not sent.
    private func upload(_ id: String, _ local: Local, ifMatch: String?, _ record: SyncRecord) async -> SyncRecord {
        guard let sent = await Self.sent(local) else { return record }
        var noted = record
        switch await online.upload(id, file: sent.copy, ifMatch: ifMatch) {
        case .saved(let etag):
            // What went online is what both sides now agree on: the next merge's base.
            SyncBases.save(sent.copy, for: id, records: recordsURL)
            return SyncRecord(etag: etag, savedAt: nil, hash: sent.hash)
        case .overQuota:
            noted.overQuota = true
            noted.hash = sent.hash
        case .changedOnline:
            // Saved on another device since this one saw it, or a first upload of an id the API keeps (#321): the next pass
            // fetches it and merges the two.
            noted.savedAt = nil
        case .failed:
            break
        }
        return noted
    }

    private func download(_ id: String, _ local: Local?, _ row: OnlineProject, _ records: inout [String: SyncRecord]) async -> Bool {
        guard case .success(let fetched) = await online.fetch(id), let remote = String(data: fetched.data, encoding: .utf8),
              let summary = await PieceSummary.read(fetched.data), summary.id == id
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
        guard let kept = await Self.core(.projectPulled, fields)?["file"] as? String,
              write(kept, to: local?.url, title: summary.title ?? row.title) != nil
        else { return false }
        SyncBases.save(remote, for: id, records: recordsURL)
        records[id] = SyncRecord(etag: fetched.etag, savedAt: row.savedAt, hash: Self.hash(Data(kept.utf8)))
        return true
    }

    /// Both copies changed: music-core merges them (`project.resolve`) and this iPad follows its answer (c-17a's note, "For
    /// c-17b"): the previous copy, when there is one, as a piece of its own (a writable one goes online as a new piece next
    /// pass, a later build's stays here, never sent), and "file" in place (`keep`). "file" goes online over the copy fetched
    /// when "upload" says so, and on the next pass when the merge failed its self-check (`ProjectSync.Resolve`); else the
    /// copy fetched is what both agree on, and "file" is sent only once it changes here.
    private func resolve(_ id: String, _ local: Local, _ row: OnlineProject, _ records: inout [String: SyncRecord], _ conflicts: inout [SyncConflict]) async -> Bool {
        guard case .success(let fetched) = await online.fetch(id), let remote = String(data: fetched.data, encoding: .utf8),
              let data = local.data, let here = String(data: data, encoding: .utf8)
        else { return false }
        let record = records[id] ?? SyncRecord()
        if let etag = fetched.etag, etag == record.etag {
            // The online copy is the one this iPad sent last, so only this iPad changed it.
            records[id] = await upload(id, local, ifMatch: etag, record)
            return false
        }
        var fields: [String: Any] = [
            "here": here, "savedHere": Self.iso(local.modified),
            "online": remote, "savedOnline": row.savedAt ?? Self.iso(Date()),
            "copyTitle": copyTitle(Date()),
            // This iPad writes "file" here and sends that file, not its own copy, when "upload" is true.
            "sendsFile": true,
        ]
        if records[id] != nil, let base = SyncBases.load(id, records: recordsURL) { fields["base"] = base }
        guard let answer = await Self.core(.projectResolve, fields), let kept = answer["file"] as? String,
              let changed = await keep(answer, kept, over: here, at: local.url, row, &conflicts)
        else { return false }
        // The copy fetched is what both sides agree on now. "file" goes over it when "upload" says so (a failed send leaves
        // it changed here, to go with the ETag fetched next pass, E-sync-6), and on the next pass, with that ETag, when the
        // merge failed its self-check; when either copy is a later build's ("file" is then the online one, decisions 36 and
        // 39) or both were the same, it goes only once it changes here.
        SyncBases.save(remote, for: id, records: recordsURL)
        let written = Local(url: local.url, hash: Self.hash(Data(kept.utf8)), modified: local.modified)
        let sends = answer["upload"] as? Bool == true
        let sendsNext = answer["merged"] as? Bool == false && answer["readOnly"] as? Bool != true && !local.readOnly
        let agreed = SyncRecord(etag: fetched.etag, savedAt: row.savedAt, hash: sends || sendsNext ? nil : written.hash)
        records[id] = agreed
        if sends { records[id] = await upload(id, written, ifMatch: fetched.etag, agreed) }
        return changed
    }

    /// Writes what `project.resolve` answered: "copy" as a piece of its own, then "file" in place, and the conflict line once
    /// both are (E-sync-3). When "file" cannot be written the copy just written goes again, so both copies stay as they were
    /// and the next pass merges again. Nil when nothing was kept, else whether a project file here changed.
    private func keep(_ answer: [String: Any], _ kept: String, over here: String, at url: URL, _ row: OnlineProject, _ conflicts: inout [SyncConflict]) async -> Bool? {
        var copied: (url: URL, title: String)?
        if let copy = answer["copy"] as? String {
            let title = await PieceSummary.read(Data(copy.utf8))?.title ?? row.title
            guard let written = write(copy, to: nil, title: title) else { return nil }
            copied = (written, title)
        }
        if kept != here, write(kept, to: url, title: row.title) == nil {
            if let copied { try? FileManager.default.removeItem(at: copied.url) }
            return nil
        }
        let places = (answer["conflicts"] as? [[String: Any]] ?? []).compactMap { $0["in"] as? String }
        if let copied, !places.isEmpty {
            let title = await PieceSummary.read(Data(kept.utf8))?.title ?? row.title
            conflicts.append(SyncConflict(title: title, copyTitle: copied.title, places: places))
        }
        return copied != nil || kept != here
    }

    /// One piece, one entry (version 0.6.1, #582 item 1): the project files here that are the same piece by the files they
    /// were made from (`pieceKeys`, grouped by `project.same`), such as the same PDF added on another iPad and on this one,
    /// become one when one of them carries nothing of the player's own (`project.join`, music-core's `SamePiece`). The
    /// other is written in place with its PDF and details, and the copy that joined it is deleted here and marked deleted here,
    /// so the pass deletes it online as one the player deleted (`deleteOnline`: with its ETag, again with the next pass after
    /// a failure) and no device fetches it again; until then what this iPad knows of that online copy stays, so this iPad
    /// does not fetch it again either. A copy whose bytes are kept beside
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
        guard rows.count > 1, let groups = await Self.core(.projectSame, ["pieces": rows])?["groups"] as? [[String]] else { return [] }
        var gone: [String] = []
        for group in groups {
            let members = group.compactMap { id in pieces.first { $0.id == id } }
            let files = members.compactMap { try? String(contentsOf: $0.url, encoding: .utf8) }
            guard members.count > 1, files.count == members.count else { continue }
            let stay = members.indices.filter { Self.keptBeside(members[$0].id) }
            guard let joined = await Self.core(.projectJoin, ["files": files, "stay": stay])?["joined"] as? [[String: Any]] else { continue }
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
                    records[id]?.deletedHere = true
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

    /// Writes a project file in place, or as a new file named by its title (the work's, as the library shows it, v1.5 r-2b);
    /// nil when it could not be written.
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
    static func core(_ name: PieceCore.NoPiece, _ fields: [String: Any]) async -> [String: Any]? {
        let request = facadeRequest(fields)
        let answer = await MusicCore.run { PieceCore.sendWithoutPiece(name, request) }
        guard let object = facadeObject(answer), object["error"] == nil else { return nil }
        return object
    }

    static func hash(_ data: Data) -> String {
        SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
    }

    static func iso(_ date: Date) -> String {
        ISO8601DateFormatter().string(from: date)
    }
}
