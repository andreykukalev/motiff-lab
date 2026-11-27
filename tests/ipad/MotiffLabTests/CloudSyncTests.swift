import XCTest
@testable import MotiffLab

/// Sync 0.2 on the iPad (Andrei, 2026-11-11), against the API as StubAPI answers it: a piece new here goes online, one new
/// online comes here, and when both changed one, the last save wins and the previous copy is kept beside as a piece of its
/// own. music-core decides each step (project.sync, project.online, project.pulled, project.resolve).
@MainActor
final class CloudSyncTests: XCTestCase {
    // Not private: the passes of v1.6 c-17b's review are tested in CloudSyncPassTests.swift, an extension of this class.
    var folder: URL!
    var records: URL!
    var account: Account!

    override func setUp() async throws {
        StubAPI.reset()
        folder = FileManager.default.temporaryDirectory.appendingPathComponent("CloudSyncTests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        records = folder.appendingPathComponent("records").appendingPathComponent("sync.json")
        let defaults = try XCTUnwrap(UserDefaults(suiteName: "CloudSyncTests"))
        defaults.removePersistentDomain(forName: "CloudSyncTests")
        account = Account(options: try XCTUnwrap(AccountOptions.load()), defaults: defaults)
    }

    override func tearDown() async throws {
        try? FileManager.default.removeItem(at: folder)
        StubAPI.reset()
    }

    /// Not writable: Main in a version build (`EnvironmentSetup.writes`). `copyTitle` is asked while a pass resolves a piece.
    func sync(writable: Bool = true, copyTitle: @escaping (Date) -> String = { _ in "{0} (previous copy)" }) -> CloudSync {
        let account = account!
        let online = OnlineProjects(session: StubAPI.session(), request: { path in await account.request(path: path, token: "t") }, writable: writable)
        return CloudSync(online: online, folder: folder, recordsURL: records, copyTitle: copyTitle)
    }

    func url(_ path: String) throws -> String {
        try XCTUnwrap(account.request(path: path, token: "t")?.url?.absoluteString)
    }

    /// A project file of the kit's piece under the id, with the tags given (and a resume point, a change that is not the
    /// tags'), as music-core writes it.
    func projectFile(id: String, tags: [String] = [], now: String = "2026-11-11T09:00:00Z", resume: Bool = false) throws -> String {
        let example = try XCTUnwrap(KitPiece.example)
        let opened = try XCTUnwrap(facadeObject(MusicCore.open(fileName: example.fileName, data: example.data, request: #"{"language": "en", "id": "\#(id)", "now": "\#(now)"}"#)))
        let piece = Int32(opened["piece"] as? Int ?? 0)
        XCTAssertGreaterThan(piece, 0, "\(opened)")
        defer { MusicCore.close(piece: piece) }
        let body = String(decoding: try JSONSerialization.data(withJSONObject: ["tags": tags]), as: UTF8.self)
        XCTAssertNil(facadeObject(MusicCore.call(piece: piece, "project.metadata", body))?["error"])
        if resume {
            let place = ResumePlace.saveRequest(id: nil, passage: nil, at: 0, focused: false, tempoPercent: 80, hands: nil, loop: false, now: Date(timeIntervalSince1970: 1_800_000_000))
            XCTAssertNil(facadeObject(MusicCore.call(piece: piece, "resume.save", place))?["error"])
        }
        let file = MusicCore.call(piece: piece, "project.save", #"{"now": "2026-11-11T09:00:00Z"}"#)
        XCTAssertNil(facadeObject(file)?["error"], file)
        return file
    }

    /// The file as a later build writes it (version 19, readable from 18, with a field this build does not know): kept
    /// here read-only (project file 18).
    func laterBuild(_ file: String) throws -> String {
        try edited(file) { object in
            object["version"] = 19
            object["future"] = 1
        }
    }

    /// The project file with its JSON changed as given.
    func edited(_ file: String, _ change: (inout [String: Any]) -> Void) throws -> String {
        var object = try XCTUnwrap(try JSONSerialization.jsonObject(with: Data(file.utf8)) as? [String: Any])
        change(&object)
        return String(decoding: try JSONSerialization.data(withJSONObject: object), as: UTF8.self)
    }

    /// This iPad last synced the piece as `base`: the online copy's ETag e1, saved online on 2020-01-01; the base kept for
    /// the merge (`SyncBases`) unless `keepsBase` is false.
    func known(_ id: String, base: String, keepsBase: Bool = true) {
        SyncRecords.save([id: SyncRecord(etag: "\"e1\"", savedAt: "2020-01-01T09:00:00+00:00", hash: CloudSync.hash(Data(base.utf8)))], to: records)
        if keepsBase { SyncBases.save(base, for: id, records: records) }
    }

    /// The API's list with the one piece saved online at the time given.
    func listing(_ id: String, savedAt: String) throws -> [String: StubAPI.Answer] {
        [try url("projects"): .init(status: 200, body: Data("[\(row(id, savedAt: savedAt))]".utf8))]
    }

    func row(_ id: String, savedAt: String) -> String {
        #"{"id":"\#(id)","fileName":"Elise.musicxml","motifCount":0,"variationCount":0,"lastOpened":"2026-11-11T09:00:00+00:00","resumeCount":0,"ideaCount":0,"savedAt":"\#(savedAt)"}"#
    }

    func testAPieceNewHereGoesOnlineAndIsKnownAfter() async throws {
        let id = "p_" + String(repeating: "a", count: 32)
        try Data(try projectFile(id: id).utf8).write(to: folder.appendingPathComponent("Elise.motiff.json"))
        StubAPI.reset([
            try url("projects"): .init(status: 200, body: Data("[]".utf8)),
            try "PUT " + url("projects/" + id): .init(status: 204, body: Data(), headers: ["ETag": "\"e1\""]),
        ])

        let outcome = await sync().run()

        XCTAssertFalse(outcome.changed)
        let put = try XCTUnwrap(StubAPI.requests.firstIndex { $0.httpMethod == "PUT" })
        XCTAssertNil(StubAPI.requests[put].value(forHTTPHeaderField: "If-Match"), "a new piece goes as new")
        let sent = await PieceSummary.read(StubAPI.bodies[put])
        XCTAssertEqual(sent?.id, id)
        XCTAssertEqual(SyncRecords.load(from: records)[id]?.etag, "\"e1\"")
    }

    func testAPieceNewOnlineComesHereWithItsId() async throws {
        let id = "p_" + String(repeating: "b", count: 32)
        StubAPI.reset([
            try url("projects"): .init(status: 200, body: Data("[\(row(id, savedAt: "2026-11-11T10:00:00+00:00"))]".utf8)),
            try url("projects/" + id): .init(status: 200, body: Data(try projectFile(id: id, tags: ["Exam"]).utf8), headers: ["ETag": "\"e1\""]),
        ])

        let outcome = await sync().run()

        XCTAssertTrue(outcome.changed)
        let files = ProjectFiles.list(in: folder)
        XCTAssertEqual(files.count, 1)
        let kept = await PieceSummary.read(files[0].url)
        XCTAssertEqual(kept?.id, id)
        XCTAssertEqual(kept?.tags, ["Exam"])
        XCTAssertFalse(StubAPI.requests.contains { $0.httpMethod == "PUT" || $0.httpMethod == "DELETE" })
        let record = try XCTUnwrap(SyncRecords.load(from: records)[id])
        XCTAssertEqual(record.etag, "\"e1\"")
        XCTAssertEqual(record.savedAt, "2026-11-11T10:00:00+00:00")
    }

    func testWhenBothChangedThePieceTheLastSaveWinsAndThePreviousCopyIsKept() async throws {
        let id = "p_" + String(repeating: "c", count: 32)
        let here = folder.appendingPathComponent("Elise.motiff.json")
        let base = try projectFile(id: id)
        let online = try projectFile(id: id, tags: ["Online"])
        let onlineSummary = await PieceSummary.read(Data(online.utf8))
        let title = try XCTUnwrap(onlineSummary?.title)
        try Data(try projectFile(id: id, tags: ["Here"]).utf8).write(to: here)
        // The iPad last saw the base, kept by a build before c-17b without it; since then it saved "Here" (now) and the web
        // saved "Online" (in 2020, earlier): the tags changed on both, a conflict.
        known(id, base: base, keepsBase: false)
        StubAPI.reset([
            try url("projects"): .init(status: 200, body: Data("[\(row(id, savedAt: "2020-01-02T09:00:00+00:00"))]".utf8)),
            try url("projects/" + id): .init(status: 200, body: Data(online.utf8), headers: ["ETag": "\"e2\""]),
            try "PUT " + url("projects/" + id): .init(status: 204, body: Data(), headers: ["ETag": "\"e3\""]),
        ])

        let outcome = await sync().run()

        XCTAssertTrue(outcome.changed)
        let stays = await PieceSummary.read(here)
        XCTAssertEqual(stays?.tags, ["Here"], "the last save stays")
        let put = try XCTUnwrap(StubAPI.requests.firstIndex { $0.httpMethod == "PUT" })
        XCTAssertEqual(StubAPI.requests[put].value(forHTTPHeaderField: "If-Match"), "\"e2\"", "over the online copy it fetched")
        let copies = ProjectFiles.list(in: folder).filter { $0.url != here }
        XCTAssertEqual(copies.count, 1, "the previous copy is kept beside")
        let copied = await PieceSummary.read(copies[0].url)
        let copy = try XCTUnwrap(copied)
        XCTAssertEqual(copy.tags, ["Online"])
        XCTAssertNotEqual(copy.id, id)
        XCTAssertEqual(copy.title, title + " (previous copy)")
        XCTAssertEqual(SyncRecords.load(from: records)[id]?.etag, "\"e3\"")
        // The library says so (v1.6 c-17b): what both changed, and where the other's is.
        let conflict = try XCTUnwrap(outcome.conflicts.first)
        XCTAssertEqual(outcome.conflicts.count, 1)
        XCTAssertEqual(conflict.copyTitle, title + " (previous copy)")
        XCTAssertEqual(Set(conflict.places.map { SyncConflict.kind($0) }), ["sync.inDetails"], "\(conflict.places)")
        XCTAssertEqual(
            conflict.line(UIText(language: "en")),
            "“\(title)” was changed differently on two devices (the piece's details and settings). The version saved earlier is kept beside it: “\(title) (previous copy)”."
        )
    }

    /// v1.6 c-17b: against the copy both last agreed on (`SyncBases`), the tags changed only online since come here without a
    /// conflict (without the base they would be one), with the resume point made here, and the file kept here is what goes
    /// online, over the copy fetched; what went is the next base.
    func testAResolveMergesAgainstTheBaseAndSendsTheFileItKeeps() async throws {
        let id = "p_" + String(repeating: "i", count: 32)
        let here = folder.appendingPathComponent("Elise.motiff.json")
        let base = try projectFile(id: id, tags: ["Base"])
        try Data(try projectFile(id: id, tags: ["Base"], resume: true).utf8).write(to: here)
        known(id, base: base)
        StubAPI.reset(try listing(id, savedAt: "2020-01-02T09:00:00+00:00").merging([
            try url("projects/" + id): .init(status: 200, body: Data(try projectFile(id: id, tags: ["Online"]).utf8), headers: ["ETag": "\"e2\""]),
            try "PUT " + url("projects/" + id): .init(status: 204, body: Data(), headers: ["ETag": "\"e3\""]),
        ]) { first, _ in first })

        let outcome = await sync().run()

        XCTAssertEqual(outcome.conflicts, [])
        XCTAssertEqual(ProjectFiles.list(in: folder).count, 1, "nothing beside: the merge holds both sides' changes")
        let stays = await PieceSummary.read(here)
        XCTAssertEqual(stays?.tags, ["Online"])
        XCTAssertNotNil(stays?.latest, "the resume point made here stays")
        let put = try XCTUnwrap(StubAPI.requests.firstIndex { $0.httpMethod == "PUT" })
        XCTAssertEqual(StubAPI.requests[put].value(forHTTPHeaderField: "If-Match"), "\"e2\"")
        let sent = await PieceSummary.read(StubAPI.bodies[put])
        XCTAssertEqual(sent?.tags, ["Online"], "the file kept here is what went")
        XCTAssertEqual(SyncBases.load(id, records: records), String(decoding: StubAPI.bodies[put], as: UTF8.self))
        XCTAssertEqual(SyncRecords.load(from: records)[id]?.etag, "\"e3\"")
    }

    /// The 1.6 plan's decisions 14 and 26: a piece the player deleted here goes online too, only as this iPad last saw it
    /// (If-Match); one saved on another device since stays online and comes back here, and goes once deleted again.
    func testAPieceDeletedHereGoesOnlineOnlyAsThisIPadLastSawIt() async throws {
        let id = "p_" + String(repeating: "j", count: 32)
        known(id, base: try projectFile(id: id))
        CloudSync.markDeletedHere(id, recordsURL: records)
        CloudSync.markDeletedHere("p_never", recordsURL: records)
        let online = try url("projects/" + id)
        StubAPI.reset(try listing(id, savedAt: "2020-01-01T09:00:00+00:00").merging(["DELETE " + online: .init(status: 412, body: Data())]) { first, _ in first })

        _ = await sync().run()

        XCTAssertEqual(StubAPI.requests.first { $0.httpMethod == "DELETE" }?.value(forHTTPHeaderField: "If-Match"), "\"e1\"")
        XCTAssertEqual(SyncRecords.load(from: records)[id]?.deletedHere, true, "saved elsewhere since: it stays online")
        XCTAssertNil(SyncRecords.load(from: records)["p_never"], "a piece never synced has nothing online to delete: forgotten")

        StubAPI.reset(try listing(id, savedAt: "2020-01-03T09:00:00+00:00").merging([
            online: .init(status: 200, body: Data(try projectFile(id: id, tags: ["Elsewhere"]).utf8), headers: ["ETag": "\"e2\""]),
        ]) { first, _ in first })
        let back = await sync().run()

        XCTAssertTrue(back.changed, "it comes back here, with what the other device saved")
        let files = ProjectFiles.list(in: folder)
        XCTAssertEqual(files.count, 1)
        XCTAssertNil(SyncRecords.load(from: records)[id]?.deletedHere)
        XCTAssertFalse(StubAPI.requests.contains { $0.httpMethod == "DELETE" })

        try FileManager.default.removeItem(at: files[0].url)
        CloudSync.markDeletedHere(id, recordsURL: records)
        StubAPI.reset(try listing(id, savedAt: "2020-01-03T09:00:00+00:00").merging(["DELETE " + online: .init(status: 204, body: Data())]) { first, _ in first })
        _ = await sync().run()

        XCTAssertEqual(StubAPI.requests.first { $0.httpMethod == "DELETE" }?.value(forHTTPHeaderField: "If-Match"), "\"e2\"")
        XCTAssertNil(SyncRecords.load(from: records)[id])
        XCTAssertNil(SyncBases.load(id, records: records), "its base goes with its record")
    }

    /// #321: a piece new here goes online only as a new one (no If-Match); an id the API keeps already answers 412, which is
    /// no save: the next pass fetches that copy and merges the two before anything goes over it.
    func testAFirstUploadOfAnIdKeptOnlineIsMergedNeverWrittenOver() async throws {
        let id = "p_" + String(repeating: "k", count: 32)
        try Data(try projectFile(id: id, tags: ["Here"]).utf8).write(to: folder.appendingPathComponent("Elise.motiff.json"))
        let target = try url("projects/" + id)
        let put = "PUT " + target
        StubAPI.reset([try url("projects"): .init(status: 200, body: Data("[]".utf8)), put: .init(status: 412, body: Data())])

        _ = await sync().run()

        let first = try XCTUnwrap(StubAPI.requests.first { $0.httpMethod == "PUT" })
        XCTAssertNil(first.value(forHTTPHeaderField: "If-Match"))
        XCTAssertNil(SyncRecords.load(from: records)[id], "not saved, so still not known online")

        StubAPI.reset(try listing(id, savedAt: "2020-01-02T09:00:00+00:00").merging([
            target: .init(status: 200, body: Data(try projectFile(id: id, tags: ["Online"]).utf8), headers: ["ETag": "\"e1\""]),
            put: .init(status: 204, body: Data(), headers: ["ETag": "\"e2\""]),
        ]) { first, _ in first })
        _ = await sync().run()

        let merged = try XCTUnwrap(StubAPI.requests.first { $0.httpMethod == "PUT" && $0.url?.absoluteString == target })
        XCTAssertEqual(merged.value(forHTTPHeaderField: "If-Match"), "\"e1\"", "over the copy it fetched")
        XCTAssertEqual(SyncRecords.load(from: records)[id]?.etag, "\"e2\"")
    }

    /// The lead's decision 39 (amending 36 for a later build's copy online): a later build's copy online meets this iPad's
    /// changed one. The later build's stays the piece, here read-only and never sent, since writing over it would remove
    /// what that build wrote (`ProjectSync.Apart`); this iPad's goes beside as a piece of its own, which the next pass sends
    /// as new.
    func testALaterBuildsCopyOnlineTakesThePieceAndThisIPadsGoesBeside() async throws {
        let id = "p_" + String(repeating: "l", count: 32)
        let here = folder.appendingPathComponent("Elise.motiff.json")
        try Data(try projectFile(id: id, tags: ["Here"]).utf8).write(to: here)
        known(id, base: try projectFile(id: id))
        StubAPI.reset(try listing(id, savedAt: "2020-01-02T09:00:00+00:00").merging([
            try url("projects/" + id): .init(status: 200, body: Data(try laterBuild(try projectFile(id: id, tags: ["Later"])).utf8), headers: ["ETag": "\"e2\""]),
        ]) { first, _ in first })

        let outcome = await sync().run()

        XCTAssertTrue(outcome.changed)
        XCTAssertFalse(StubAPI.requests.contains { $0.httpMethod == "PUT" }, "nothing goes over a later build's copy")
        let kept = await PieceSummary.read(here)
        XCTAssertEqual(kept?.readOnly, true)
        XCTAssertEqual(kept?.tags, ["Later"])
        let beside = ProjectFiles.list(in: folder).filter { $0.url != here }
        XCTAssertEqual(beside.count, 1)
        let first = try XCTUnwrap(beside.first)
        let copied = await PieceSummary.read(first.url)
        let copy = try XCTUnwrap(copied)
        XCTAssertEqual(copy.tags, ["Here"])
        XCTAssertFalse(copy.readOnly)
        let copyId = try XCTUnwrap(copy.id)
        XCTAssertNotEqual(copyId, id)

        StubAPI.answer([try "PUT " + url("projects/" + copyId): .init(status: 204, body: Data(), headers: ["ETag": "\"e3\""])])
        _ = await sync().run()

        let puts = StubAPI.requests.filter { $0.httpMethod == "PUT" }
        XCTAssertEqual(puts.map { $0.url?.absoluteString }, [try url("projects/" + copyId)], "only this iPad's copy, as a new piece")
        XCTAssertNil(puts.first?.value(forHTTPHeaderField: "If-Match"))
    }

    /// A later build's copy here is kept and never sent (project.online refuses it): not asked again on every pass (E-sync-5).
    func testALaterBuildsCopyHereIsNeverSent() async throws {
        let id = "p_" + String(repeating: "m", count: 32)
        try Data(try laterBuild(try projectFile(id: id)).utf8).write(to: folder.appendingPathComponent("Later.motiff.json"))
        StubAPI.reset([try url("projects"): .init(status: 200, body: Data("[]".utf8))])

        let outcome = await sync().run()

        XCTAssertFalse(outcome.changed)
        XCTAssertTrue(StubAPI.requests.allSatisfy { ($0.httpMethod ?? "GET") == "GET" })
        XCTAssertNil(SyncRecords.load(from: records)[id])
    }

    /// What the library calls each place a conflict can be in (c-17a's "in" values, those of the PDF's page marks included),
    /// in both languages, listed in one order whatever order they came in.
    func testEachPlaceOfAConflictHasItsName() {
        let named = [
            "workspace.observations": "sync.inIdeas", "workspace.variations": "sync.inMotifs", "workspace.drafts": "sync.inDrafts",
            "workspace.resumePoints": "sync.inResumePoints", "workspace.bookmarks.items": "sync.inBookmarks",
            "originals.marks.bookmarks": "sync.inBookmarks", "originals.marks.ink": "sync.inMarks", "originals.marks": "sync.inMarks",
            "workspace.marks.fingering": "sync.inMarks", "notes.marks": "sync.inMarks", "workspace.phrases.anchors": "sync.inPhrases",
            "notes": "sync.inNotes", "notes.edits": "sync.inNotes", "notes.scan.marks": "sync.inNotes", "notes.name": "sync.inNotes",
            "workspace.metadata": "sync.inDetails", "workspace.settings": "sync.inDetails", "workspace.future": "sync.inOther",
            "originals.future": "sync.inOther",
        ]
        for (place, key) in named { XCTAssertEqual(SyncConflict.kind(place), key, place) }
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            for key in Set(named.values).union(["sync.conflict"]) { XCTAssertNotEqual(text[key], key, "\(language) \(key)") }
        }
        let both = SyncConflict(title: "Elise", copyTitle: "Elise (copy)", places: ["originals.marks.ink", "workspace.observations", "originals.marks"])
        XCTAssertEqual(
            both.line(UIText(language: "ru")),
            "«Elise» изменили на двух устройствах по-разному (идеи, пометки). Версия, сохранённая раньше, лежит рядом: «Elise (copy)»."
        )
    }

    /// Version 0.6.1 (#582 item 1): the same file added here and on the web, neither with work of its own, is one piece: the
    /// one made first stays on both devices with both copies' details, and this iPad's copy goes here and online.
    func testTheSamePieceAddedHereAndOnTheWebIsOnePiece() async throws {
        let mine = "p_" + String(repeating: "1", count: 32)
        let web = "p_" + String(repeating: "2", count: 32)
        try Data(try projectFile(id: mine, tags: ["Here"], now: "2026-11-15T09:00:00Z").utf8).write(to: folder.appendingPathComponent("Elise.motiff.json"))
        StubAPI.reset([
            try url("projects"): .init(status: 200, body: Data("[\(row(web, savedAt: "2026-11-15T08:30:00+00:00"))]".utf8)),
            try url("projects/" + web): .init(status: 200, body: Data(try projectFile(id: web, tags: ["Web"], now: "2026-11-15T08:00:00Z").utf8), headers: ["ETag": "\"e1\""]),
            try "PUT " + url("projects/" + mine): .init(status: 204, body: Data(), headers: ["ETag": "\"e2\""]),
            try "DELETE " + url("projects/" + mine): .init(status: 204, body: Data()),
        ])

        let outcome = await sync().run()

        XCTAssertTrue(outcome.changed)
        let files = ProjectFiles.list(in: folder)
        XCTAssertEqual(files.count, 1, "one piece in the library")
        let kept = await PieceSummary.read(files[0].url)
        XCTAssertEqual(kept?.id, web, "the one made first")
        XCTAssertEqual(kept?.tags, ["Web", "Here"])
        let gone = try url("projects/" + mine)
        XCTAssertTrue(StubAPI.requests.contains { $0.httpMethod == "DELETE" && $0.url?.absoluteString == gone }, "this iPad's copy goes online too")
        XCTAssertNil(SyncRecords.load(from: records)[mine])
    }

    /// A copy whose bytes this iPad keeps beside its file (a linked PDF, a copy kept for playing offline) is never joined
    /// away (`stay`), though another may join it; when both are kept so, both stay.
    func testCopiesKeptBesideTheirFilesAreNeverJoinedAway() async throws {
        let mine = try projectFile(id: "p_" + String(repeating: "3", count: 32), now: "2026-11-15T09:00:00Z")
        let web = try projectFile(id: "p_" + String(repeating: "4", count: 32), now: "2026-11-15T08:00:00Z")
        func join(_ stay: [Int]) throws -> [[String: Any]] {
            let body = String(decoding: try JSONSerialization.data(withJSONObject: ["files": [mine, web], "stay": stay]), as: UTF8.self)
            let answer = try XCTUnwrap(facadeObject(MusicCore.call(piece: 0, "project.join", body)))
            XCTAssertNil(answer["error"])
            return answer["joined"] as? [[String: Any]] ?? []
        }

        // The web's copy, made first, would stay; this iPad's is kept beside here, so the web's joins it instead.
        let joined = try join([0])
        XCTAssertEqual(joined.count, 1)
        XCTAssertEqual((joined.first?["keeper"] as? NSNumber)?.intValue, 0)
        XCTAssertEqual((joined.first?["from"] as? [NSNumber])?.map(\.intValue), [1])
        XCTAssertTrue(try join([0, 1]).isEmpty, "neither may go")
    }

    func testAPieceBeyondTheFreeAccountsRoomStaysHereAndIsCounted() async throws {
        let id = "p_" + String(repeating: "d", count: 32)
        try Data(try projectFile(id: id).utf8).write(to: folder.appendingPathComponent("Elise.motiff.json"))
        StubAPI.reset([
            try url("projects"): .init(status: 200, body: Data("[]".utf8)),
            try "PUT " + url("projects/" + id): .init(status: 403, body: Data(#"{"message":"full","code":"quota"}"#.utf8)),
        ])

        let outcome = await sync().run()

        XCTAssertEqual(outcome.overQuota, 1)
        XCTAssertEqual(ProjectFiles.list(in: folder).count, 1, "it stays here")
        XCTAssertEqual(SyncRecords.load(from: records)[id]?.overQuota, true)
    }

    func testNothingIsSentWhileSignedOut() async throws {
        try Data(try projectFile(id: "p_" + String(repeating: "e", count: 32)).utf8).write(to: folder.appendingPathComponent("Elise.motiff.json"))
        let online = OnlineProjects(session: StubAPI.session()) { _ in nil }
        let outcome = await CloudSync(online: online, folder: folder, recordsURL: records) { _ in "{0}" }.run()
        XCTAssertEqual(outcome, CloudSync.Outcome())
        XCTAssertTrue(StubAPI.requests.isEmpty)
    }

    /// Main in a version build (Andrei, 2026-11-12): the real pieces come here, nothing goes online.
    func testReadOnlyFetchesWhatIsNewOnlineAndSendsNothing() async throws {
        let here = "p_" + String(repeating: "f", count: 32)
        let there = "p_" + String(repeating: "g", count: 32)
        try Data(try projectFile(id: here).utf8).write(to: folder.appendingPathComponent("Elise.motiff.json"))
        StubAPI.reset([
            try url("projects"): .init(status: 200, body: Data("[\(row(there, savedAt: "2026-11-11T10:00:00+00:00"))]".utf8)),
            try url("projects/" + there): .init(status: 200, body: Data(try projectFile(id: there, tags: ["Real"]).utf8), headers: ["ETag": "\"e1\""]),
            try "PUT " + url("projects/" + here): .init(status: 204, body: Data(), headers: ["ETag": "\"e2\""]),
        ])

        let outcome = await sync(writable: false).run()

        XCTAssertTrue(outcome.changed)
        XCTAssertEqual(ProjectFiles.list(in: folder).count, 2, "the piece online came here")
        XCTAssertTrue(StubAPI.requests.allSatisfy { ($0.httpMethod ?? "GET") == "GET" }, "nothing was sent")
        XCTAssertNil(SyncRecords.load(from: records)[here]?.etag, "the piece here is not online, and goes once writing is allowed")
        XCTAssertEqual(SyncRecords.load(from: records)[there]?.etag, "\"e1\"")
    }

    func testReadOnlyLeavesAPieceChangedOnBothSidesAsItIs() async throws {
        let id = "p_" + String(repeating: "h", count: 32)
        let here = folder.appendingPathComponent("Elise.motiff.json")
        let base = try projectFile(id: id)
        let mine = try projectFile(id: id, tags: ["Here"])
        try Data(mine.utf8).write(to: here)
        known(id, base: base)
        StubAPI.reset([
            try url("projects"): .init(status: 200, body: Data("[\(row(id, savedAt: "2020-01-02T09:00:00+00:00"))]".utf8)),
            try url("projects/" + id): .init(status: 200, body: Data(try projectFile(id: id, tags: ["Online"]).utf8), headers: ["ETag": "\"e2\""]),
        ])

        let outcome = await sync(writable: false).run()

        XCTAssertFalse(outcome.changed)
        XCTAssertEqual(try String(contentsOf: here, encoding: .utf8), mine, "this iPad's copy stays as it was")
        XCTAssertEqual(ProjectFiles.list(in: folder).count, 1, "no previous copy is made")
        XCTAssertTrue(StubAPI.requests.allSatisfy { ($0.httpMethod ?? "GET") == "GET" })
        XCTAssertEqual(SyncRecords.load(from: records)[id]?.etag, "\"e1\"", "resolved once writing is allowed")
    }
}
