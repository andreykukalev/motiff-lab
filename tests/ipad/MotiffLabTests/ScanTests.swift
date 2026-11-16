import XCTest
@testable import MotiffLab

/// Photos of pages (v0.4, experiment «Фото страниц»): the order of the pages on the review screen, how a photo's look is
/// measured and warned about before sending, the scan server's page report, the progress page by page, the `scan.join`
/// request and its answer, the summary after reading, in both languages, and the reports kept with the notes
/// (`document.read`, v0.4) that Check reads back page by page.
final class ScanTests: XCTestCase {
    private let english = UIText(language: "en")
    private let russian = UIText(language: "ru")

    private func pages(_ count: Int) -> PhotoPageList {
        PhotoPageList(pages: (0..<count).map { PhotoPage(photo: Data([UInt8($0)])) })
    }

    private func order(_ list: PhotoPageList) -> [UInt8] { list.pages.map { $0.photo[0] } }

    // MARK: - The review screen

    func testAPageDraggedOntoAnotherTakesItsPlace() {
        var list = pages(4)
        list.move(list.pages[3].id, to: list.pages[1].id)
        XCTAssertEqual(order(list), [0, 3, 1, 2], "a page from further on goes before the page it is dropped on")
        list.move(list.pages[0].id, to: list.pages[2].id)
        XCTAssertEqual(order(list), [3, 1, 0, 2], "a page from earlier goes after it")
        list.move(list.pages[1].id, to: list.pages[1].id)
        XCTAssertEqual(order(list), [3, 1, 0, 2], "dropped on itself, nothing moves")
        XCTAssertEqual(list.number(of: list.pages[2].id), 3)
    }

    func testAPageStepsEarlierOrLaterButNotPastTheEnds() {
        var list = pages(3)
        list.step(list.pages[0].id, by: -1)
        XCTAssertEqual(order(list), [0, 1, 2])
        list.step(list.pages[0].id, by: 1)
        XCTAssertEqual(order(list), [1, 0, 2])
        list.step(list.pages[2].id, by: 1)
        XCTAssertEqual(order(list), [1, 0, 2])
    }

    func testPagesTurnTakeOutAndArePhotographedAgainUpright() {
        var list = pages(3)
        let second = list.pages[1].id
        for _ in 0..<5 { list.turn(second) }
        XCTAssertEqual(list.pages[1].turns, 1, "five quarter turns are one")
        list.measured(second, PageLook(sharpness: 0.5, contrast: 120))
        list.replace(second, with: Data([9]))
        XCTAssertEqual(list.pages[1].photo, Data([9]))
        XCTAssertEqual(list.pages[1].turns, 0)
        XCTAssertNil(list.pages[1].look, "a new photo is measured again")
        XCTAssertEqual(list.pages[1].id, second)
        list.remove(list.pages[0].id)
        XCTAssertEqual(order(list), [9, 2])
    }

    // MARK: - How a photo looks

    private let width = 1600
    private let height = 1200

    /// A page of paper with lines 4 pixels thick every 48, `ink` grey levels darker, softened over `blur` pixels up and down.
    private func staffPage(ink: Int, blur: Int = 0) -> [UInt8] {
        var grey = [UInt8](repeating: 230, count: width * height)
        for line in stride(from: 40, to: height - 44, by: 48) {
            for y in line..<(line + 4) {
                for x in 20..<(width - 20) { grey[y * width + x] = UInt8(230 - ink) }
            }
        }
        guard blur > 0 else { return grey }
        var soft = grey
        for y in blur..<(height - blur) {
            for x in 0..<width {
                var sum = 0
                for offset in -blur...blur { sum += Int(grey[(y + offset) * width + x]) }
                soft[y * width + x] = UInt8(sum / (2 * blur + 1))
            }
        }
        return soft
    }

    func testACrispPageInGoodLightNeedsNoNewPhoto() {
        let look = PageLook.measure(grey: staffPage(ink: 200), width: width, height: height)
        XCTAssertEqual(Double(look.contrast), 200, accuracy: 2)
        XCTAssertFalse(look.isDark)
        XCTAssertFalse(look.isBlurry, "sharpness \(look.sharpness)")
        XCTAssertNil(PageWarning(page: 1, look: look))
    }

