import XCTest
@testable import MotiffLab

/// Sync 0.2 on the iPad (Andrei, 2026-11-11), against the API as StubAPI answers it: a piece new here goes online, one new
/// online comes here, and when both changed one, the last save wins and the previous copy is kept beside as a piece of its
/// own. music-core decides each step (project.sync, project.online, project.pulled, project.resolve).
@MainActor
final class CloudSyncTests: XCTestCase {
    private var folder: URL!
    private var records: URL!
    private var account: Account!

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

    /// Not writable: Main in a version build (`EnvironmentSetup.writes`).
    private func sync(writable: Bool = true) -> CloudSync {
        let account = account!
        let online = OnlineProjects(session: StubAPI.session(), request: { path in await account.request(path: path, token: "t") }, writable: writable)
        return CloudSync(online: online, folder: folder, recordsURL: records) { _ in "{0} (previous copy)" }
    }

    private func url(_ path: String) throws -> String {
        try XCTUnwrap(account.request(path: path, token: "t")?.url?.absoluteString)
    }

    /// A project file of the kit's piece under the id, with the tags given, as music-core writes it.
    private func projectFile(id: String, tags: [String] = [], now: String = "2026-11-11T09:00:00Z") throws -> String {
        let example = try XCTUnwrap(KitPiece.example)
        let opened = try XCTUnwrap(facadeObject(MusicCore.open(fileName: example.fileName, data: example.data, request: #"{"language": "en", "id": "\#(id)", "now": "\#(now)"}"#)))
        let piece = Int32(opened["piece"] as? Int ?? 0)
        XCTAssertGreaterThan(piece, 0, "\(opened)")
        defer { MusicCore.close(piece: piece) }
        let body = String(decoding: try JSONSerialization.data(withJSONObject: ["tags": tags]), as: UTF8.self)
        XCTAssertNil(facadeObject(MusicCore.call(piece: piece, "project.metadata", body))?["error"])
        let file = MusicCore.call(piece: piece, "project.save", #"{"now": "2026-11-11T09:00:00Z"}"#)
        XCTAssertNil(facadeObject(file)?["error"], file)
        return file
    }

    private func row(_ id: String, savedAt: String) -> String {
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
        // The iPad last saw the base; since then it saved "Here" (now) and the web saved "Online" (in 2020, earlier).
        SyncRecords.save([id: SyncRecord(etag: "\"e1\"", savedAt: "2020-01-01T09:00:00+00:00", hash: CloudSync.hash(Data(base.utf8)))], to: records)
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
        SyncRecords.save([id: SyncRecord(etag: "\"e1\"", savedAt: "2020-01-01T09:00:00+00:00", hash: CloudSync.hash(Data(base.utf8)))], to: records)
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
