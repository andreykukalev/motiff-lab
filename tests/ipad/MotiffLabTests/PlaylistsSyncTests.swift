import XCTest
@testable import MotiffLab

/// The library's playlists file as p-4's record will keep it: in memory, with an edit the player may make while a pass runs.
actor FakePlaylists: PlaylistsFileStore {
    private(set) var copy: PlaylistsCopy?
    /// Written as the player's edit just before the pass replaces the file.
    var edit: String?

    init(_ file: String?) {
        copy = file.map { PlaylistsCopy(file: $0, saved: Date()) }
    }

    func current() -> PlaylistsCopy? { copy }

    func replace(_ over: String?, with file: String) -> Bool {
        if let edit {
            copy = PlaylistsCopy(file: edit, saved: Date())
            self.edit = nil
        }
        guard copy?.file == over else { return false }
        copy = PlaylistsCopy(file: file, saved: Date())
        return true
    }

    func editWhilePassRuns(_ file: String) { edit = file }
}

/// The playlists' step of a sync pass (v1.7 p-3), against the API as StubAPI answers it and a library's playlists file kept
/// by `FakePlaylists`: a copy new on one side goes to the other, both changed merge three-way (`playlists.resolve`) with the
/// pieces' former ids and go over the copy fetched, a stale save is merged again next pass, a later build's copy online
/// leaves this iPad's as it is, and the player's edit during a pass is never written over.
extension CloudSyncTests {
    /// A pass that syncs these playlists too.
    func sync(playlists: FakePlaylists, writable: Bool = true) -> CloudSync {
        var pass = sync(writable: writable)
        pass.playlists = playlists
        return pass
    }

    /// A `playlists.*` call on `file`; its "file" and, for `playlists.create`, the new playlist's id.
    @discardableResult
    func playlists(_ call: String, _ file: String?, _ fields: [String: Any]) throws -> (file: String, id: String?) {
        var request = fields
        if let file { request["file"] = file }
        let body = String(decoding: try JSONSerialization.data(withJSONObject: request), as: UTF8.self)
        let answer = try XCTUnwrap(facadeObject(MusicCore.call(piece: 0, call, body)))
        XCTAssertNil(answer["error"], "\(answer)")
        return (try XCTUnwrap(answer["file"] as? String), answer["id"] as? String)
    }

    /// The playlists in a file: each name with its pieces, in order.
    func shown(_ file: String?) throws -> [String: [String]] {
        guard let file else { return [:] }
        let body = String(decoding: try JSONSerialization.data(withJSONObject: ["file": file]), as: UTF8.self)
        let rows = try XCTUnwrap(facadeObject(MusicCore.call(piece: 0, "playlists", body))?["playlists"] as? [[String: Any]])
        return Dictionary(uniqueKeysWithValues: rows.map { ($0["name"] as? String ?? "", $0["pieces"] as? [String] ?? []) })
    }

    func playlistsURL() throws -> String { try url("playlists") }

    func playlistsPuts() throws -> [(request: URLRequest, body: String)] {
        let target = try playlistsURL()
        return zip(StubAPI.requests, StubAPI.bodies)
            .filter { $0.0.httpMethod == "PUT" && $0.0.url?.absoluteString == target }
            .map { (request: $0.0, body: String(decoding: $0.1, as: UTF8.self)) }
    }

