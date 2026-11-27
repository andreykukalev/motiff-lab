import XCTest
@testable import MotiffLab

/// What a sync pass leaves for the next one (v1.6 c-17b, the review's round 1), against the API as StubAPI answers it: a
/// file kept unsent goes then, with the ETag fetched; a mark made while a pass runs survives it; nothing half written stays;
/// a piece synced before the bases has one once both sides hold the same copy.
extension CloudSyncTests {
    /// The piece with a second Notes, n_2 (a copy of the first, named as given), as an import of another edition adds it.
    private func withOtherNotes(_ file: String, named name: String? = nil) throws -> String {
        try edited(file) { object in
            var notes = object["notes"] as? [[String: Any]] ?? []
            guard var other = notes.first else { return }
            other["id"] = "n_2"
            if let name { other["name"] = name }
            notes.append(other)
            object["notes"] = notes
        }
    }

    private func puts(to id: String) throws -> [URLRequest] {
        let target = try url("projects/" + id)
        return StubAPI.requests.filter { $0.httpMethod == "PUT" && $0.url?.absoluteString == target }
    }

    /// `ProjectSync.Resolve`: Notes removed here and renamed online cannot be merged (the merge fails its self-check), and
    /// "upload" is false; this iPad, saved last, keeps its copy, the online one goes beside, and "file" goes online on the
    /// next pass over the copy fetched (If-Match its ETag), else the next download would write over what only this iPad has.
    func testAFileAMergeCouldNotMergeGoesOnTheNextPassOverTheCopyFetched() async throws {
        let id = "p_" + String(repeating: "q", count: 32)
        let base = try withOtherNotes(try projectFile(id: id))
        try Data(try projectFile(id: id, tags: ["Here"]).utf8).write(to: folder.appendingPathComponent("Elise.motiff.json"))
        known(id, base: base)
        let online = try url("projects/" + id)
        StubAPI.reset(try listing(id, savedAt: "2020-01-02T09:00:00+00:00").merging([
            online: .init(status: 200, body: Data(try withOtherNotes(try projectFile(id: id), named: "Urtext").utf8), headers: ["ETag": "\"e2\""]),
        ]) { first, _ in first })

        let outcome = await sync().run()

        XCTAssertTrue(outcome.changed)
        XCTAssertEqual(ProjectFiles.list(in: folder).count, 2, "the online copy is kept beside")
        XCTAssertTrue(try puts(to: id).isEmpty, "not merged: nothing goes in this pass")
        let record = try XCTUnwrap(SyncRecords.load(from: records)[id])
        XCTAssertEqual(record.etag, "\"e2\"")
        XCTAssertNil(record.hash, "the file kept here is still to go")

        StubAPI.answer(["PUT " + online: .init(status: 204, body: Data(), headers: ["ETag": "\"e3\""])])
        _ = await sync().run()

        let sent = try XCTUnwrap(try puts(to: id).first)
        XCTAssertEqual(sent.value(forHTTPHeaderField: "If-Match"), "\"e2\"", "over the copy it fetched")
        XCTAssertEqual(SyncRecords.load(from: records)[id]?.etag, "\"e3\"")
    }

