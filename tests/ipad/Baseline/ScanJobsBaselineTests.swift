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
        await ScanSupport.restore(jobsBefore)
        jobsBefore = nil
        for folder in folders { try? FileManager.default.removeItem(at: folder) }
        folders = []
        StubAPI.reset()
    }

    /// A PDF piece of `pages` pictures of pages, with no notes yet, in a folder of its own.
    private func piece(pages: Int) throws -> URL {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("scan-baseline-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        folders.append(folder)
        return try ScanSupport.pdfPiece(in: folder, pdf: ScanSupport.scannedPdf(pages: pages), pages: pages)
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
        return ScanSupport.json(["id": id, "state": state, "etaSeconds": reading.isEmpty ? 0 : 61, "pages": done + going])
    }

    /// The answers of a server of 1.3 to a new job `id` of the pages `pages`, done at once, and to its DELETE.
    private func newJob(_ id: String, _ pages: [(page: Int, step: String?)]) -> [String: StubAPI.Answer] {
        [
            "http://scan.test/health": ScanSupport.health, "POST http://scan.test/jobs": ScanSupport.answer(202, #"{"id": "\#(id)"}"#),
            "http://scan.test/jobs/\(id)": status(id, read: pages.map { $0.page }), "http://scan.test/jobs/\(id)/result": ScanSupport.result(id, pages),
            "DELETE http://scan.test/jobs/\(id)": ScanSupport.answer(204, ""),
        ]
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
        queue.start([RecognitionItem(file: url.lastPathComponent, title: "Etude", pages: 2)], address: ScanSupport.server, hosted: false)
        try await ScanSupport.wait("the first job read and its notes written") { queue.batch?.finished == true && !queue.running }
        return queue
    }

    // MARK: - SCAN-02: the engine's choice

    /// SCAN-02: Settings · «Движок» offers «Четыре движка», «Авто», homr and copisteria, a setting of this iPad, «Четыре
    /// движка» until another is chosen; its hint says what each does, that recognition goes on after a relaunch and what an
    /// older server does. There is no experiment switch. Where a PDF goes says, with four engines, that the server keeps it
    /// until the iPad has fetched what was read, at most 24 hours. A server before 1.3 takes no jobs.
    func testSCAN02FourEnginesAreTheEngineChoiceOfThisIPadByDefaultAndItsTextsSayHowLongTheServerKeepsThePdf() async throws {
        let settings = DeviceSettings(store: try XCTUnwrap(UserDefaults(suiteName: "ScanJobsBaseline-\(UUID().uuidString)")))
        await settings.load()
        XCTAssertEqual(settings.catalogue[ScanServer.engineChoice]?.scope, "device", "a setting of this iPad")
        XCTAssertEqual(settings.catalogue[ScanServer.engineChoice]?.choices, ["all", "auto", "homr", "copisteria"])
        XCTAssertTrue(ScanServer.jobsOn(settings), "«Четыре движка» on a new iPad (Andrei, 2026-11-25)")
        XCTAssertNil(settings.catalogue["experimental.scanJobs"], "no experiment switch (Andrei, 2026-11-25)")
        XCTAssertEqual(russian["settings.scanEngineAll"], "Четыре движка")
        let hint = russian["settings.scanEngineHint"]
        XCTAssertTrue(hint.contains("продолжается после закрытия и нового открытия приложения"))
        XCTAssertTrue(hint.contains("со старым всё идёт как при «Авто»"))
        XCTAssertNotEqual(english["settings.scanEngineAll"], "settings.scanEngineAll")

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
        StubAPI.reset(["http://scan.test/health": ScanSupport.health])
        let asked = await ScanServer.health(ScanSupport.server, session: StubAPI.session())
        guard case .success(let health) = asked else { return XCTFail("health: \(asked)") }
        XCTAssertTrue(health.takesJobs)

        let engraved = try Data(contentsOf: ScanSupport.promenade)
        let drawn = await ScanJobPlan.of(engraved, pages: 2)
        XCTAssertEqual(drawn.device.keys.sorted(), [1, 2], "the Promenade, engraved by LilyPond: both pages read on the iPad")
        XCTAssertEqual(drawn.server, [])
        XCTAssertEqual(drawn.line(russian, minutes: health.minutes(pages: drawn.server.count)), "2 страницы прочитаны на iPad.")
        let scanned = await ScanJobPlan.of(ScanSupport.scannedPdf(pages: 2), pages: 2)
        XCTAssertEqual(scanned.server, [1, 2], "pictures of pages go to the server")
        XCTAssertEqual(scanned.line(russian, minutes: health.minutes(pages: 2)), "2 страницы отправим на распознавание, около 3 минут.")
        let both = ScanJobPlan(device: [1: "", 2: "", 3: "", 5: ""], server: [4, 6])
        XCTAssertEqual(both.line(russian, minutes: health.minutes(pages: 2)), "4 страницы прочитаны на iPad. 2 страницы отправим на распознавание, около 3 минут.")
        XCTAssertEqual(both.line(english, minutes: 1), "4 pages read on this iPad. 2 pages go to be recognised, about 1 minute.")
        let busy = ScanHealth(engine: health.engine, secondsPerPage: health.secondsPerPage, queue: 8, workers: 4)
        XCTAssertEqual(busy.minutes(pages: 2), 6, "the units already queued there count")

        let target = ScanTarget(address: ScanSupport.server, account: Account(options: nil), session: StubAPI.session())
        let outcome = await ScanServer.readByJob(fileName: "Promenade.pdf", data: engraved, pages: 2, to: target)
        guard case .success(let read)? = outcome else { return XCTFail("read: \(String(describing: outcome))") }
        XCTAssertNil(read.job, "no job for a PDF read whole on the iPad")
        XCTAssertEqual(ScanSupport.sent("POST", "jobs"), 0)
        XCTAssertTrue(read.result.engine.hasPrefix("vector"), read.result.engine)
    }

    // MARK: - SCAN-04: the progress in the library, the job over a relaunch

    /// SCAN-04: a piece read by a job from the library keeps its place there, its line showing the job's progress, the pages
    /// every engine is done with and the server's minutes left, and on a tap each page with its engines; the job's id is kept
    /// on this iPad as soon as the server gives it, so after a relaunch the app asks for the same job at once, without
    /// «Продолжить» and without sending the PDF again, writes the notes and deletes the job.
    func testSCAN04APieceReadByAJobShowsItsProgressInTheLibraryAndGoesOnAfterARelaunch() async throws {
        jobsBefore = await ScanSupport.jobsOn()
        let url = try piece(pages: 2)
        let folder = url.deletingLastPathComponent()
        StubAPI.reset([
            "http://scan.test/health": ScanSupport.health, "POST http://scan.test/jobs": ScanSupport.answer(202, #"{"id": "j1", "status": "/jobs/j1"}"#),
            "http://scan.test/jobs/j1": status("j1", read: [1], reading: [2]),
        ])
        let queue = self.queue(in: folder)
        queue.start([RecognitionItem(file: url.lastPathComponent, title: "Etude", pages: 2)], address: ScanSupport.server, hosted: false)

        try await ScanSupport.wait("the job's id kept and its state heard") { RecognitionStore.load(in: folder)?.items.first?.job != nil && queue.job != nil }
        XCTAssertEqual(RecognitionStore.load(in: folder)?.items.first?.job, ScanJobRecord(id: "j1", pages: [1, 2]), "on the device, as soon as the server gives it")
        XCTAssertEqual(pagesSent(), "1,2")
        XCTAssertEqual(queue.batch?.items.first?.state, .reading, "the piece stays in the library, being read")
        let heard = try XCTUnwrap(queue.job)
        XCTAssertEqual(heard.file, url.lastPathComponent, "its own line shows the job")
        XCTAssertEqual(RecognitionTop.jobLine(heard.status.progress, russian), "распознаётся · страница 2 из 2 · около 1 мин 10 с")
        XCTAssertEqual(heard.status.pages.map { ScanJobDetail.engines($0, russian) }, ["homr: прочитал · copisteria: прочитал", "homr: читает · copisteria: ждёт"])

        // The app is closed while the job runs: the batch stays on the device as it was then.
        queue.pause()
        try await ScanSupport.wait("the paused batch stops asking") { !queue.running }
        var left = try XCTUnwrap(RecognitionStore.load(in: folder))
        left.paused = false
        left.items[0].state = .reading
        RecognitionStore.save(left, in: folder)
        StubAPI.reset([
            "http://scan.test/jobs/j1": status("j1", read: [1, 2]), "http://scan.test/jobs/j1/result": ScanSupport.result("j1", [(1, "C"), (2, "E")]),
            "DELETE http://scan.test/jobs/j1": ScanSupport.answer(204, ""),
        ])

        let relaunched = self.queue(in: folder)
        relaunched.restoreAndFollowJobs()

        try await ScanSupport.wait("the same job asked again at once and its notes written") { relaunched.batch?.finished == true && !relaunched.running }
        let done = try XCTUnwrap(relaunched.batch?.items.first)
        XCTAssertEqual(done.state, .done)
        XCTAssertNil(done.job, "gone from the device")
        XCTAssertEqual(ScanSupport.sent("POST", "jobs"), 0, "the PDF is not sent again")
        XCTAssertEqual(ScanSupport.sent("GET", "jobs/j1"), 1)
        XCTAssertEqual(ScanSupport.sent("DELETE", "jobs/j1"), 1, "the job goes from the server once the notes are saved")
        let summary = await PieceSummary.read(url)
        XCTAssertEqual(summary?.hasNotes, true, "the piece opens on its notes")
        XCTAssertEqual(try ScanSupport.pitches(url, bar: 2), [64])
    }

    // MARK: - SCAN-04: one recognition at a time, the open PDF in the background

    /// SCAN-04 (Andrei, 2026-11-25): with «Четыре движка», «Распознать ноты» on the open PDF goes to the library's
    /// recognition; while one goes on none starts, and the banner and the PDF's line say whose it is and how far.
    func testSCAN04WithFourEnginesThePieceOpenOnScreenIsSentAfterTheRestAndOnlyItsReadingsWait() {
        var batch = RecognitionBatch(address: "https://scan.example.org/", hosted: false, items: [
            RecognitionItem(file: "a.motiff.json", title: "Arabesque", pages: 5), RecognitionItem(file: "b.motiff.json", title: "Ballade", pages: 12),
        ])

        XCTAssertEqual(RecognitionQueue.next(in: batch, open: "a.motiff.json", jobs: true), 1, "the rest of a batch first: not held behind the open piece")
        batch.items[1].state = .done
        XCTAssertEqual(RecognitionQueue.next(in: batch, open: "a.motiff.json", jobs: true), 0, "«Распознать ноты» on the open PDF goes on in the background (Andrei, 2026-11-25)")
        XCTAssertNil(RecognitionQueue.next(in: batch, open: "a.motiff.json"), "with one engine the open piece waits for its screen to close")
        batch.items[0].job = ScanJobRecord(id: "j1", pages: [1])
        XCTAssertEqual(RecognitionQueue.next(in: batch, open: "a.motiff.json"), 0, "a job kept: followed while the piece is open, its readings wait for the screen")
    }

    func testSCAN04OneRecognitionAtATimeTheBannerNamesItsPieceAndStoppingEndsIt() throws {
        let queue = self.queue(in: try piece(pages: 1).deletingLastPathComponent())
        XCTAssertNil(queue.busy)

        queue.start([RecognitionItem(file: "a.motiff.json", title: "Arabesque", pages: 5)], address: URL(string: "https://scan.example.org/")!, hosted: false)

        XCTAssertEqual(queue.busy?.title, "Arabesque", "while it goes on, none starts (Andrei, 2026-11-25)")
        queue.pause()
        XCTAssertEqual(queue.busy?.title, "Arabesque", "paused, it still goes on")
        XCTAssertEqual(russian.format("recognition.busy", "Arabesque"), "Идёт распознавание «Arabesque». Новое начнётся, когда оно закончится или вы его остановите в библиотеке.")
        XCTAssertEqual(russian.format("recognition.runningOne", "Arabesque"), "Распознаю ноты «Arabesque»")
        queue.stop()
        XCTAssertNil(queue.busy, "stopped: a new one may start")
    }

    func testSCAN04ThePdfsLineSaysHowFarAndWhenTheNotesAreReadyInBothLanguages() {
        let queue = RecognitionQueue()
        var item = RecognitionItem(file: "a.motiff.json", title: "Arabesque", pages: 2)
        XCTAssertEqual(BackgroundRecognitionLine.words(item, queue: queue, text: russian), russian["import.scanBusy"], "sent, nothing told yet")
        XCTAssertFalse(BackgroundRecognitionLine.ready(item, queue: queue))
        item.state = .failed
        item.reason = "Сервер не ответил."
        XCTAssertEqual(BackgroundRecognitionLine.words(item, queue: queue, text: russian), "Ноты не распознаны: Сервер не ответил.")
        item.state = .done
        XCTAssertNil(BackgroundRecognitionLine.words(item, queue: queue, text: russian), "the notes are in: no line")
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            for key in ["recognition.busy", "recognition.background", "recognition.notesReady", "recognition.openNotes", "recognition.backgroundFailed", "recognition.runningOne"] {
                XCTAssertNotEqual(text[key], key, "\(language): \(key)")
            }
        }
    }

    /// SCAN-04: the piece open on its PDF gets its notes only once its screen lets it go («Открыть ноты»): meanwhile the
    /// notes wait, its line says «Ноты готовы» and nothing is written into the file, which the screen saves.
    private func readWhileOpen(_ url: URL, _ queue: RecognitionQueue) async throws {
        queue.opened(url)
        queue.start([RecognitionItem(file: url.lastPathComponent, title: "Etude", pages: 2)], address: ScanSupport.server, hosted: false)

        try await ScanSupport.wait("the notes read and waiting for the screen") { queue.awaiting == url.lastPathComponent }
        let waiting = try XCTUnwrap(queue.status(of: url))
        XCTAssertTrue(BackgroundRecognitionLine.ready(waiting, queue: queue), "«Ноты готовы · Открыть ноты»")
        let before = await PieceSummary.read(url)
        XCTAssertEqual(before?.hasNotes, false, "nothing written while the piece is open")

        queue.released(url)
        let done = await queue.finished(url, within: 15)
        XCTAssertEqual(done?.state, .done)
        XCTAssertNil(queue.awaiting)
        let after = await PieceSummary.read(url)
        XCTAssertEqual(after?.hasNotes, true, "the notes written once the screen let the piece go")
    }

    func testSCAN04ThePieceOpenOnItsPdfGetsTheNotesOfItsJobOnlyOnceItsScreenLetsItGo() async throws {
        jobsBefore = await ScanSupport.jobsOn()
        let url = try piece(pages: 2)
        StubAPI.reset(newJob("j1", [(1, "C"), (2, "E")]))

        try await readWhileOpen(url, self.queue(in: url.deletingLastPathComponent()))

        XCTAssertEqual(try ScanSupport.pitches(url, bar: 2), [64])
        XCTAssertEqual(ScanSupport.sent("POST", "jobs"), 1, "sent once, not again after the wait")
    }

    func testSCAN04APdfReadWholeOnTheIPadWaitsForItsScreenTooAndIsNotReadAgain() async throws {
        jobsBefore = await ScanSupport.jobsOn()
        let folder = try piece(pages: 2).deletingLastPathComponent()
        let url = try ScanSupport.pdfPiece(in: folder, pdf: Data(contentsOf: ScanSupport.promenade), pages: 2)
        StubAPI.reset(["http://scan.test/health": ScanSupport.health])

        try await readWhileOpen(url, self.queue(in: folder))

        XCTAssertEqual(ScanSupport.sent("POST", "jobs"), 0, "nothing sent: the Promenade is read on the iPad")
    }

    // MARK: - SCAN-05: a page no engine read, «Повторить»

    /// SCAN-05: a page no engine read is said on the piece's line («есть ноты · стр. 2 не распознана») with «Повторить»; the
    /// rest opens. «Повторить» sends only that page, as a new job, and puts it into the same notes at its place, the player's
    /// corrections kept; the line then has no «Повторить».
    func testSCAN05APageNoEngineReadIsSaidOnItsLineAndRetryPutsItIntoTheSameNotes() async throws {
        jobsBefore = await ScanSupport.jobsOn()
        let url = try piece(pages: 2)
        let queue = try await readFirst(url)

        let first = try XCTUnwrap(queue.batch?.items.first)
        XCTAssertEqual(first.state, .done, "the rest opens")
        XCTAssertEqual(first.unread, [2])
        XCTAssertEqual(russian["recognition.state.done"] + " · " + RecognitionTop.unreadLine(first.unread ?? [], russian), "есть ноты · стр. 2 не распознана")
        XCTAssertEqual(russian["recognition.retry"], "Повторить")
        XCTAssertEqual(russian["recognition.retryPagesHint"], "Отправит снова только эти страницы и вставит их в эти же ноты")
        XCTAssertEqual(try ScanSupport.pitches(url, bar: 1), [60], "page 1's bar is the piece's")
        XCTAssertEqual(try ScanSupport.pitches(url, bar: 2), [], "page 2 is not in the notes")
        try ScanSupport.change(url, "corrections.change", #"{"bar": 1, "id": "n_0001", "pitch": 1}"#)

        StubAPI.reset(newJob("j2", [(2, "E")]))
        queue.retryUnread(url.lastPathComponent)

        try await ScanSupport.wait("page 2 read again and put in") { queue.batch?.finished == true && !queue.running }
        let after = try XCTUnwrap(queue.batch?.items.first)
        XCTAssertEqual(after.state, .done)
        XCTAssertNil(after.unread, "every page is in the notes: no «Повторить»")
        XCTAssertNil(after.reason)
        XCTAssertEqual(pagesSent(), "2", "only the page no engine read")
        XCTAssertEqual(try ScanSupport.pitches(url, bar: 1), [61], "the player's correction stays")
        XCTAssertEqual(try ScanSupport.pitches(url, bar: 2), [64], "page 2's bar goes in after page 1's, in the same notes")
        XCTAssertEqual(ScanSupport.sent("DELETE", "jobs/j2"), 1)
    }

    /// SCAN-05: a page read again that music-core does not put in (here a margin note stands on the notes, which would move;
    /// a bookmark moves on with the page since v1.5 i-7)
    /// leaves the notes as they were and the line with «Повторить» and the reason, «Не вставлено: …».
    func testSCAN05APageReadAgainThatCannotGoInLeavesTheNotesAndSaysWhyBesideRetry() async throws {
        jobsBefore = await ScanSupport.jobsOn()
        let url = try piece(pages: 2)
        let queue = try await readFirst(url)
        try ScanSupport.change(url, "note.add", #"{"bars": {"first": 1, "last": 1}, "text": "Тема", "now": "2026-11-24T19:05:00Z"}"#)

        StubAPI.reset(newJob("j3", [(2, "E")]))
        queue.retryUnread(url.lastPathComponent)

        try await ScanSupport.wait("page 2 read again and refused") { queue.batch?.finished == true && !queue.running }
        let after = try XCTUnwrap(queue.batch?.items.first)
        XCTAssertEqual(after.state, .done, "the piece keeps its notes, not «не вышло»")
        XCTAssertEqual(after.unread, [2], "the line keeps «Повторить»")
        let why = try XCTUnwrap(after.reason, "and says why")
        XCTAssertTrue(why.hasPrefix("Страницу можно вставить в ноты из этого PDF, только пока на нотах нет ничего, кроме правок") || why.hasPrefix("The page can be put into notes"), why)
        XCTAssertEqual(russian.format("recognition.againWhy", why), "Не вставлено: " + why)
        XCTAssertEqual(try ScanSupport.pitches(url, bar: 1), [60])
        XCTAssertEqual(try ScanSupport.pitches(url, bar: 2), [], "the notes as they were")
    }
}
