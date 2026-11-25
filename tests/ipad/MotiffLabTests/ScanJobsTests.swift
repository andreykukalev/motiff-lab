import XCTest
@testable import MotiffLab

/// Scan jobs on the iPad (version 1.3, decision record 0035 section 6, spec part 5): the estimate from `/health`, the line
/// before sending, a job's state and the pause between asks, its result given to music-core's merge with the pages read
/// on the device, the job kept on the device, and the words in both languages.
@MainActor
final class ScanJobsTests: XCTestCase {
    private let english = UIText(language: "en")
    private let russian = UIText(language: "ru")
    /// The experiment as it was before a test turned it on (`ScanSupport.jobsOn`).
    private var jobsBefore: String?

    override func tearDown() async throws {
        await ScanSupport.restore(jobsBefore)
        jobsBefore = nil
    }

    /// A JSON object as the server writes it.
    private func json(_ text: String) throws -> [String: Any] {
        try XCTUnwrap(JSONSerialization.jsonObject(with: Data(text.utf8)) as? [String: Any])
    }

    func testTheHealthOfAJobServerGivesTheEstimateAndAnOlderServerTakesNoJobs() throws {
        let object = try json("""
        {"engine": "homr", "version": "0.7.0", "engines": [{"engine": "homr", "secondsPerPage": 75}, {"engine": "copisteria", "secondsPerPage": 90},
         {"engine": "audiveris", "secondsPerPage": 70}, {"engine": "transcoda", "secondsPerPage": 125}], "jobs": {"queue": 0, "workers": 4}}
        """)
        let health = try XCTUnwrap(ScanHealth(object))

        XCTAssertEqual(health.engine, "homr 0.7.0")
        XCTAssertTrue(health.takesJobs)
        XCTAssertEqual(health.minutes(pages: 2), 3, "2 × 360 s shared by 4 at once is 180 s: 3 minutes")
        XCTAssertEqual(health.minutes(pages: 1), 3, "never less than the slowest engine's 125 s for a page")
        XCTAssertNil(health.minutes(pages: 0))
        let busy = ScanHealth(engine: "homr 0.7.0", secondsPerPage: health.secondsPerPage, queue: 8, workers: 4)
        XCTAssertEqual(busy.minutes(pages: 2), 6, "8 units ahead at the mean 90 s add 180 s")
        let older = try XCTUnwrap(ScanHealth(["engine": "homr", "version": "0.7.0"]))
        XCTAssertFalse(older.takesJobs, "a server before version 1.3: the PDF goes to /recognise")
        XCTAssertNil(ScanHealth(["status": "ok"]), "not a scan server")
    }

    func testTheLineBeforeSendingSaysWhatTheIPadReadAndWhatGoesToTheServer() {
        let plan = ScanJobPlan(device: [1: "a", 2: "b", 3: "c", 5: "d"], engine: "vector 1", server: [4, 6])

        XCTAssertEqual(plan.line(russian, minutes: 3), "4 страницы прочитаны на iPad. 2 страницы отправим на распознавание, около 3 минут.")
        XCTAssertEqual(plan.line(english, minutes: 1), "4 pages read on this iPad. 2 pages go to be recognised, about 1 minute.")
        XCTAssertEqual(ScanJobPlan(server: [1]).line(russian, minutes: 1), "1 страницу отправим на распознавание, около 1 минуты.")
        XCTAssertEqual(ScanJobPlan(device: [1: "a"]).line(russian, minutes: nil), "1 страница прочитана на iPad.", "nothing to send")
    }

