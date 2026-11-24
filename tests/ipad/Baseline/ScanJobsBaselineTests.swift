import UIKit
import XCTest

@testable import MotiffLab

/// Scan jobs as 1.3 built them (docs/ux-baseline.md, SCAN-02 to SCAN-05; the spec «Распознавание сканов четырьмя движками в
/// фоне», parts 1, 2a and 5, decision record 0035 section 6, and Andrei's «Вставить в те же ноты» of 2026-11-24 17:41 UTC):
/// the experiment's switch, the line before sending, a piece's progress in the library and its job kept over a relaunch,
/// a page no engine read and «Повторить» for that page. Taken through the queue the library runs and the calls its lines
/// and the PDF's panel make, over the real music-core, with the scan server answered here (`StubAPI`), no network. Only
/// public-domain or synthetic pages: the Mutopia Promenade of the vector reader's fixtures, read where it lies in the
/// repository, and pictures of pages drawn here. The look of the lines, the banner and the panel is the device's.
@MainActor
final class ScanJobsBaselineTests: XCTestCase {
    private let russian = UIText(language: "ru")
    private let english = UIText(language: "en")
    private var folders: [URL] = []
    private var jobsBefore: String?

    override func tearDown() async throws {
        await ScanBaseline.restore(jobsBefore)
        jobsBefore = nil
        for folder in folders { try? FileManager.default.removeItem(at: folder) }
        folders = []
        StubAPI.reset()
    }

    /// A PDF piece of `pages` pictures of pages, with no notes yet, in a folder of its own.
    private func piece(pages: Int) throws -> URL {
        let url = try ScanBaseline.pdfPiece(pdf: ScanBaseline.scannedPdf(pages: pages), pages: pages)
        folders.append(url.deletingLastPathComponent())
        return url
    }

    /// The library's queue over the folder, its requests answered here.
    private func queue(in folder: URL) -> RecognitionQueue {
        let queue = RecognitionQueue()
        queue.root = folder
        queue.projects = { folder }
        queue.account = Account(options: nil)
        queue.session = StubAPI.session()
        return queue
    }

    /// `GET /jobs/{id}`: homr and copisteria done with the pages `read`, homr reading `reading` while copisteria waits.
    private func status(_ id: String, read: [Int], reading: [Int] = []) -> StubAPI.Answer {
        let done: [[String: Any]] = read.map { ["page": $0, "engines": ["homr": "read", "copisteria": "read"]] }
        let going: [[String: Any]] = reading.map { ["page": $0, "engines": ["homr": "reading", "copisteria": "waiting"]] }
        let state = reading.isEmpty ? "done" : "running"
        return ScanBaseline.json(["id": id, "state": state, "etaSeconds": reading.isEmpty ? 0 : 61, "pages": done + going])
    }

    /// `GET /jobs/{id}/result`: each page read by homr as one bar of a whole note on `step` 4, or by no engine (nil).
    private func result(_ id: String, _ pages: [(page: Int, step: String?)]) -> StubAPI.Answer {
        let entries: [[String: Any]] = pages.map { entry in
            let homr: [String: Any] = entry.step.map { ["state": "read", "version": "0.7.0", "musicxml": ScanBaseline.bar(of: $0)] as [String: Any] } ?? ["state": "not_read"]
            return ["page": entry.page, "engines": ["homr": homr]]
        }
        return ScanBaseline.json(["id": id, "pages": entries])
    }