    func testFaintPrintIsDarkAndSoftPrintIsBlurry() {
        let faint = PageLook.measure(grey: staffPage(ink: 60), width: width, height: height)
        XCTAssertTrue(faint.isDark, "contrast \(faint.contrast)")
        let soft = PageLook.measure(grey: staffPage(ink: 200, blur: 2), width: width, height: height)
        XCTAssertFalse(soft.isDark)
        XCTAssertTrue(soft.isBlurry, "sharpness \(soft.sharpness)")
        XCTAssertEqual(PageLook.measure(grey: [UInt8](repeating: 200, count: 100), width: 10, height: 10), PageLook(sharpness: 0, contrast: 0), "blank paper has no print")
    }

    func testTheWarningNamesThePageAndWhatToDo() {
        var list = pages(4)
        list.measured(list.pages[1].id, PageLook(sharpness: 1.2, contrast: 120))
        list.measured(list.pages[3].id, PageLook(sharpness: 0.7, contrast: 118))
        list.measured(list.pages[2].id, PageLook(sharpness: 0.5, contrast: 58))
        XCTAssertEqual(list.warnings.map(\.page), [3, 4], "a page not measured yet has no warning")
        XCTAssertEqual(list.warnings[1].text(russian), "Страница 4 нечёткая, переснимите.")
        XCTAssertEqual(list.warnings[1].text(english), "Page 4 is blurry: photograph it again.")
        XCTAssertEqual(list.warnings[0].text(russian), "Страница 3 тёмная и нечёткая, переснимите при лучшем свете.")
    }

    // MARK: - Reading page by page

    private let report = #"[{"page":1,"rotation":90,"sharpness":1.2,"contrast":118,"staves":10,"noteheads":226,"barLines":59,"spacePx":13.0,"measures":28}]"#

