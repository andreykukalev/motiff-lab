import XCTest
@testable import MotiffLab

/// Scan jobs on the iPad (version 1.3, decision record 0035 section 6, spec part 5): the estimate from `/health`, the line
/// before sending, a job's state and the pause between asks, its result given to music-core's merge with the pages read
/// on the device, the job kept on the device, and the words in both languages.
@MainActor
final class ScanJobsTests: XCTestCase {
    private let english = UIText(language: "en")
    private let russian = UIText(language: "ru")

    /// A page of one bar with one note, as an engine would read it.
    private func page(_ step: String) -> String {
        """
        <?xml version="1.0" encoding="UTF-8"?><score-partwise version="3.1"><part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>\
        <part id="P1"><measure number="1"><attributes><divisions>1</divisions><key><fifths>0</fifths></key><time><beats>4</beats><beat-type>4</beat-type></time>\
        <clef><sign>G</sign><line>2</line></clef></attributes><note><pitch><step>\(step)</step><octave>4</octave></pitch><duration>4</duration><type>whole</type></note></measure></part></score-partwise>
        """
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
        XCTAssertEqual(health.minutes(pages: 1), 2, "90 s, rounded up")
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
        let bare = ScanJobs.joined(device: [:], server: [try json(#"{"page": 1, "engines": {"homr": {"state": "read", "musicxml": "H"}}}"#)])
        XCTAssertNil((bare.pages.first as? [String: Any])?["evidence"], "no evidence is sent when the server gave none")
    }

    func testMusicCoreMergesAJobsReadingsWithThePageReadOnTheDevice() async throws {
        let reading: [String: Any] = ["state": "read", "musicxml": page("E")]
        let engines: [String: Any] = ["homr": reading, "copisteria": reading]
        let joined = ScanJobs.joined(device: [1: page("C")], server: [["page": 2, "engines": engines]])
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
}