    /// The answers of a server of 1.3 to a new job `id` of the pages `pages`, done at once, and to its DELETE.
    private func newJob(_ id: String, _ pages: [(page: Int, step: String?)]) -> [String: StubAPI.Answer] {
        [
            "http://scan.test/health": ScanBaseline.health, "POST http://scan.test/jobs": ScanBaseline.answer(202, #"{"id": "\#(id)"}"#),
            "http://scan.test/jobs/\(id)": status(id, read: pages.map { $0.page }), "http://scan.test/jobs/\(id)/result": result(id, pages),
            "DELETE http://scan.test/jobs/\(id)": ScanBaseline.answer(204, ""),
        ]
    }

    private func sent(_ method: String, _ path: String) -> Int {
        StubAPI.requests.filter { ($0.httpMethod ?? "GET") == method && $0.url?.absoluteString == "http://scan.test/" + path }.count
    }

    /// The pages the job's upload asked for, as its form wrote them.
    private func pagesSent() -> String? {
        guard let index = StubAPI.requests.firstIndex(where: { $0.httpMethod == "POST" }) else { return nil }
        let form = String(decoding: StubAPI.bodies[index], as: UTF8.self)
        return form.components(separatedBy: "name=\"pages\"\r\n\r\n").dropFirst().first?.components(separatedBy: "\r\n").first
    }

    /// The piece of two pictures of pages read by a first job from the library: page 1 read by homr as C4, page 2 by no engine.
    private func readFirst(_ url: URL) async throws -> RecognitionQueue {
        StubAPI.reset(newJob("j1", [(1, "C"), (2, nil)]))
        let queue = self.queue(in: url.deletingLastPathComponent())
        queue.start([RecognitionItem(file: url.lastPathComponent, title: "Etude", pages: 2)], address: ScanBaseline.server, hosted: false)
        try await ScanBaseline.wait("the first job read and its notes written") { queue.batch?.finished == true && !queue.running }
        return queue
    }

    // MARK: - SCAN-02: the switch

    /// SCAN-02: «Распознавание в фоне четырьмя движками (эксперимент)» is a setting of this iPad, off until turned on; its hint
    /// says what goes on after a relaunch and that off, recognition works as before. Where a PDF goes says, with it on, that
    /// the server keeps it until the iPad has fetched what was read, at most 24 hours. A server before 1.3 takes no jobs.
    func testSCAN02TheExperimentIsASettingOfThisIPadOffUntilTurnedOnAndItsTextsSayHowLongTheServerKeepsThePdf() async throws {
        let settings = DeviceSettings(store: try XCTUnwrap(UserDefaults(suiteName: "ScanJobsBaseline-\(UUID().uuidString)")))
        await settings.load()
        XCTAssertEqual(settings.catalogue[ScanServer.jobsKey]?.scope, "device", "a setting of this iPad")
        XCTAssertFalse(ScanServer.jobsOn(settings), "off on a new iPad: scans go to the server as in 1.2")
        XCTAssertEqual(russian["settings.scanJobs"], "Распознавание в фоне четырьмя движками (эксперимент)")
        let hint = russian["settings.scanJobsHint"]
        XCTAssertTrue(hint.contains("продолжают распознаваться после закрытия и нового открытия приложения, ход виден на пьесе"))
        XCTAssertTrue(hint.contains("«Распознать ноты» на открытом PDF и в Источниках ждёт в своей панели"))
        XCTAssertTrue(hint.contains("если выключено, распознавание работает как раньше"))
        XCTAssertNotEqual(english["settings.scanJobs"], "settings.scanJobs")

        for words in [russian["recognition.whereHostedJob"], russian["pdf.readLineHostedJob"], SourceLayers.counted("sources.sheet.readHostedJob", 2, text: russian)] {
            XCTAssertTrue(words.contains("пока iPad не заберёт прочитанное, но не дольше 24 часов"), words)
        }
        XCTAssertFalse(try XCTUnwrap(ScanHealth(["engine": "homr", "version": "0.7.0"])).takesJobs, "a server before 1.3: the PDF goes as before")
    }

    // MARK: - SCAN-03: the line before sending

    /// SCAN-03: before a PDF is sent as a job, the panel says which pages the iPad read itself, which go to the server and
    /// about how long they take there, from what the server says of its engines and its queue (`/health`). A PDF a notation
    /// program drew is read on the iPad whole and nothing is sent.
    func testSCAN03BeforeSendingTheLineSaysThePagesReadOnTheIPadThePagesSentAndAboutHowLong() async throws {
        StubAPI.reset(["http://scan.test/health": ScanBaseline.health])
        let asked = await ScanServer.health(ScanBaseline.server, session: StubAPI.session())
        guard case .success(let health) = asked else { return XCTFail("health: \(asked)") }
        XCTAssertTrue(health.takesJobs)

        let engraved = try Data(contentsOf: ScanBaseline.promenade)
        let drawn = await ScanJobPlan.of(engraved, pages: 2)
        XCTAssertEqual(drawn.device.keys.sorted(), [1, 2], "the Promenade, engraved by LilyPond: both pages read on the iPad")
        XCTAssertEqual(drawn.server, [])
        XCTAssertEqual(drawn.line(russian, minutes: health.minutes(pages: drawn.server.count)), "2 страницы прочитаны на iPad.")
        let scanned = await ScanJobPlan.of(ScanBaseline.scannedPdf(pages: 2), pages: 2)
        XCTAssertEqual(scanned.server, [1, 2], "pictures of pages go to the server")
        XCTAssertEqual(scanned.line(russian, minutes: health.minutes(pages: 2)), "2 страницы отправим на распознавание, около 3 минут.")
        let both = ScanJobPlan(device: [1: "", 2: "", 3: "", 5: ""], server: [4, 6])
        XCTAssertEqual(both.line(russian, minutes: health.minutes(pages: 2)), "4 страницы прочитаны на iPad. 2 страницы отправим на распознавание, около 3 минут.")
        XCTAssertEqual(both.line(english, minutes: 1), "4 pages read on this iPad. 2 pages go to be recognised, about 1 minute.")
        let busy = ScanHealth(engine: health.engine, secondsPerPage: health.secondsPerPage, queue: 8, workers: 4)
        XCTAssertEqual(busy.minutes(pages: 2), 6, "the units already queued there count")

        let target = ScanTarget(address: ScanBaseline.server, account: Account(options: nil), session: StubAPI.session())
        let outcome = await ScanServer.readByJob(fileName: "Promenade.pdf", data: engraved, pages: 2, to: target)
        guard case .success(let read)? = outcome else { return XCTFail("read: \(String(describing: outcome))") }
        XCTAssertNil(read.job, "no job for a PDF read whole on the iPad")
        XCTAssertEqual(sent("POST", "jobs"), 0)
        XCTAssertTrue(read.result.engine.hasPrefix("vector"), read.result.engine)
    }

    // MARK: - SCAN-04: the progress in the library, the job over a relaunch

    /// SCAN-04: a piece read by a job from the library keeps its place there, its line showing the job's progress, the pages
    /// every engine is done with and the server's minutes left, and on a tap each page with its engines; the job's id is kept
    /// on this iPad as soon as the server gives it, so after a relaunch the app asks for the same job at once, without
    /// «Продолжить» and without sending the PDF again, writes the notes and deletes the job.
    func testSCAN04APieceReadByAJobShowsItsProgressInTheLibraryAndGoesOnAfterARelaunch() async throws {
        jobsBefore = await ScanBaseline.jobsOn()
        let url = try piece(pages: 2)
        let folder = url.deletingLastPathComponent()
        StubAPI.reset([
            "http://scan.test/health": ScanBaseline.health, "POST http://scan.test/jobs": ScanBaseline.answer(202, #"{"id": "j1", "status": "/jobs/j1"}"#),
            "http://scan.test/jobs/j1": status("j1", read: [1], reading: [2]),
        ])
        let queue = self.queue(in: folder)
        queue.start([RecognitionItem(file: url.lastPathComponent, title: "Etude", pages: 2)], address: ScanBaseline.server, hosted: false)

        try await ScanBaseline.wait("the job's id kept and its state heard") { RecognitionStore.load(in: folder)?.items.first?.job != nil && queue.job != nil }
        XCTAssertEqual(RecognitionStore.load(in: folder)?.items.first?.job, ScanJobRecord(id: "j1", pages: [1, 2]), "on the device, as soon as the server gives it")
        XCTAssertEqual(pagesSent(), "1,2")
        XCTAssertEqual(queue.batch?.items.first?.state, .reading, "the piece stays in the library, being read")
        let heard = try XCTUnwrap(queue.job)
        XCTAssertEqual(heard.file, url.lastPathComponent, "its own line shows the job")
        XCTAssertEqual(RecognitionTop.jobLine(heard.status.progress, russian), "распознаётся · 1 из 2 страниц · ещё около 2 мин.")
        XCTAssertEqual(heard.status.pages.map { ScanJobDetail.engines($0, russian) }, ["homr: прочитал · copisteria: прочитал", "homr: читает · copisteria: ждёт"])

        // The app is closed while the job runs: the batch stays on the device as it was then.
        queue.pause()
        try await ScanBaseline.wait("the paused batch stops asking") { !queue.running }
        var left = try XCTUnwrap(RecognitionStore.load(in: folder))
        left.paused = false
        left.items[0].state = .reading
        RecognitionStore.save(left, in: folder)
        StubAPI.reset([
            "http://scan.test/jobs/j1": status("j1", read: [1, 2]), "http://scan.test/jobs/j1/result": result("j1", [(1, "C"), (2, "E")]),
            "DELETE http://scan.test/jobs/j1": ScanBaseline.answer(204, ""),
        ])

        let relaunched = self.queue(in: folder)
        relaunched.restoreAndFollowJobs()

        try await ScanBaseline.wait("the same job asked again at once and its notes written") { relaunched.batch?.finished == true && !relaunched.running }
        let done = try XCTUnwrap(relaunched.batch?.items.first)
        XCTAssertEqual(done.state, .done)
        XCTAssertNil(done.job, "gone from the device")
        XCTAssertEqual(sent("POST", "jobs"), 0, "the PDF is not sent again")
        XCTAssertEqual(sent("GET", "jobs/j1"), 1)
        XCTAssertEqual(sent("DELETE", "jobs/j1"), 1, "the job goes from the server once the notes are saved")
        let summary = await PieceSummary.read(url)
        XCTAssertEqual(summary?.hasNotes, true, "the piece opens on its notes")
        XCTAssertEqual(try ScanBaseline.pitches(url, bar: 2), [64])
    }

    // MARK: - SCAN-05: a page no engine read, «Повторить»

    /// SCAN-05: a page no engine read is said on the piece's line («есть ноты · стр. 2 не распознана») with «Повторить»; the
    /// rest opens. «Повторить» sends only that page, as a new job, and puts it into the same notes at its place, the player's
    /// corrections kept; the line then has no «Повторить».
    func testSCAN05APageNoEngineReadIsSaidOnItsLineAndRetryPutsItIntoTheSameNotes() async throws {
        jobsBefore = await ScanBaseline.jobsOn()
        let url = try piece(pages: 2)
        let queue = try await readFirst(url)

        let first = try XCTUnwrap(queue.batch?.items.first)
        XCTAssertEqual(first.state, .done, "the rest opens")
        XCTAssertEqual(first.unread, [2])
        XCTAssertEqual(russian["recognition.state.done"] + " · " + RecognitionTop.unreadLine(first.unread ?? [], russian), "есть ноты · стр. 2 не распознана")
        XCTAssertEqual(russian["recognition.retry"], "Повторить")
        XCTAssertEqual(russian["recognition.retryPagesHint"], "Отправит снова только эти страницы и вставит их в эти же ноты")
        XCTAssertEqual(try ScanBaseline.pitches(url, bar: 1), [60], "page 1's bar is the piece's")
        XCTAssertEqual(try ScanBaseline.pitches(url, bar: 2), [], "page 2 is not in the notes")
        try ScanBaseline.change(url, "corrections.change", #"{"bar": 1, "id": "n_0001", "pitch": 1}"#)

        StubAPI.reset(newJob("j2", [(2, "E")]))
        queue.retryUnread(url.lastPathComponent)

        try await ScanBaseline.wait("page 2 read again and put in") { queue.batch?.finished == true && !queue.running }
        let after = try XCTUnwrap(queue.batch?.items.first)
        XCTAssertEqual(after.state, .done)
        XCTAssertNil(after.unread, "every page is in the notes: no «Повторить»")
        XCTAssertNil(after.reason)
        XCTAssertEqual(pagesSent(), "2", "only the page no engine read")
        XCTAssertEqual(try ScanBaseline.pitches(url, bar: 1), [61], "the player's correction stays")
        XCTAssertEqual(try ScanBaseline.pitches(url, bar: 2), [64], "page 2's bar goes in after page 1's, in the same notes")
        XCTAssertEqual(sent("DELETE", "jobs/j2"), 1)
    }

    /// SCAN-05: a page read again that music-core does not put in (here a bookmark stands on the notes, which would move)
    /// leaves the notes as they were and the line with «Повторить» and the reason, «Не вставлено: …».
    func testSCAN05APageReadAgainThatCannotGoInLeavesTheNotesAndSaysWhyBesideRetry() async throws {
        jobsBefore = await ScanBaseline.jobsOn()
        let url = try piece(pages: 2)
        let queue = try await readFirst(url)
        try ScanBaseline.change(url, "bookmark.add", #"{"bars": {"first": 1, "last": 1}, "title": "Тема", "category": "teacher", "now": "2026-11-24T19:05:00Z"}"#)

        StubAPI.reset(newJob("j3", [(2, "E")]))
        queue.retryUnread(url.lastPathComponent)

        try await ScanBaseline.wait("page 2 read again and refused") { queue.batch?.finished == true && !queue.running }
        let after = try XCTUnwrap(queue.batch?.items.first)
        XCTAssertEqual(after.state, .done, "the piece keeps its notes, not «не вышло»")
        XCTAssertEqual(after.unread, [2], "the line keeps «Повторить»")
        let why = try XCTUnwrap(after.reason, "and says why")
        XCTAssertTrue(why.hasPrefix("Страницу можно вставить в ноты из этого PDF, только пока на нотах нет ничего, кроме правок") || why.hasPrefix("The page can be put into notes"), why)
        XCTAssertEqual(russian.format("recognition.againWhy", why), "Не вставлено: " + why)
        XCTAssertEqual(try ScanBaseline.pitches(url, bar: 1), [60])
        XCTAssertEqual(try ScanBaseline.pitches(url, bar: 2), [], "the notes as they were")
    }
}

/// What the scan baselines share: the switch turned on and back, a PDF piece and pictures of pages, the scan server's
/// answers, the notes of a piece's file, and a bounded wait.
@MainActor
enum ScanBaseline {
    static let server = URL(string: "http://scan.test/")!

    /// The Mutopia Promenade (public domain, LilyPond) of the vector reader's fixtures, where it lies in the repository.
    static var promenade: URL {
        URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("backend/Unit/MotiffLab.MusicCore.Tests/Import/Vector/Fixtures/promenade-a4.pdf")
    }

    /// Turns «Распознавание в фоне» on in this iPad's settings, as its switch does; answers what it was, for `restore`.
    static func jobsOn() async -> String {
        let before = ScanServer.jobs ? "on" : "off"
        await DeviceSettings.shared.update(["set": ["id": ScanServer.jobsKey, "value": "on"]])
        XCTAssertTrue(ScanServer.jobs, "the experiment on")
        return before
    }

    static func restore(_ before: String?) async {
        guard let before else { return }
        await DeviceSettings.shared.update(["set": ["id": ScanServer.jobsKey, "value": before]])
    }

    static func answer(_ status: Int, _ body: String) -> StubAPI.Answer {
        StubAPI.Answer(status: status, body: Data(body.utf8))
    }

    static func json(_ object: [String: Any]) -> StubAPI.Answer {
        StubAPI.Answer(status: 200, body: (try? JSONSerialization.data(withJSONObject: object)) ?? Data())
    }

    /// `/health` of a server of 1.3: four engines timed there, nothing queued, four at once.
    static var health: StubAPI.Answer {
        let engines: [[String: Any]] = [("homr", 75), ("copisteria", 90), ("audiveris", 70), ("transcoda", 125)].map { ["engine": $0.0, "secondsPerPage": $0.1] }
        return json(["engine": "homr", "version": "0.7.0", "engines": engines, "jobs": ["queue": 0, "workers": 4]])
    }

    /// A page of one bar, a whole note on `step` 4 in 4/4, as an engine writes it.
    static func bar(of step: String) -> String {
        let attributes = "<attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time><clef><sign>G</sign><line>2</line></clef></attributes>"
        let note = "<note><pitch><step>\(step)</step><octave>4</octave></pitch><duration>4</duration><type>whole</type></note>"
        return #"<?xml version="1.0" encoding="UTF-8"?><score-partwise version="3.1"><part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>"#
            + #"<part id="P1"><measure number="1">"# + attributes + note + "</measure></part></score-partwise>"
    }

    /// A PDF of `pages` pictures of pages, 1000 × 400 points each: a photo of the page (no text, no lines), white with a
    /// black block where `dark` is.
    static func scannedPdf(pages: Int, dark: CGRect = CGRect(x: 100, y: 80, width: 100, height: 140)) -> Data {
        let size = CGSize(width: 1000, height: 400)
        let format = UIGraphicsImageRendererFormat()
        format.scale = 1
        let photo = UIGraphicsImageRenderer(size: size, format: format).image { context in
            UIColor.white.setFill()
            context.fill(CGRect(origin: .zero, size: size))
            UIColor.black.setFill()
            context.fill(dark)
        }
        return UIGraphicsPDFRenderer(bounds: CGRect(origin: .zero, size: size)).pdfData { context in
            for _ in 0..<pages {
                context.beginPage()
                photo.draw(in: CGRect(origin: .zero, size: size))
            }
        }
    }

    /// A PDF piece of `pdf`, `pages` pages, with no notes yet, saved as the library keeps it in a new folder of its own.
    static func pdfPiece(pdf: Data, pages: Int) throws -> URL {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("scan-baseline-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let request = #"{"id": "p_etude", "photos": \#(pages), "now": "2026-11-24T19:00:00Z"}"#
        let opened = try XCTUnwrap(facadeObject(MusicCore.open(fileName: "Etude.pdf", data: pdf, request: request)))
        let piece = try XCTUnwrap((opened["piece"] as? NSNumber)?.int32Value, "\(opened)")
        defer { MusicCore.close(piece: piece) }
        let url = folder.appendingPathComponent("Etude.motiff.json")
        try Data(MusicCore.call(piece: piece, "project.save", "{}").utf8).write(to: url)
        return url
    }

    /// The piece of the file at `url` opened, `call` made on it and the file saved, as the score's screen saves a change.
    static func change(_ url: URL, _ call: String, _ request: String) throws {
        let opened = try XCTUnwrap(facadeObject(MusicCore.open(fileName: url.lastPathComponent, data: try Data(contentsOf: url), request: #"{"language": "ru"}"#)))
        let piece = try XCTUnwrap((opened["piece"] as? NSNumber)?.int32Value)
        defer { MusicCore.close(piece: piece) }
        let answer = try XCTUnwrap(facadeObject(MusicCore.call(piece: piece, call, request)))
        XCTAssertNil(answer["error"], "\(call): \(answer)")
        try Data(MusicCore.call(piece: piece, "project.save", "{}").utf8).write(to: url, options: .atomic)
    }

    /// The pitches the piece of the file at `url` plays in `bar`, its corrections on; none for a bar it does not have.
    static func pitches(_ url: URL, bar: Int) throws -> [Int] {
        let opened = try XCTUnwrap(facadeObject(MusicCore.open(fileName: url.lastPathComponent, data: try Data(contentsOf: url), request: #"{"language": "ru"}"#)))
        let piece = try XCTUnwrap((opened["piece"] as? NSNumber)?.int32Value)
        defer { MusicCore.close(piece: piece) }
        let answer = facadeObject(MusicCore.call(piece: piece, "corrections", #"{"bar": \#(bar)}"#))
        return (answer?["notes"] as? [[String: Any]] ?? []).compactMap { $0["pitch"] as? Int }.sorted()
    }

    /// Waits, `seconds` at most, for what the queue or the controller does in a task of its own.
    static func wait(_ what: String, seconds: Int = 15, until done: () -> Bool) async throws {
        for _ in 0..<(seconds * 20) where !done() { try await Task.sleep(for: .milliseconds(50)) }
        XCTAssertTrue(done(), what)
    }
}
