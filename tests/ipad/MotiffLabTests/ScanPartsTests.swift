import PDFKit
import XCTest
@testable import MotiffLab

/// A score sent a page a request (issue #479; v0.5.6, #533 section 1): planned in consecutive parts, each part a PDF of its
/// own pages, the notes of the parts joined by music-core with each page of the PDF where it starts, scores of more than
/// 100 pages refused, and the progress told only by the pages' answers, in both languages.
final class ScanPartsTests: XCTestCase {
    private let english = UIText(language: "en")
    private let russian = UIText(language: "ru")

    /// A PDF of blank pages, each a little wider than the one before, so a copied page is known by its width.
    private func document(_ pages: Int) -> PDFDocument {
        let document = PDFDocument()
        for number in 1...pages {
            let page = PDFPage()
            page.setBounds(CGRect(x: 0, y: 0, width: 500 + number, height: 842), for: .mediaBox)
            document.insert(page, at: document.pageCount)
        }
        return document
    }

    func testAPdfLongerThanTheServerReadsGoesInConsecutiveFullParts() {
        XCTAssertEqual(ScanParts.plan(pages: 6, maxPages: 4), [1...4, 5...6])
        XCTAssertEqual(ScanParts.plan(pages: 9, maxPages: 4), [1...4, 5...8, 9...9])
        XCTAssertEqual(ScanParts.plan(pages: 100, maxPages: 4).count, 25)
        XCTAssertEqual(ScanParts.plan(pages: 6, maxPages: 8), [1...6], "a PDF that fits goes whole")
        XCTAssertEqual(ScanParts.plan(pages: 5, maxPages: 0), [1...4, 5...5], "a server that does not say reads the safe number")
        XCTAssertEqual(ScanParts.mostPages, 100)
    }

    func testAPartIsAPdfOfThosePagesInOrder() throws {
        let data = try XCTUnwrap(ScanParts.part(of: document(6), 5...6))
        let part = try XCTUnwrap(PDFDocument(data: data))

        XCTAssertEqual(part.pageCount, 2)
        XCTAssertEqual(part.page(at: 0)?.bounds(for: .mediaBox).width, 505)
        XCTAssertEqual(part.page(at: 1)?.bounds(for: .mediaBox).width, 506)
        XCTAssertNil(ScanParts.part(of: document(3), 3...4), "a page past the end")
    }

    @MainActor
    func testAScoreOfMoreThanAHundredPagesIsRefusedBeforeAnythingIsSent() async throws {
        let data = try XCTUnwrap(document(101).dataRepresentation())
        let server = try XCTUnwrap(ScanServer.parse("http://127.0.0.1:9"))

        let answer = await ScanServer.recognise(fileName: "Long.pdf", data: data, address: server, account: Account(options: nil))

        guard case .failure(.tooManyPages(101)) = answer else { return XCTFail("refused before sending, with its pages") }
        XCTAssertEqual(ScanProblem.tooManyPages(101).text(english), "This PDF has 101 pages. Recognition reads scores of up to 100 pages: split it into pieces of up to 100 pages.")
        XCTAssertTrue(ScanProblem.tooManyPages(101).text(russian).hasPrefix("В этом PDF 101 стр."))
    }

    /// v1.5 r-2b: a server that does not answer, answers late or is not a scan server is said in the interface's language
    /// (these were in English in a Russian interface).
    func testAServerThatDoesNotAnswerIsSaidInBothLanguages() throws {
        let server = try XCTUnwrap(ScanServer.parse("http://192.168.1.20:8000"))
        XCTAssertEqual(ScanProblem.unreachable(server).text(russian), "Сервер распознавания по адресу http://192.168.1.20:8000/ не отвечает. Проверьте, что он запущен (см. Настройки) и что этот iPad может до него дотянуться.")
        XCTAssertEqual(ScanProblem.unreachable(server).text(english), "The scan server at http://192.168.1.20:8000/ does not answer. Check that it is running (see Settings), and that this iPad may reach it.")
        XCTAssertEqual(ScanProblem.notAScanServer.text(russian), "По этому адресу что-то отвечает, но это не сервер распознавания Motiff Lab.")
        XCTAssertEqual(ScanServer.problem(URLError(.timedOut), address: server), .slow)
        XCTAssertEqual(ScanProblem.slow.text(russian), "Сервер распознавания слишком долго не отвечал.")
        XCTAssertEqual(ScanProblem.slow.onPage(3, russian), .server("Страница 3: Сервер распознавания слишком долго не отвечал."), "which page, as before")
        XCTAssertEqual(ScanServer.problem(URLError(.cannotConnectToHost), address: server), .unreachable(server))
    }