    func testThePageReportIsReadFromTheHeader() {
        let pages = try? XCTUnwrap(PageReportEntry.parse(report))
        XCTAssertEqual(pages, [PageReportEntry(rotation: 90, sharpness: 1.2, contrast: 118, staves: 10, measures: 28)])
        XCTAssertEqual(PageReportEntry.parse(#"[{"page":1}]"#)?.first?.measures, 0, "a number the server does not send counts as 0")
        XCTAssertNil(PageReportEntry.parse(nil))
        XCTAssertNil(PageReportEntry.parse("not json"))
        XCTAssertNil(PageReportEntry.parse(#"{"page":1}"#), "the header is a list")
        XCTAssertNil(PageReportEntry.parse("[1, 2]"))
    }

    func testProgressCountsThePagesRead() {
        var readings = PhotoReadings(count: 6)
        XCTAssertEqual(readings.progress(russian), "готово 0 из 6")
        readings.set(1, .read(musicXml: "<a/>", report: nil))
        readings.set(2, .failed(message: "no staves", report: nil))
        readings.set(3, .read(musicXml: "<b/>", report: nil))
        readings.set(4, .read(musicXml: "<c/>", report: nil))
        readings.set(5, .sending)
        readings.set(9, .sending)
        XCTAssertEqual(readings.progress(russian), "готово 3 из 6")
        XCTAssertEqual(readings.progress(english), "3 of 6 done")
        XCTAssertTrue(readings.isBusy)
        XCTAssertFalse(readings.allTried)
        XCTAssertEqual(readings.readPages, [1, 3, 4])
    }

    func testTheJoinSendsThePagesReadWithTheirReportsOrOnePageReadAgain() throws {
        var readings = PhotoReadings(count: 3)
        XCTAssertNil(readings.joinRequest(), "nothing read, nothing to join")
        readings.set(1, .read(musicXml: "<one/>", report: report))
        readings.set(2, .failed(message: "no staves", report: report))
        readings.set(3, .read(musicXml: "<three/>", report: report))
        let first = try XCTUnwrap(readings.joinRequest())
        XCTAssertEqual(first["pages"] as? [String], ["<one/>", "<three/>"])
        XCTAssertEqual(first["reports"] as? [String], [report, report])
        XCTAssertNil(first["score"])

        XCTAssertTrue(readings.joined(["score": "<joined/>", "pages": [["page": 1, "bars": 28, "doubtful": 2, "quality": "good"], ["page": 2, "bars": 12, "doubtful": 12, "quality": "poor"]]], from: [1, 3], reports: first["reports"] as? [String]))
        XCTAssertEqual(readings.joinedReports, [report, report], "the reports the join took go on to the notes")
        readings.set(3, .read(musicXml: "<again/>", report: nil))
        let again = try XCTUnwrap(readings.joinRequest(again: 3))
        XCTAssertEqual(again["score"] as? String, "<joined/>")
        XCTAssertEqual((again["replace"] as? [[String: Any]])?.first?["page"] as? Int, 2, "the page's place in the joined score")
        XCTAssertEqual((again["replace"] as? [[String: Any]])?.first?["musicxml"] as? String, "<again/>")
        XCTAssertNil(again["reports"], "a page with no report leaves the reports out")

        readings.set(2, .read(musicXml: "<two/>", report: report))
        let more = try XCTUnwrap(readings.joinRequest(again: 2))
        XCTAssertEqual(more["pages"] as? [String], ["<one/>", "<two/>", "<again/>"], "a page newly read joins afresh")
    }

    func testTheSummarySaysPageByPageWhatIsInDoubt() {
        var readings = PhotoReadings(count: 4)
        readings.set(1, .read(musicXml: "<a/>", report: report))
        readings.set(2, .failed(message: "Не найдены нотоносцы.", report: nil))
        readings.set(3, .read(musicXml: "<c/>", report: report))
        readings.set(4, .read(musicXml: "<d/>", report: report))
        XCTAssertFalse(readings.joined(["error": "badRequest"], from: [1, 3, 4], reports: [report, report, report]))
        XCTAssertNil(readings.joinedReports, "a join refused keeps no reports")
        XCTAssertTrue(readings.joined(["score": "<s/>", "pages": [
            ["page": 1, "bars": 12, "doubtful": 0, "quality": "good"],
            ["page": 2, "bars": 12, "doubtful": 9, "quality": "weak"],
            ["page": 3, "bars": 21, "doubtful": 21, "quality": "poor"],
        ]], from: [1, 3, 4]))

        let lines = readings.summary(russian)
        XCTAssertEqual(lines.map(\.text), [
            "Страница 1: 12 тактов, вопросов нет.",
            "Страница 2 не прочиталась: Не найдены нотоносцы.",
            "Страница 3: 9 из 12 тактов под вопросом. Снимок темноват или нерезкий.",
            "Страница 4: 21 из 21 такта под вопросом. Снимок слишком тёмный или нечёткий, ему нельзя доверять.",
        ])
        XCTAssertEqual(lines.map(\.retake), [false, true, true, true])
        XCTAssertEqual(lines.map(\.unsure), [false, true, true, true])
        XCTAssertEqual(readings.summary(english)[2].text, "Page 3: 9 of 12 bars in doubt. The photo is a little dark or soft.")
        XCTAssertEqual(PageSummaryLine.of(5, JoinedPage(bars: 8, doubtful: 3, quality: nil), russian).text, "Страница 5: 3 из 8 тактов под вопросом.")
        XCTAssertEqual(readings.barRanges, [1: 1...12, 3: 13...24, 4: 25...45])
    }

    // MARK: - Through music-core

    private func page(_ measures: Int) -> String {
        var xml = #"<?xml version="1.0" encoding="UTF-8"?><score-partwise version="4.0"><part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list><part id="P1">"#
        for number in 1...measures {
            let attributes = number == 1 ? "<attributes><divisions>1</divisions><time><beats>3</beats><beat-type>4</beat-type></time></attributes>" : ""
            xml += #"<measure number="\#(number)">\#(attributes)<note><pitch><step>C</step><octave>5</octave></pitch><duration>3</duration></note></measure>"#
        }
        return xml + "</part></score-partwise>"
    }

    func testMusicCoreJoinsThePagesAndCountsTheReports() throws {
        var readings = PhotoReadings(count: 2)
        let dim = #"[{"page":1,"rotation":0,"sharpness":0.76,"contrast":58,"staves":13,"noteheads":121,"barLines":48,"spacePx":17.0,"measures":2}]"#
        readings.set(1, .read(musicXml: page(3), report: report))
        readings.set(2, .read(musicXml: page(2), report: dim))
        let body = try JSONSerialization.data(withJSONObject: try XCTUnwrap(readings.joinRequest()))
        let answer = try XCTUnwrap(facadeObject(MusicCore.call(piece: 0, "scan.join", String(decoding: body, as: UTF8.self))))
        XCTAssertTrue(readings.joined(answer, from: readings.readPages), "\(answer)")
        XCTAssertEqual(readings.joinedPages[1], JoinedPage(bars: 3, doubtful: 0, quality: "good"))
        XCTAssertEqual(readings.joinedPages[2], JoinedPage(bars: 2, doubtful: 2, quality: "poor"))
        XCTAssertEqual(readings.summary(english)[1].text, "Page 2: 2 of 2 bars in doubt. The photo is too dark or too soft to trust.")
    }

    func testTheReadRequestCarriesTheReportsOnlyWhenThereAreSome() {
        let now = Date(timeIntervalSince1970: 0)
        let with = DocumentRead.request(musicXml: Data("<s/>".utf8), engine: "homr 0.7.0", pages: 2, reports: [report, report], language: "ru", now: now)
        XCTAssertEqual(with["reports"] as? [String], [report, report])
        XCTAssertEqual(with["musicXml"] as? String, Data("<s/>".utf8).base64EncodedString())
        XCTAssertEqual(with["pages"] as? Int, 2)
        XCTAssertEqual(with["now"] as? String, "1970-01-01T00:00:00Z")
        XCTAssertNil(DocumentRead.request(musicXml: Data(), engine: "e", pages: 1, reports: [], language: "en", now: now)["reports"])
        XCTAssertNil(DocumentRead.request(musicXml: Data(), engine: "e", pages: 1, reports: nil, language: "en", now: now)["reports"])
    }

    func testCheckReadsAScansPagesAndWhatToShowOfThem() throws {
        let answer = #"{"flags": [{"bar": 4, "source": "scan", "reason": "pageQuality", "name": "Плохая страница", "text": "Страница плохая.", "resolved": false, "span": null}], "open": 1, "origin": "scanned", "engine": "homr 0.7.0", "pages": [{"page": 1, "quality": "good", "rotation": 0, "bars": 3, "doubtful": 0, "firstBar": 1, "lastBar": 3}, {"page": 2, "quality": "poor", "rotation": 90, "bars": 2, "doubtful": 2, "firstBar": 4, "lastBar": 5}, {"page": 3, "quality": "weak", "rotation": 0, "bars": 0, "doubtful": 0, "firstBar": null, "lastBar": null}]}"#
        let check = try XCTUnwrap(CheckShelf(answer: answer))
        XCTAssertEqual(check.pages, [
            CheckPageEntry(page: 1, quality: "good", rotation: 0, bars: 3, doubtful: 0, firstBar: 1, lastBar: 3),
            CheckPageEntry(page: 2, quality: "poor", rotation: 90, bars: 2, doubtful: 2, firstBar: 4, lastBar: 5),
            CheckPageEntry(page: 3, quality: "weak", rotation: 0, bars: 0, doubtful: 0, firstBar: nil, lastBar: nil),
        ])
        XCTAssertEqual(check.pages.map(\.barRange), [1...3, 4...5, nil])
        XCTAssertEqual(check.pages.map { PageSummaryLine.of($0, russian).unsure }, [false, true, true], "only the pages in doubt are listed")
        XCTAssertEqual(PageSummaryLine.of(check.pages[1], russian).text, "Страница 2: 2 из 2 тактов под вопросом. Снимок слишком тёмный или нечёткий, ему нельзя доверять.")
        XCTAssertEqual(CheckShelf.reasonNames(check.openBars[0].flags, russian), "Плохая страница", "a poor page's flag is named by music-core")
        XCTAssertEqual(CheckShelf(answer: #"{"flags": [], "open": 0}"#)?.pages, [], "no reports, no pages")
    }

    /// A PDF piece of `count` pages as music-core counts them (its page objects), opened in the language.
    private func pdfPiece(_ count: Int, language: String) throws -> Int32 {
        let pdf = Data(("%PDF-1.4\n1 0 obj << /Type /Pages /Count \(count) >> endobj\n" + (2...(count + 1)).map { "\($0) 0 obj << /Type /Page >> endobj\n" }.joined() + "%%EOF\n").utf8)
        let opened = facadeObject(MusicCore.open(fileName: "Pages.pdf", data: pdf, request: #"{"language": "\#(language)"}"#))
        return try XCTUnwrap((opened?["piece"] as? NSNumber)?.int32Value)
    }

    @MainActor
    func testThePageReportsAreKeptWithTheNotesAndCheckTellsThePagesAfter() async throws {
        var readings = PhotoReadings(count: 2)
        let dim = #"[{"page":1,"rotation":0,"sharpness":0.76,"contrast":58,"staves":13,"noteheads":121,"barLines":48,"spacePx":17.0,"measures":2}]"#
        readings.set(1, .read(musicXml: page(3), report: report))
        readings.set(2, .read(musicXml: page(2), report: dim))
        let join = try XCTUnwrap(readings.joinRequest())
        let joinBody = String(decoding: try JSONSerialization.data(withJSONObject: join), as: UTF8.self)
        let joinAnswer = try XCTUnwrap(facadeObject(MusicCore.call(piece: 0, "scan.join", joinBody)))
        XCTAssertTrue(readings.joined(joinAnswer, from: readings.readPages, reports: join["reports"] as? [String]))
        let score = try XCTUnwrap(readings.joined)

        let piece = try pdfPiece(2, language: "en")
        defer { MusicCore.close(piece: piece) }
        let request = DocumentRead.request(musicXml: Data(score.utf8), engine: "homr 0.7.0", pages: 2, reports: readings.joinedReports, language: "en", now: Date())
        let answer = await DocumentRead.call(piece: piece, request)
        let read = try XCTUnwrap(facadeObject(answer))
        XCTAssertNil(read["error"], "\(read)")
        let file = try XCTUnwrap(read["file"] as? String)

        // Opened again, as after the app was closed: Check tells the pages from the file.
        let again = try XCTUnwrap((facadeObject(MusicCore.open(fileName: "Pages.motiff.json", data: Data(file.utf8), request: #"{"language": "en"}"#))?["piece"] as? NSNumber)?.int32Value)
        defer { MusicCore.close(piece: again) }
        let check = try XCTUnwrap(CheckShelf(answer: MusicCore.call(piece: again, "check")))
        XCTAssertEqual(check.pages.map(\.quality), ["good", "poor"])
        XCTAssertEqual(check.pages.map(\.barRange), [1...3, 4...5])
        XCTAssertEqual(check.pages.map(\.doubtful), [0, 2])
        XCTAssertEqual(Set(check.flags.filter { $0.bar >= 4 }.map(\.name)), ["Poor page"], "the poor page's bars are flagged, named by music-core")
        XCTAssertEqual(PageSummaryLine.of(check.pages[1], english).text, "Page 2: 2 of 2 bars in doubt. The photo is too dark or too soft to trust.")
    }

    @MainActor
    func testReportsMusicCoreRefusesAreLeftOutAndTheNotesReadWithout() async throws {
        let piece = try pdfPiece(1, language: "en")
        defer { MusicCore.close(piece: piece) }
        // Two reports for the one page of the notes: not one per page.
        let request = DocumentRead.request(musicXml: Data(page(3).utf8), engine: "homr 0.7.0", pages: 1, reports: [report, report], language: "en", now: Date())
        let refused = facadeObject(MusicCore.call(piece: piece, "document.read", String(decoding: try JSONSerialization.data(withJSONObject: request), as: UTF8.self)))
        XCTAssertEqual(refused?["error"] as? String, "badRequest", "music-core takes one report per page of the notes")
        let answer = await DocumentRead.call(piece: piece, request)
        let read = try XCTUnwrap(facadeObject(answer))
        XCTAssertNil(read["error"], "\(read)")
        let file = try XCTUnwrap(read["file"] as? String)
        let again = try XCTUnwrap((facadeObject(MusicCore.open(fileName: "Pages.motiff.json", data: Data(file.utf8), request: #"{"language": "en"}"#))?["piece"] as? NSNumber)?.int32Value)
        defer { MusicCore.close(piece: again) }
        let check = try XCTUnwrap(CheckShelf(answer: MusicCore.call(piece: again, "check")))
        XCTAssertEqual(check.pages, [], "read without the reports")
        XCTAssertEqual(check.origin, "scanned")
    }
}