    func testAJobsStateGivesThePagesEveryEngineIsDoneWithAndTheMinutesLeft() throws {
        let object = try json("""
        {"id": "j1", "state": "running", "progress": 0.4, "etaSeconds": 61,
         "pages": [{"page": 4, "engines": {"homr": "read", "copisteria": "not_read"}}, {"page": 6, "engines": {"homr": "reading", "copisteria": "retrying"}}]}
        """)
        let status = try XCTUnwrap(ScanJobStatus(object))

        XCTAssertFalse(status.finished)
        XCTAssertEqual(status.progress.pages, 2)
        XCTAssertEqual(status.progress.done, 1)
        XCTAssertEqual(status.progress.minutesLeft, 2, "61 s is about 2 minutes")
        XCTAssertEqual(RecognitionTop.jobLine(status.progress, russian), "распознаётся · 1 из 2 страниц · ещё около 2 мин.")
        XCTAssertEqual(RecognitionTop.jobLine(status.progress, english), "being recognised · 1 of 2 pages · about 2 min. left")
        XCTAssertEqual(ScanJobDetail.engines(status.pages[1], russian), "homr: читает · copisteria: повторяет", "in the engines' order")
        let done = ScanJobStatus(state: "done", pages: status.pages, etaSeconds: 0)
        XCTAssertTrue(done.finished)
        XCTAssertNil(done.progress.minutesLeft)
        XCTAssertTrue(ScanJobStatus(state: "failed", pages: [], etaSeconds: 0).finished, "no page read: the result is fetched and the reason shown")
    }

    func testTheStateIsAskedEveryTwoAndAHalfSecondsAndLessOftenWhileTheServerDoesNotAnswer() {
        XCTAssertEqual(ScanJobs.pause(misses: 0), 2.5)
        XCTAssertEqual(ScanJobs.pause(misses: 1), 5)
        XCTAssertEqual(ScanJobs.pause(misses: 3), 20)
        XCTAssertEqual(ScanJobs.pause(misses: 5), 60, "at most a minute")
        XCTAssertEqual(ScanJobs.pause(misses: 40), 60)
    }

