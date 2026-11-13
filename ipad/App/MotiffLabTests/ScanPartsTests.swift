import PDFKit
import XCTest
@testable import MotiffLab

/// A score longer than the scan server reads at a time (issue #479): planned in consecutive parts, each part a PDF of its
/// own pages, the notes of the parts joined by music-core with each page of the PDF where it starts, scores of more than
/// 100 pages refused, and the progress shown while it is read, in both languages.
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

    func testAPartsProblemSaysWhichPagesAndTheStopsStayAsTheyAre() {
        XCTAssertEqual(ScanProblem.server("The page is empty.").inPart(5...8, english), .server("Pages 5–8: The page is empty."))
        XCTAssertEqual(ScanProblem.signIn.inPart(5...8, english), .signIn)
    }

    func testTheProgressMovesWithTheTimeAPageTakesAndNeverShowsTheScoreDone() {
        let start = Date(timeIntervalSince1970: 1_800_000_000)
        let first = ScanProgress(pages: 6, parts: [1...4, 5...6], part: 1, sent: start)
        let second = ScanProgress(pages: 6, parts: [1...4, 5...6], part: 2, sent: start)

        XCTAssertEqual(first.fraction(at: start), 0)
        XCTAssertEqual(first.fraction(at: start.addingTimeInterval(60)), 60.0 / 180, accuracy: 0.001, "a minute of the three six pages take")
        XCTAssertLessThan(first.fraction(at: start.addingTimeInterval(3600)), 4.0 / 6, "a part is not done before its notes are back")
        XCTAssertEqual(second.pagesDone, 4)
        XCTAssertEqual(second.fraction(at: start), 4.0 / 6, accuracy: 0.001)
        XCTAssertEqual(first.minutesLeft(at: start), 3)
        XCTAssertEqual(second.minutesLeft(at: start.addingTimeInterval(3600)), 1, "at least one")
    }

    func testTheProgressSaysThePartOrThePagesInBothLanguages() {
        let parts = ScanProgress(pages: 6, parts: [1...4, 5...6], part: 2)
        let whole = ScanProgress(pages: 3, parts: [1...3])

        XCTAssertEqual(parts.line(english), "Part 2 of 2 · pages 5–6")
        XCTAssertEqual(parts.line(russian), "Часть 2 из 2 · страницы 5–6")
        XCTAssertEqual(whole.line(english), "3 pages")
        XCTAssertEqual(whole.line(russian), "3 страницы")
        for key in ["scan.progressLabel", "pdf.readLineHosted", "recognition.left"] {
            XCTAssertNotEqual(english[key], key)
            XCTAssertNotEqual(russian[key], key)
        }
    }
}