    /// The API's list with no piece, and its playlists document as given (none: 404).
    func onlinePlaylists(_ file: String?, etag: String = "\"l2\"", put: StubAPI.Answer? = nil) throws -> [String: StubAPI.Answer] {
        let target = try playlistsURL()
        var answers: [String: StubAPI.Answer] = [try url("projects"): StubAPI.Answer(status: 200, body: Data("[]".utf8))]
        if let file {
            answers[target] = StubAPI.Answer(status: 200, body: Data(file.utf8), headers: ["ETag": etag, "Last-Modified": "Thu, 02 Jan 2020 09:00:00 GMT"])
        } else {
            answers[target] = StubAPI.Answer(status: 404, body: Data(#"{"message":"There are no playlists online yet."}"#.utf8))
        }
        if let put { answers["PUT " + target] = put }
        return answers
    }

    /// This iPad last agreed with the online copy (ETag l1) on `base`.
    func knownPlaylists(_ base: String) {
        PlaylistsSyncRecord(etag: "\"l1\"", base: base).save(for: records)
    }

    func testPlaylistsMadeHereGoOnlineAsTheFirstCopyAndAreKnownAfter() async throws {
        let file = try playlists("playlists.create", nil, ["name": "Бах", "pieces": ["p_a"]]).file
        StubAPI.reset(try onlinePlaylists(nil, put: .init(status: 204, body: Data(), headers: ["ETag": "\"l1\""])))

        _ = await sync(playlists: FakePlaylists(file)).run()

        let put = try XCTUnwrap(try playlistsPuts().first)
        XCTAssertNil(put.request.value(forHTTPHeaderField: "If-Match"), "only as the first copy")
        XCTAssertEqual(put.body, file)
        XCTAssertEqual(PlaylistsSyncRecord.load(for: records), PlaylistsSyncRecord(etag: "\"l1\"", base: file))
    }

    func testPlaylistsOnlineComeToAnIPadWithNoneAndNothingIsSent() async throws {
        let online = try playlists("playlists.create", nil, ["name": "Шопен", "pieces": ["p_b"]]).file
        StubAPI.reset(try onlinePlaylists(online))
        let here = FakePlaylists(nil)

        _ = await sync(playlists: here).run()

        let kept = await here.copy?.file
        XCTAssertEqual(kept, online)
        XCTAssertTrue(try playlistsPuts().isEmpty)
        XCTAssertEqual(PlaylistsSyncRecord.load(for: records), PlaylistsSyncRecord(etag: "\"l2\"", base: online))
    }

    /// The common path: synced before and changed only here since, the file goes over the copy last agreed on (If-Match
    /// the record's ETag), and once it is online it is the new agreed copy.
    func testPlaylistsChangedOnlyHereGoOverTheCopyLastAgreedOn() async throws {
        let (base, bach) = try playlists("playlists.create", nil, ["name": "Бах", "pieces": ["p_a"]])
        knownPlaylists(base)
        let mine = try playlists("playlists.add", base, ["id": try XCTUnwrap(bach), "pieces": ["p_b"]]).file
        StubAPI.reset(try onlinePlaylists(base, etag: "\"l1\"", put: .init(status: 204, body: Data(), headers: ["ETag": "\"l2\""])))
        let here = FakePlaylists(mine)

        let outcome = await sync(playlists: here).run()

        XCTAssertFalse(outcome.playlistsNewerOnline)
        let put = try XCTUnwrap(try playlistsPuts().first)
        XCTAssertEqual(put.request.value(forHTTPHeaderField: "If-Match"), "\"l1\"")
        XCTAssertEqual(put.body, mine)
        let kept = await here.copy?.file
        XCTAssertEqual(kept, mine, "nothing to write here")
        XCTAssertEqual(PlaylistsSyncRecord.load(for: records), PlaylistsSyncRecord(etag: "\"l2\"", base: mine))
    }

    /// The first sync with playlists on both sides (each iPad moved its pieces' playlists, or made its own): no base, so the
    /// merge is a union that loses nothing, and it goes over the copy fetched.
    func testTheFirstSyncWithPlaylistsOnBothSidesKeepsAllOfThem() async throws {
        let mine = try playlists("playlists.create", nil, ["name": "Бах", "pieces": ["p_a"]]).file
        let online = try playlists("playlists.create", nil, ["name": "Шопен", "pieces": ["p_b"]]).file
        StubAPI.reset(try onlinePlaylists(online, put: .init(status: 204, body: Data(), headers: ["ETag": "\"l3\""])))
        let here = FakePlaylists(mine)

        _ = await sync(playlists: here).run()

        let merged = await here.copy?.file
        XCTAssertEqual(try shown(merged), ["Бах": ["p_a"], "Шопен": ["p_b"]])
        let put = try XCTUnwrap(try playlistsPuts().first)
        XCTAssertEqual(put.request.value(forHTTPHeaderField: "If-Match"), "\"l2\"")
        XCTAssertEqual(put.body, merged)
        XCTAssertEqual(PlaylistsSyncRecord.load(for: records), PlaylistsSyncRecord(etag: "\"l3\"", base: merged))
    }

    /// Changed on both sides: the merge keeps the piece added here and the playlist made on the other iPad, and goes over
    /// the copy fetched; a pass with nothing changed since asks nothing more than the two reads.
    func testPlaylistsChangedOnBothMergeAndGoOverTheCopyFetched() async throws {
        let (base, bach) = try playlists("playlists.create", nil, ["name": "Бах", "pieces": ["p_a"]])
        knownPlaylists(base)
        let mine = try playlists("playlists.add", base, ["id": try XCTUnwrap(bach), "pieces": ["p_b"]]).file
        let online = try playlists("playlists.create", base, ["name": "Шопен", "pieces": ["p_c"]]).file
        StubAPI.reset(try onlinePlaylists(online, put: .init(status: 204, body: Data(), headers: ["ETag": "\"l3\""])))
        let here = FakePlaylists(mine)

        _ = await sync(playlists: here).run()

        let merged = await here.copy?.file
        XCTAssertEqual(try shown(merged), ["Бах": ["p_a", "p_b"], "Шопен": ["p_c"]])
        let put = try XCTUnwrap(try playlistsPuts().first)
        XCTAssertEqual(put.request.value(forHTTPHeaderField: "If-Match"), "\"l2\"", "over the copy it fetched")
        XCTAssertEqual(put.body, merged)
        XCTAssertEqual(PlaylistsSyncRecord.load(for: records), PlaylistsSyncRecord(etag: "\"l3\"", base: merged))

        StubAPI.reset(try onlinePlaylists(try XCTUnwrap(merged), etag: "\"l3\""))
        _ = await sync(playlists: here).run()
        XCTAssertTrue(try playlistsPuts().isEmpty, "nothing changed since")
    }

    /// Saved on another iPad between the read and the save (412): the merged file stays here, and the next pass merges it
    /// with the newer online copy against the one fetched, then sends it over that.
    func testAStaleSaveIsMergedAgainOnTheNextPass() async throws {
        let (base, bach) = try playlists("playlists.create", nil, ["name": "Бах", "pieces": ["p_a"]])
        knownPlaylists(base)
        let mine = try playlists("playlists.add", base, ["id": try XCTUnwrap(bach), "pieces": ["p_b"]]).file
        let online = try playlists("playlists.create", base, ["name": "Шопен", "pieces": []]).file
        StubAPI.reset(try onlinePlaylists(online, put: .init(status: 412, body: Data())))
        let here = FakePlaylists(mine)

        _ = await sync(playlists: here).run()

        XCTAssertEqual(PlaylistsSyncRecord.load(for: records), PlaylistsSyncRecord(etag: "\"l2\"", base: online))
        let newer = try playlists("playlists.create", online, ["name": "Лист", "pieces": []]).file
        StubAPI.reset(try onlinePlaylists(newer, etag: "\"l4\"", put: .init(status: 204, body: Data(), headers: ["ETag": "\"l5\""])))
        _ = await sync(playlists: here).run()

        let kept = await here.copy?.file
        XCTAssertEqual(try shown(kept), ["Бах": ["p_a", "p_b"], "Шопен": [], "Лист": []])
        XCTAssertEqual(try playlistsPuts().first?.request.value(forHTTPHeaderField: "If-Match"), "\"l4\"")
        XCTAssertEqual(PlaylistsSyncRecord.load(for: records).etag, "\"l5\"")
    }

    /// A piece re-keyed on another iPad (#317) is on a playlist online under its id now and here under the one it had: the
    /// pass reads the pieces' former ids after the pieces and the merge keeps it once, under its id now.
    func testAPieceKnownByAnotherIdBeforeIsOnItsPlaylistOnce() async throws {
        let now = "p_" + String(repeating: "n", count: 32)
        let before = "p_" + String(repeating: "o", count: 32)
        try Data(try edited(try projectFile(id: now)) { $0["formerIds"] = [before] }.utf8).write(to: folder.appendingPathComponent("Elise.motiff.json"))
        let rekeyed = await PlaylistsSync.rekeyed(in: folder)
        XCTAssertEqual(rekeyed.map { $0["id"] as? String }, [now])
        XCTAssertEqual(rekeyed.map { $0["formerIds"] as? [String] }, [[before]])

        let (base, bach) = try playlists("playlists.create", nil, ["name": "Бах", "pieces": []])
        knownPlaylists(base)
        let mine = try playlists("playlists.add", base, ["id": try XCTUnwrap(bach), "pieces": [before]]).file
        let online = try playlists("playlists.add", base, ["id": try XCTUnwrap(bach), "pieces": [now]]).file
        let here = FakePlaylists(mine)
        // The piece is online too, unchanged since this iPad saw it, so the pass asks nothing of it.
        let file = try String(contentsOf: folder.appendingPathComponent("Elise.motiff.json"), encoding: .utf8)
        SyncRecords.save([now: SyncRecord(etag: "\"e1\"", savedAt: "2020-01-01T09:00:00+00:00", hash: CloudSync.hash(Data(file.utf8)))], to: records)
        StubAPI.reset(try onlinePlaylists(online, put: .init(status: 204, body: Data(), headers: ["ETag": "\"l3\""])).merging(try listing(now, savedAt: "2020-01-01T09:00:00+00:00")) { _, list in list })

        _ = await sync(playlists: here).run()

        let kept = await here.copy?.file
        XCTAssertEqual(try shown(kept), ["Бах": [now]])
    }

    /// A later build's copy online (the lead's decision, p-3 review 1) is never merged, taken here nor written over: the
    /// file here stays as the player left it, nothing is sent, nothing is recorded as agreed, and the pass says so.
    func testALaterBuildsOnlineCopyLeavesThePlaylistsHereAsTheyAreAndIsNeverWrittenOver() async throws {
        let (base, bach) = try playlists("playlists.create", nil, ["name": "Бах", "pieces": ["p_a"]])
        knownPlaylists(base)
        let mine = try playlists("playlists.add", base, ["id": try XCTUnwrap(bach), "pieces": ["p_b"]]).file
        let later = try edited(base) { object in
            object["version"] = 2
            object["future"] = 1
        }
        StubAPI.reset(try onlinePlaylists(later, put: .init(status: 204, body: Data(), headers: ["ETag": "\"l3\""])))
        let here = FakePlaylists(mine)

        let outcome = await sync(playlists: here).run()

        XCTAssertTrue(outcome.playlistsNewerOnline)
        let kept = await here.copy?.file
        XCTAssertEqual(kept, mine)
        XCTAssertTrue(try playlistsPuts().isEmpty)
        XCTAssertEqual(PlaylistsSyncRecord.load(for: records), PlaylistsSyncRecord(etag: "\"l1\"", base: base))

        // An iPad with none takes none either.
        let none = FakePlaylists(nil)
        let again = await sync(playlists: none).run()
        XCTAssertTrue(again.playlistsNewerOnline)
        let taken = await none.copy
        XCTAssertNil(taken)
    }

    /// Where the app may not write, a pass only fetches: changed on both, nothing moves; changed online only, it comes here.
    func testWhereTheAppMayNotWriteOnlyTheOnlineCopyComes() async throws {
        let (base, bach) = try playlists("playlists.create", nil, ["name": "Бах", "pieces": ["p_a"]])
        knownPlaylists(base)
        let mine = try playlists("playlists.add", base, ["id": try XCTUnwrap(bach), "pieces": ["p_b"]]).file
        let online = try playlists("playlists.create", base, ["name": "Шопен", "pieces": []]).file
        StubAPI.reset(try onlinePlaylists(online))
        let changed = FakePlaylists(mine)

        _ = await sync(playlists: changed, writable: false).run()

        let left = await changed.copy?.file
        XCTAssertEqual(left, mine)
        XCTAssertFalse(StubAPI.requests.contains { $0.httpMethod == "PUT" })

        let unchanged = FakePlaylists(base)
        _ = await sync(playlists: unchanged, writable: false).run()
        let came = await unchanged.copy?.file
        XCTAssertEqual(came, online)
    }

    /// The player edits the playlists while a pass brings the online copy: the edit stays, nothing is recorded as agreed,
    /// and the next pass merges the two.
    func testAnEditMadeWhileThePassRunsIsNeverWrittenOver() async throws {
        let base = try playlists("playlists.create", nil, ["name": "Бах", "pieces": ["p_a"]]).file
        knownPlaylists(base)
        let online = try playlists("playlists.create", base, ["name": "Шопен", "pieces": []]).file
        let edit = try playlists("playlists.create", base, ["name": "Лист", "pieces": []]).file
        StubAPI.reset(try onlinePlaylists(online))
        let here = FakePlaylists(base)
        await here.editWhilePassRuns(edit)

        _ = await sync(playlists: here).run()

        let kept = await here.copy?.file
        XCTAssertEqual(kept, edit)
        XCTAssertEqual(PlaylistsSyncRecord.load(for: records), PlaylistsSyncRecord(etag: "\"l1\"", base: base))
    }

    /// Gone online (everything erased from another device) and unchanged here: kept here and not sent again until it
    /// changes here, as a piece is; then it goes as the first copy.
    func testPlaylistsGoneOnlineAreNotSentAgainUntilTheyChangeHere() async throws {
        let (base, bach) = try playlists("playlists.create", nil, ["name": "Бах", "pieces": ["p_a"]])
        knownPlaylists(base)
        StubAPI.reset(try onlinePlaylists(nil, put: .init(status: 204, body: Data(), headers: ["ETag": "\"l9\""])))
        let here = FakePlaylists(base)

        _ = await sync(playlists: here).run()

        XCTAssertTrue(try playlistsPuts().isEmpty)
        XCTAssertEqual(PlaylistsSyncRecord.load(for: records), PlaylistsSyncRecord(base: base, deletedOnline: true))

        let changed = try playlists("playlists.add", base, ["id": try XCTUnwrap(bach), "pieces": ["p_b"]]).file
        _ = await sync(playlists: FakePlaylists(changed)).run()

        let put = try XCTUnwrap(try playlistsPuts().first)
        XCTAssertNil(put.request.value(forHTTPHeaderField: "If-Match"))
        XCTAssertEqual(put.body, changed)
    }
}