    /// E-sync-6: a merged file whose upload fails stays here changed, and the next pass sends it over the copy fetched.
    func testAMergedFileThatCouldNotBeSentGoesOnTheNextPassOverTheCopyFetched() async throws {
        let id = "p_" + String(repeating: "r", count: 32)
        try Data(try projectFile(id: id, tags: ["Base"], resume: true).utf8).write(to: folder.appendingPathComponent("Elise.motiff.json"))
        known(id, base: try projectFile(id: id, tags: ["Base"]))
        let online = try url("projects/" + id)
        StubAPI.reset(try listing(id, savedAt: "2020-01-02T09:00:00+00:00").merging([
            online: .init(status: 200, body: Data(try projectFile(id: id, tags: ["Online"]).utf8), headers: ["ETag": "\"e2\""]),
            "PUT " + online: .init(status: 503, body: Data()),
        ]) { first, _ in first })

        _ = await sync().run()

        XCTAssertEqual(try puts(to: id).count, 1)
        XCTAssertEqual(SyncRecords.load(from: records)[id]?.etag, "\"e2\"")
        XCTAssertNil(SyncRecords.load(from: records)[id]?.hash)

        StubAPI.reset(try listing(id, savedAt: "2020-01-02T09:00:00+00:00").merging([
            "PUT " + online: .init(status: 204, body: Data(), headers: ["ETag": "\"e3\""]),
        ]) { first, _ in first })
        _ = await sync().run()

        let sent = try XCTUnwrap(try puts(to: id).first)
        XCTAssertEqual(sent.value(forHTTPHeaderField: "If-Match"), "\"e2\"")
        let summary = await PieceSummary.read(StubAPI.bodies[try XCTUnwrap(StubAPI.requests.firstIndex(of: sent))])
        XCTAssertEqual(summary?.tags, ["Online"], "the merged file")
        XCTAssertFalse(StubAPI.requests.contains { $0.url?.absoluteString == online && ($0.httpMethod ?? "GET") == "GET" }, "nothing to fetch")
    }

    /// E-sync-1: pieces the library deletes while a pass runs (here while it resolves another) keep their marks, one synced
    /// before and one the pass sends for the first time, and the next pass deletes the first online with its ETag.
    func testAPieceDeletedWhileAPassRunsKeepsItsMark() async throws {
        let synced = "p_" + String(repeating: "0", count: 32)
        let sentNow = "p_" + String(repeating: "1", count: 32)
        let resolved = "p_" + String(repeating: "s", count: 32)
        let syncedFile = folder.appendingPathComponent("Synced.motiff.json")
        let newFile = folder.appendingPathComponent("New.motiff.json")
        let file = try projectFile(id: synced)
        try Data(file.utf8).write(to: syncedFile)
        try Data(try projectFile(id: sentNow).utf8).write(to: newFile)
        try Data(try projectFile(id: resolved, tags: ["Base"], resume: true).utf8).write(to: folder.appendingPathComponent("Elise.motiff.json"))
        known(resolved, base: try projectFile(id: resolved, tags: ["Base"]))
        var saved = SyncRecords.load(from: records)
        saved[synced] = SyncRecord(etag: "\"e1\"", savedAt: "2020-01-01T09:00:00+00:00", hash: CloudSync.hash(Data(file.utf8)))
        SyncRecords.save(saved, to: records)
        StubAPI.reset([
            try url("projects"): .init(status: 200, body: Data("[\(row(synced, savedAt: "2020-01-01T09:00:00+00:00")),\(row(resolved, savedAt: "2020-01-02T09:00:00+00:00"))]".utf8)),
            try "PUT " + url("projects/" + sentNow): .init(status: 204, body: Data(), headers: ["ETag": "\"n1\""]),
            try url("projects/" + resolved): .init(status: 200, body: Data(try projectFile(id: resolved, tags: ["Online"]).utf8), headers: ["ETag": "\"e2\""]),
            try "PUT " + url("projects/" + resolved): .init(status: 204, body: Data(), headers: ["ETag": "\"e3\""]),
        ])
        let recordsURL: URL = records
        // The library's Delete, as the pass resolves the third piece, after it saw the other two.
        let pass = sync { _ in
            try? FileManager.default.removeItem(at: syncedFile)
            try? FileManager.default.removeItem(at: newFile)
            CloudSync.markDeletedHere(synced, recordsURL: recordsURL)
            CloudSync.markDeletedHere(sentNow, recordsURL: recordsURL)
            return "{0} (previous copy)"
        }

        _ = await pass.run()

        let marked = SyncRecords.load(from: records)
        XCTAssertEqual(marked[synced]?.deletedHere, true)
        XCTAssertEqual(marked[sentNow]?.deletedHere, true, "sent for the first time in that pass")
        XCTAssertEqual(marked[sentNow]?.etag, "\"n1\"")

        StubAPI.reset([
            try url("projects"): .init(status: 200, body: Data("[\(row(synced, savedAt: "2020-01-01T09:00:00+00:00"))]".utf8)),
            try "DELETE " + url("projects/" + synced): .init(status: 204, body: Data()),
        ])
        _ = await sync().run()

        XCTAssertEqual(StubAPI.requests.first { $0.httpMethod == "DELETE" }?.value(forHTTPHeaderField: "If-Match"), "\"e1\"")
        XCTAssertNil(SyncRecords.load(from: records)[synced])
    }