    /// v1.5 r-2b, review: a photo page the server did not answer in time fails only that page, as the server's own words on
    /// it do, so the photos after it are still sent and joined (`PhotoReader.send`); a server that is not there, or a sign-in,
    /// would meet every page after and stops them.
    func testATimedOutPageDoesNotStopTheOthers() throws {
        let server = try XCTUnwrap(ScanServer.parse("http://192.168.1.20:8000"))
        XCTAssertTrue(ScanServer.problem(URLError(.timedOut), address: server).failsOnlyItsPage)
        XCTAssertTrue(ScanProblem.server("The page is empty.").failsOnlyItsPage)
        XCTAssertFalse(ScanServer.problem(URLError(.cannotConnectToHost), address: server).failsOnlyItsPage)
        XCTAssertFalse(ScanProblem.notAScanServer.failsOnlyItsPage)
        XCTAssertFalse(ScanProblem.signIn.failsOnlyItsPage)
        XCTAssertFalse(ScanProblem.needsPro.failsOnlyItsPage)
    }

    func testTheIPadSendsAPageARequest() {
        XCTAssertEqual(ScanParts.pagesARequest, 1)
        XCTAssertEqual(ScanParts.plan(pages: 3, maxPages: ScanParts.pagesARequest), [1...1, 2...2, 3...3])
    }

    func testAPagesProblemSaysWhichPageAndTheStopsStayAsTheyAre() {
        XCTAssertEqual(ScanProblem.server("The page is empty.").onPage(5, english), .server("Page 5: The page is empty."))
        XCTAssertEqual(ScanProblem.server("The page is empty.").onPage(5, russian), .server("Страница 5: The page is empty."))
        XCTAssertEqual(ScanProblem.signIn.onPage(5, english), .signIn)
    }

    func testPagesReadAreDoneThePageSentIsCurrentAndTheRestWait() {
        var progress = ScanProgress(pages: 4)
        XCTAssertEqual(progress.current, 1)
        XCTAssertEqual(progress.states, [.current, .waiting, .waiting, .waiting])

        progress = progress.read(after: 30_000)
        XCTAssertEqual(progress.done, 1)
        XCTAssertEqual(progress.states, [.done, .current, .waiting, .waiting])

        progress = progress.read(after: 1).read(after: 1).read(after: 1)
        XCTAssertNil(progress.current)
        XCTAssertEqual(progress.read(after: 5), progress, "an answer after the last changes nothing")
        XCTAssertEqual(ScanProgress(pages: 0).pages, 1)
    }

    func testThereIsNoEstimateBeforeTheFirstPageAndThenOnlyFromThePagesMeasured() {
        var progress = ScanProgress(pages: 8)
        XCTAssertNil(progress.minutesLeft)

        // 30 s and 40 s: 35 s a page for the six pages not back yet is three and a half minutes, so about four.
        progress = progress.read(after: 30_000).read(after: 40_000)
        XCTAssertEqual(progress.minutesLeft, 4)
        XCTAssertEqual(ScanProgress.minutes(measured: [30_000, 32_000], pagesLeft: 10), 6, "the mean of the pages read times the pages left")
        XCTAssertNil(ScanProgress.minutes(measured: [], pagesLeft: 3))
        XCTAssertNil(ScanProgress.minutes(measured: [1_000], pagesLeft: 0))
        XCTAssertEqual(ScanProgress.minutes(measured: [1], pagesLeft: 1), 1, "at least one")
    }

    func testTheProgressSaysThePageInBothLanguages() {
        let reading = ScanProgress(pages: 8).read(after: 30_000).read(after: 30_000)
        let first = ScanProgress(pages: 8)
        let all = ScanProgress(pages: 2).read(after: 1).read(after: 1)

        XCTAssertEqual(first.caption(english), "Page 1 of 8")
        XCTAssertEqual(first.caption(russian), "Страница 1 из 8")
        XCTAssertEqual(reading.caption(english), "Page 3 of 8 · about 3 min. left")
        XCTAssertEqual(reading.caption(russian), "Страница 3 из 8 · ещё около 3 мин.")
        XCTAssertEqual(all.line(english), "All 2 pages read")
        for key in ["scan.progressLabel", "pdf.readLineHosted", "recognition.left", "recognition.readingPage", "recognition.pieceOf", "recognition.pageOf"] {
            XCTAssertNotEqual(english[key], key)
            XCTAssertNotEqual(russian[key], key)
        }
    }

    func testTheScanServerOnThisIPadIsOnThisDeviceNotAComputer() {
        // Andrei, 2026-11-18 12:55 UTC (answer 4, after answer 12): the iPad says «на этом устройстве» / "this device".
        let english = UIText(language: "en"), russian = UIText(language: "ru")
        XCTAssertEqual(
            english.format("pdf.readLineHere", "http://localhost:8000"),
            "Recognise notes sends this PDF, up to 100 pages, to the scan server set in Settings, http://localhost:8000 on this device, which deletes it once read. It stays here as it is."
        )
        XCTAssertEqual(
            russian.format("pdf.readLineHere", "http://localhost:8000"),
            "«Распознать ноты» отправит этот PDF, до 100 страниц, на сервер распознавания из Настроек, http://localhost:8000 на этом устройстве, который удалит его после чтения. Здесь он останется как есть."
        )
        XCTAssertEqual(english["settings.scanElsewhere"], "This address is another computer, so pages and recordings you send leave this device. Each send asks first.")
    }
}