    func testAJobsResultGoesToTheMergeAsReadingsInOrderWithItsEvidenceBesideTheDevicesPages() throws {
        let result = try json("""
        {"id": "j1", "pages": [
          {"page": 3, "engines": {"homr": {"state": "not_read", "message": "Page 3 could not be read."}, "copisteria": {"state": "not_read"}}},
          {"page": 2, "engines": {"transcoda": {"state": "read", "version": "2.1", "musicxml": "T"}, "homr": {"state": "read", "version": "0.7.0", "musicxml": "H"},
            "audiveris": {"state": "not_read"}, "copisteria": {"state": "read", "version": "1.3.4", "musicxml": "C"}}, "evidence": {"evidence": 1, "page": 2}}]}
        """)
        let server = try XCTUnwrap(result["pages"] as? [[String: Any]])

        let joined = ScanJobs.joined(device: [1: "V"], server: server)

        XCTAssertEqual(joined.pages.count, 2, "page 3, which no engine read, is left out")
        XCTAssertEqual(joined.pages.first as? String, "V", "a page read on the device goes as its MusicXML")
        let merged = try XCTUnwrap(joined.pages.last as? [String: Any])
        let readings = try XCTUnwrap(merged["readings"] as? [[String: String]])
        XCTAssertEqual(readings.map { $0["engine"] }, ["homr", "copisteria", "transcoda"], "the engines that read, in a fixed order")
        XCTAssertEqual(readings.map { $0["musicxml"] }, ["H", "C", "T"])
        XCTAssertEqual((merged["evidence"] as? [String: Any])?["page"] as? Int, 2)
        XCTAssertEqual(joined.engines, ["homr 0.7.0", "copisteria 1.3.4", "transcoda 2.1"])
        XCTAssertEqual(joined.unread, [3])
        XCTAssertEqual(RecognitionTop.unreadLine(joined.unread, russian), "стр. 3 не распознана")
        XCTAssertEqual(RecognitionTop.unreadLine([3, 5], english), "pages 3, 5 not recognised")
        XCTAssertNil(joined.reports, "the device's page has no report here, so none are passed on")
        let one = try json(#"{"page": 1, "engines": {"homr": {"state": "read", "musicxml": "H"}}}"#)
        let bare = ScanJobs.joined(device: [:], server: [one])
        XCTAssertNil((bare.pages.first as? [String: Any])?["evidence"], "no evidence is sent when the server gave none")
    }

    func testEachPageJoinedKeepsItsPageReportAsRecogniseAndAutoPassThemOn() throws {
        let entry = try json("""
        {"page": 2, "engines": {"copisteria": {"state": "read", "musicxml": "C", "report": {"page": 2, "measures": 7}},
          "homr": {"state": "read", "musicxml": "H", "report": {"page": 2, "measures": 8}}}}
        """)

        let joined = ScanJobs.joined(device: [1: "V"], reports: [1: #"[{"page":1}]"#], server: [entry])

        XCTAssertEqual(joined.reports, [#"[{"page":1}]"#, #"[{"measures":8,"page":2}]"#], "the device's, then the first engine's in order, homr")
        XCTAssertNil(ScanJobs.joined(device: [1: "V"], server: [entry]).reports, "nil unless every page has one")
    }

    func testMusicCoreMergesAJobsReadingsWithThePageReadOnTheDevice() async throws {
        let reading: [String: Any] = ["state": "read", "musicxml": ScanSupport.page("E")]
        let engines: [String: Any] = ["homr": reading, "copisteria": reading]
        let joined = ScanJobs.joined(device: [1: ScanSupport.page("C")], server: [["page": 2, "engines": engines]])
        let json = facadeRequest(["pages": joined.pages])

        let answer = await MusicCore.run { MusicCore.call(piece: 0, "scan.join", json) }

        let object = try XCTUnwrap(facadeObject(answer))
        XCTAssertNil(object["error"], "\(object)")
        let pages = try XCTUnwrap(object["pages"] as? [[String: Any]])
        XCTAssertEqual(pages.count, 2)
        XCTAssertNotNil(pages[1]["merge"], "the job's page is merged by music-core")
        XCTAssertNil(pages[0]["merge"], "the device's page is kept as read")
    }

    func testTheJobIsKeptOnTheDeviceAndABatchFromBeforeStillOpens() throws {
        let older = #"{"id":"b1","address":"https://scan.example.org/","hosted":false,"items":[{"file":"a.motiff.json","title":"A","pages":2,"state":"queued"}],"paused":false,"startedAt":"2026-11-20T10:00:00Z"}"#
        let batch = try ImportFolders.decoder.decode(RecognitionBatch.self, from: Data(older.utf8))
        XCTAssertNil(batch.items[0].job)
        XCTAssertNil(batch.items[0].unread)

        var item = batch.items[0]
        item.job = ScanJobRecord(id: "j1", pages: [2])
        item.unread = [2]
        let again = try ImportFolders.decoder.decode(RecognitionItem.self, from: ImportFolders.encoder.encode(item))
        XCTAssertEqual(again.job, ScanJobRecord(id: "j1", pages: [2]))
        XCTAssertEqual(again.unread, [2])
    }

    func testTheExperimentIsOffUntilTurnedOnAndItsWordsAreInBothLanguages() async {
        let settings = DeviceSettings(store: UserDefaults(suiteName: "ScanJobsTests-\(UUID().uuidString)")!)
        await settings.load()
        XCTAssertEqual(ScanServer.jobsKey, "experimental.scanJobs")
        XCTAssertFalse(ScanServer.jobsOn(settings), "off: scans go to /recognise as before")
        XCTAssertEqual(settings.catalogue[ScanServer.jobsKey]?.scope, "device")

        var keys = ["pdf.readLineJob", "pdf.readLineHereJob", "pdf.readLineHostedJob", "recognition.whereHostedJob", "folderImport.recogniseHostedJob",
                    "recognition.retry", "scanJob.page", "scanJob.engine", "scanJob.detailHint", "settings.scanJobs", "settings.scanJobsHint"]
        keys += ["waiting", "reading", "read", "retrying", "not_read"].map { "scanJob.state." + $0 }
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            for key in keys { XCTAssertNotEqual(text[key], key, "\(language): \(key)") }
        }
        XCTAssertTrue(english["recognition.whereHostedJob"].contains("at most 24 hours"), "the job path's promise (0035 section 4)")
        XCTAssertTrue(russian["pdf.readLineHostedJob"].contains("не дольше 24 часов"))
        XCTAssertTrue(english["recognition.whereHosted"].hasSuffix("deletes them once read."), "/recognise keeps its own")
    }

    // MARK: - The requests, against a stub server

    private let server = ScanSupport.server
    private let notAPdf = Data("not a pdf".utf8)

    private var target: ScanTarget { ScanTarget(address: server, account: Account(options: nil), session: StubAPI.session()) }

    private var jobServer: StubAPI.Answer {
        ScanSupport.answer(200, #"{"engine": "homr", "version": "0.7.0", "engines": [{"engine": "homr", "secondsPerPage": 75}], "jobs": {"queue": 0, "workers": 1}}"#)
    }

    private func status(_ state: String) -> StubAPI.Answer {
        let engine = state == "done" ? "read" : "reading"
        return ScanSupport.answer(200, #"{"id": "j1", "state": "\#(state)", "etaSeconds": 30, "pages": [{"page": 1, "engines": {"homr": "\#(engine)"}}, {"page": 2, "engines": {"homr": "\#(engine)"}}]}"#)
    }

    /// The job's result: page 1 read by homr with its report, page 2 read by none.
    private var result: StubAPI.Answer {
        let reading: [String: Any] = ["state": "read", "version": "0.7.0", "musicxml": ScanSupport.page("C"), "report": ["page": 1, "measures": 1]]
        let pages: [[String: Any]] = [["page": 1, "engines": ["homr": reading]], ["page": 2, "engines": ["homr": ["state": "not_read"] as [String: Any]]]]
        let body = try? JSONSerialization.data(withJSONObject: ["id": "j1", "pages": pages] as [String: Any])
        return StubAPI.Answer(status: 200, body: body ?? Data())
    }

    func testAJobIsSentAskedUntilDoneAndItsReadingsMergedWithTheJobLeftForTheCallerToDelete() async throws {
        StubAPI.reset(["http://scan.test/health": jobServer, "POST http://scan.test/jobs": ScanSupport.answer(202, #"{"id": "j1", "status": "/jobs/j1"}"#), "http://scan.test/jobs/j1": status("running")])
        var kept: [ScanJobRecord] = []
        var heard: [String] = []
        var pauses: [Double] = []

        let outcome = await ScanServer.readByJob(
            fileName: "a.pdf", data: notAPdf, pages: 2, to: target, keep: { kept.append($0) }, hear: { heard.append($0.state) },
            pause: { seconds in
                pauses.append(seconds)
                StubAPI.answer(["http://scan.test/jobs/j1": self.status("done"), "http://scan.test/jobs/j1/result": self.result])
            }
        )

        guard case .success(let read)? = outcome else { return XCTFail("read: \(String(describing: outcome))") }
        XCTAssertEqual(kept, [ScanJobRecord(id: "j1", pages: [1, 2])], "the id is kept as soon as the server gives it")
        XCTAssertEqual(read.job, kept.first)
        XCTAssertEqual(heard, ["running", "done"])
        XCTAssertEqual(pauses, [2.5])
        XCTAssertEqual(read.result.unread, [2])
        XCTAssertEqual(read.result.engine, "homr 0.7.0")
        XCTAssertEqual(read.result.partReports?.count, 1, "page 1's report goes on to document.read")
        let form = String(decoding: StubAPI.bodies[StubAPI.requests.firstIndex { $0.httpMethod == "POST" } ?? 0], as: UTF8.self)
        XCTAssertTrue(form.contains("name=\"pages\"\r\n\r\n1,2\r\n"), "the pages the device did not read")
        XCTAssertEqual(ScanSupport.sent("DELETE", "jobs/j1"), 0, "deleted by the caller once the notes are saved")
    }

    func testAJobKeptFromBeforeIsAskedAgainWithoutSendingThePdf() async {
        StubAPI.reset(["http://scan.test/jobs/j1": status("done"), "http://scan.test/jobs/j1/result": result])

        let outcome = await ScanServer.readByJob(fileName: "a.pdf", data: notAPdf, pages: 2, to: target, job: ScanJobRecord(id: "j1", pages: [1, 2]))

        guard case .success? = outcome else { return XCTFail("read: \(String(describing: outcome))") }
        XCTAssertEqual(ScanSupport.sent("POST", "jobs"), 0)
        XCTAssertEqual(ScanSupport.sent("GET", "health"), 0)
    }

    func testTheServerIsGivenTwoMinutesToWakeAndOneThatDoesNotAnswerIsNotSentThePdf() async {
        StubAPI.reset(["http://scan.test/health": jobServer])
        _ = await ScanServer.health(server, session: StubAPI.session())
        XCTAssertEqual(StubAPI.requests.first?.timeoutInterval, 120, "Motiff Lab's server took 80 s to wake from zero (2026-11-25)")

        StubAPI.reset()
        let outcome = await ScanServer.readByJob(fileName: "a.pdf", data: notAPdf, pages: 2, to: target)

        guard case .failure(let problem)? = outcome else { return XCTFail("no answer: \(String(describing: outcome))") }
        XCTAssertEqual(problem, .unreachable(server), "said at once, not tried again by /recognise")
        XCTAssertEqual(ScanSupport.sent("GET", "health"), 1)
        XCTAssertEqual(StubAPI.requests.count, 1, "nothing else is sent")
    }

    func testAServerSlowToAnswerIsSaidToBeWakingAndAQuickOneIsNot() async {
        var waking = jobServer
        waking.delay = ScanJobs.wakingAfter + 0.5
        StubAPI.reset(["http://scan.test/health": waking])
        var heard: [ScanJobStatus] = []

        let slow = await ScanServer.takesJobs(target, pages: [1, 2], hear: { heard.append($0) })

        XCTAssertEqual(slow, .success(true))
        XCTAssertEqual(heard.map(\.state), ["waking", "sending"], "once it answers, no line says it still wakes")
        XCTAssertEqual(heard.last.map { RecognitionTop.jobLine($0.progress, russian) }, "распознаётся · 0 из 2 страниц")
        let line = heard.first?.progress
        XCTAssertEqual(line.map { RecognitionTop.jobLine($0, russian) }, "Сервер распознавания просыпается…")
        XCTAssertEqual(line?.caption(english), "The scan server is waking up…", "the panel's bar")
        XCTAssertEqual(line?.pages, 2)
        XCTAssertNil(line?.minutesLeft)

        StubAPI.reset(["http://scan.test/health": jobServer])
        heard = []
        let quick = await ScanServer.takesJobs(target, pages: [1], hear: { heard.append($0) })

        XCTAssertEqual(quick, .success(true))
        XCTAssertTrue(heard.isEmpty, "an awake server answers before the line would change")
    }

    func testAServerWithoutJobsLeavesThePdfToRecogniseAsBefore() async {
        StubAPI.reset(["http://scan.test/health": ScanSupport.answer(200, #"{"engine": "homr", "version": "0.7.0"}"#)])

        let outcome = await ScanServer.readByJob(fileName: "a.pdf", data: notAPdf, pages: 2, to: target)

        XCTAssertNil(outcome)
        XCTAssertEqual(ScanSupport.sent("POST", "jobs"), 0)
    }

    func testNoAnswerAndA503WhilePollingAreMissesThatDoubleThePauseNotFailures() async {
        StubAPI.reset(["http://scan.test/jobs/j1/result": result])
        var pauses: [Double] = []

        let outcome = await ScanServer.readByJob(
            fileName: "a.pdf", data: notAPdf, pages: 2, to: target, job: ScanJobRecord(id: "j1", pages: [1, 2]),
            pause: { seconds in
                pauses.append(seconds)
                StubAPI.answer(["http://scan.test/jobs/j1": pauses.count == 1 ? ScanSupport.answer(503, "{}") : self.status("done")])
            }
        )

        guard case .success? = outcome else { return XCTFail("read: \(String(describing: outcome))") }
        XCTAssertEqual(pauses, [5, 10], "no answer, then a 503: the pause doubles each time")
    }

    func testAJobTheServerNoLongerHasIsAFailureWithItsWords() async {
        StubAPI.reset(["http://scan.test/jobs/j1": ScanSupport.answer(404, #"{"code": "job", "message": "There is no such scan job."}"#)])

        let outcome = await ScanServer.readByJob(fileName: "a.pdf", data: notAPdf, pages: 2, to: target, job: ScanJobRecord(id: "j1", pages: [1, 2]))

        guard case .failure(.server("There is no such scan job."))? = outcome else { return XCTFail("gone: \(String(describing: outcome))") }
    }

    func testAJobNoLongerWaitedForStopsAndStaysOnTheServer() async {
        StubAPI.reset(["http://scan.test/jobs/j1": status("running")])
        var stopped = false

        let outcome = await ScanServer.readByJob(
            fileName: "a.pdf", data: notAPdf, pages: 2, to: target, job: ScanJobRecord(id: "j1", pages: [1, 2]),
            hear: { _ in stopped = true }, wanted: { !stopped }, pause: { _ in }
        )

        guard case .failure(.stopped)? = outcome else { return XCTFail("stopped: \(String(describing: outcome))") }
        XCTAssertEqual(ScanSupport.sent("DELETE", "jobs/j1"), 0, "kept, to be asked again on «Продолжить»")
        XCTAssertEqual(ScanSupport.sent("GET", "jobs/j1/result"), 0)
    }

    func testDeletingAJobIsOneDeleteOfIt() async {
        StubAPI.reset(["DELETE http://scan.test/jobs/j1": ScanSupport.answer(204, "")])

        await ScanServer.deleteJob("j1", to: target)

        XCTAssertEqual(ScanSupport.sent("DELETE", "jobs/j1"), 1)
    }

    // MARK: - The queue's pieces end to end (the reviewer's gap of i-1; v1.3 i-1b)

    private func follow(_ kept: ScanJobRecord? = nil, ready: @escaping () async -> Bool = { true }) -> RecognitionJob {
        RecognitionJob(kept: kept, keep: { _ in }, hear: { _ in }, wanted: { true }, ready: ready)
    }

    func testAPieceReadByAJobIsWrittenThenItsJobDeletedAndAPageNoEngineReadIsReadAgainIntoTheSameNotes() async throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("queue-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        let url = try ScanSupport.pdfPiece(in: folder)
        let done = ScanSupport.answer(200, #"{"id": "j", "state": "done", "etaSeconds": 0, "pages": []}"#)
        StubAPI.reset(["http://scan.test/health": jobServer, "POST http://scan.test/jobs": ScanSupport.answer(202, #"{"id": "j1"}"#), "http://scan.test/jobs/j1": done,
                       "http://scan.test/jobs/j1/result": result, "DELETE http://scan.test/jobs/j1": ScanSupport.answer(204, "")])

        let first = await RecognitionQueue.recognise(url, address: server, account: Account(options: nil), links: folder, folders: folder, job: follow(), session: StubAPI.session(), isOpen: { false })

        guard case .success(let read) = first else { return XCTFail("read: \(first)") }
        XCTAssertEqual(read.unread, [2])
        XCTAssertEqual(read.read, [1], "the PDF pages the notes have, kept for a page read again")
        let summary = await PieceSummary.read(url)
        XCTAssertEqual(summary?.hasNotes, true, "the notes are written into the piece's file")
        XCTAssertEqual(ScanSupport.sent("DELETE", "jobs/j1"), 1, "then the job is deleted on the server")

        // «Повторить»: only page 2 goes, as a new job, and its bar goes in after page 1's into the same notes.
        StubAPI.reset(["http://scan.test/health": jobServer, "POST http://scan.test/jobs": ScanSupport.answer(202, #"{"id": "j2"}"#), "http://scan.test/jobs/j2": done,
                       "http://scan.test/jobs/j2/result": ScanSupport.result("j2", [(2, "E")]), "DELETE http://scan.test/jobs/j2": ScanSupport.answer(204, "")])

        // While the job runs the player corrects a note and the screen saves the file: that correction stays.
        let again = await RecognitionQueue.recognise(url, address: server, account: Account(options: nil), links: folder, folders: folder, job: follow(ready: { (try? ScanSupport.change(url, "corrections.change", #"{"bar": 1, "id": "n_0001", "pitch": 1}"#)) != nil }), again: [2], read: read.read, session: StubAPI.session(), isOpen: { false })

        guard case .success(let inserted) = again else { return XCTFail("again: \(again)") }
        XCTAssertEqual(inserted.unread, [], "page 2 is read now")
        XCTAssertEqual(inserted.read, [1, 2])
        XCTAssertNil(inserted.why)
        let form = String(decoding: StubAPI.bodies[StubAPI.requests.firstIndex { $0.httpMethod == "POST" } ?? 0], as: UTF8.self)
        XCTAssertTrue(form.contains("name=\"pages\"\r\n\r\n2\r\n"), "only the page no engine read")
        XCTAssertEqual(ScanSupport.sent("DELETE", "jobs/j2"), 1)
        XCTAssertEqual(try ScanSupport.pitches(url, bar: 1), [61], "the correction saved during the wait is in the notes")
        XCTAssertEqual(try ScanSupport.pitches(url, bar: 2), [64], "bar 2 is page 2's E4")
    }

    func testAPageReadAgainThatNoEngineReadStaysUnreadAndThePieceKeepsItsNotes() async throws {
        let file = FileManager.default.temporaryDirectory.appendingPathComponent("again-\(UUID().uuidString).motiff.json")
        try Data("{}".utf8).write(to: file)
        defer { try? FileManager.default.removeItem(at: file) }
        StubAPI.reset(["http://scan.test/health": jobServer, "POST http://scan.test/jobs": ScanSupport.answer(202, #"{"id": "j3"}"#),
                       "http://scan.test/jobs/j3": ScanSupport.answer(200, #"{"id": "j3", "state": "done", "etaSeconds": 0, "pages": []}"#), "http://scan.test/jobs/j3/result": ScanSupport.result("j3", [(2, nil)])])
        let outcome = await ScanPageAgain.read([2], read: [1], pages: 2, file: file, pdf: notAPdf, to: target, job: follow(), text: english)

        guard case .success(let read) = outcome else { return XCTFail("again: \(outcome)") }
        XCTAssertEqual(read.unread, [2], "the line keeps «Повторить»")
        XCTAssertEqual(read.file, "{}", "the notes as they were")
        XCTAssertEqual(read.job.id, "j3")
        XCTAssertEqual(read.read, [1])
        XCTAssertNil(read.why)
        XCTAssertEqual(ScanPageAgain.place(of: 3, read: [1, 2, 5]), 3, "after the pages the notes have before it")
        XCTAssertEqual(ScanPageAgain.place(of: 1, read: [2, 3]), 1)
        for key in ["recognition.retryPagesHint", "recognition.againWhy"] {
            XCTAssertNotEqual(english[key], key)
            XCTAssertNotEqual(russian[key], key)
        }
    }

    func testPagesReadAgainThatDidNotWorkLeaveThePieceWithItsNotesAndTheReasonOnItsLine() async throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("queue-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        let url = try ScanSupport.pdfPiece(in: folder)
        StubAPI.reset(["http://scan.test/health": jobServer, "POST http://scan.test/jobs": ScanSupport.answer(202, #"{"id": "j1"}"#),
                       "http://scan.test/jobs/j1": ScanSupport.answer(200, #"{"id": "j1", "state": "done", "etaSeconds": 0, "pages": []}"#), "http://scan.test/jobs/j1/result": result])
        guard case .success(let first) = await RecognitionQueue.recognise(url, address: server, account: Account(options: nil), links: folder, folders: folder, job: follow(), session: StubAPI.session(), isOpen: { false }) else {
            return XCTFail("the first read")
        }
        // «Повторить» with the experiment on: the page goes as a new job, and the server no longer has it when asked.
        jobsBefore = await ScanSupport.jobsOn()
        StubAPI.reset(["http://scan.test/health": jobServer, "POST http://scan.test/jobs": ScanSupport.answer(202, #"{"id": "j2"}"#),
                       "http://scan.test/jobs/j2": ScanSupport.answer(404, #"{"code": "job", "message": "There is no such scan job."}"#)])
        let queue = RecognitionQueue()
        queue.root = folder
        queue.projects = { folder }
        queue.account = Account(options: nil)
        queue.session = StubAPI.session()
        var item = RecognitionItem(file: url.lastPathComponent, title: "Pages", pages: 2)
        item.unread = first.unread
        item.read = first.read
        queue.start([item], address: server, hosted: false)

        try await ScanSupport.wait("the page read again") { queue.batch?.finished == true && !queue.running }

        XCTAssertEqual(ScanSupport.sent("POST", "jobs"), 1, "the page was sent as a job")
        let after = try XCTUnwrap(queue.batch?.items.first)
        XCTAssertEqual(after.state, .done, "the piece keeps its notes, not «не вышло»")
        XCTAssertEqual(after.unread, [2], "the line keeps «Повторить»")
        XCTAssertEqual(after.reason, "There is no such scan job.", "and says why, in the server's words")
        XCTAssertEqual(try ScanSupport.pitches(url, bar: 1), [60], "the notes as they were")
    }
}