    /// E-sync-2: a piece deleted here whose online copy is still to go is not offered to get back.
    func testAPieceBeingDeletedOnlineIsNotOfferedToGetBack() {
        let id = "p_" + String(repeating: "t", count: 32)
        known(id, base: "{}", keepsBase: false)
        let listed = [id, "p_other", "p_here"].map { OnlineProject(id: $0, fileName: "Elise.musicxml") }
        XCTAssertEqual(CloudSync.offered(listed, here: ["p_here"], recordsURL: records).map(\.id), [id, "p_other"])

        CloudSync.markDeletedHere(id, recordsURL: records)

        XCTAssertEqual(CloudSync.offered(listed, here: ["p_here"], recordsURL: records).map(\.id), ["p_other"])
    }

    /// E-sync-3: when the merged file cannot be written here, the previous copy written before it goes again, no conflict is
    /// told and nothing is sent: both copies stay as they were, and the next pass merges again.
    func testAMergeHalfWrittenLeavesBothCopiesAsTheyWere() async throws {
        let id = "p_" + String(repeating: "u", count: 32)
        let here = folder.appendingPathComponent("Elise.motiff.json")
        let mine = try projectFile(id: id, tags: ["Here"])
        try Data(mine.utf8).write(to: here)
        known(id, base: try projectFile(id: id, tags: ["Base"]))
        // Tags changed on both (a conflict, so a copy beside) and a resume point online (so "file" is not this iPad's file).
        StubAPI.reset(try listing(id, savedAt: "2020-01-02T09:00:00+00:00").merging([
            try url("projects/" + id): .init(status: 200, body: Data(try projectFile(id: id, tags: ["Online"], resume: true).utf8), headers: ["ETag": "\"e2\""]),
        ]) { first, _ in first })
        // An atomic write replaces the file, which an immutable one refuses; the copy beside is a new file and is written.
        try FileManager.default.setAttributes([.immutable: true], ofItemAtPath: here.path)
        defer { try? FileManager.default.setAttributes([.immutable: false], ofItemAtPath: here.path) }

        let outcome = await sync().run()

        XCTAssertEqual(outcome.conflicts, [])
        XCTAssertEqual(ProjectFiles.list(in: folder).count, 1, "the copy written beside went again")
        XCTAssertEqual(try String(contentsOf: here, encoding: .utf8), mine)
        XCTAssertFalse(StubAPI.requests.contains { $0.httpMethod == "PUT" })
        XCTAssertEqual(SyncRecords.load(from: records)[id]?.etag, "\"e1\"", "merged again next pass")
    }

    /// A piece synced by a build before c-17b has no base: while neither side changed it, the pass keeps its copy as it goes
    /// online (`project.online`) as one, asking the API nothing more, so the next merge has a base.
    func testAPieceSyncedBeforeBasesWereKeptGetsOneWhileNeitherSideChangedIt() async throws {
        let id = "p_" + String(repeating: "v", count: 32)
        let file = try projectFile(id: id)
        try Data(file.utf8).write(to: folder.appendingPathComponent("Elise.motiff.json"))
        known(id, base: file, keepsBase: false)
        StubAPI.reset(try listing(id, savedAt: "2020-01-01T09:00:00+00:00"))

        _ = await sync().run()

        XCTAssertEqual(StubAPI.requests.count, 1, "only the list")
        let body = String(decoding: try JSONSerialization.data(withJSONObject: ["file": file]), as: UTF8.self)
        let sent = try XCTUnwrap(facadeObject(MusicCore.call(piece: 0, "project.online", body))?["file"] as? String)
        XCTAssertEqual(SyncBases.load(id, records: records), sent)
    }
}
