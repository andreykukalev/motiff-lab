import UIKit
import XCTest

@testable import MotiffLab

/// The bars of a scan no two engines agree on, as 1.3 built them (docs/ux-baseline.md, SCAN-06; the spec «Распознавание
/// сканов четырьмя движками в фоне», part 6, decision record 0035 section 6, Andrei's «Новая причина в файле» of
/// 2026-11-24 14:48 UTC and «только на этом iPad» of 17:41 UTC): flagged on the score with the reason «Движки разошлись»,
/// a tap on the flag opening Check on the bar with its crop of the original page and the engines' other readings, one of
/// them put in with one tap as the player's correction of that bar, and its Undo. A page read by a job of three engines
/// (answered here, `StubAPI`) over the real music-core: in 4/4, four quarters in the right hand over a whole C3, the three
/// reading bar 2's right hand each otherwise, as in music-core's own test of it (`ScanVariantsTests`); the page is a
/// picture drawn here. Taken through the score's screen as `WorkspaceSession` drives it. The block's look is the device's.
@MainActor
final class ScanCheckBaselineTests: XCTestCase {
    private let russian = UIText(language: "ru")
    private var folders: [URL] = []
    private var pieces: [Int32] = []
    private var reviews: [URL] = []
    private var jobsBefore: String?
    /// The flags the score's page was last sent to draw.
    private var drawn: [[String: Any]]?

    override func tearDown() async throws {
        for piece in pieces { MusicCore.close(piece: piece) }
        pieces = []
        for folder in folders + reviews { try? FileManager.default.removeItem(at: folder) }
        folders = []
        reviews = []
        await ScanSupport.restore(jobsBefore)
        jobsBefore = nil
        StubAPI.reset()
    }

    private func note(_ step: String, _ octave: Int, _ duration: Int, staff: Int) -> String {
        "<note><pitch><step>\(step)</step><octave>\(octave)</octave></pitch><duration>\(duration)</duration><voice>\(staff == 1 ? 1 : 5)</voice><staff>\(staff)</staff></note>"
    }

    /// An engine's reading of the page: bar 1's right hand C D E F, bar 2's `second`, each over a whole C3.
    private func reading(_ second: [String]) -> String {
        func bar(_ right: [String]) -> String {
            right.map { note($0, 5, 2, staff: 1) }.joined() + "<backup><duration>\(2 * right.count)</duration></backup>" + note("C", 3, 8, staff: 2)
        }
        let attributes = "<attributes><divisions>2</divisions><key><fifths>1</fifths></key><time><beats>4</beats><beat-type>4</beat-type></time><staves>2</staves>"
            + #"<clef number="1"><sign>G</sign><line>2</line></clef><clef number="2"><sign>F</sign><line>4</line></clef></attributes>"#
        return #"<?xml version="1.0" encoding="UTF-8"?><score-partwise version="4.0"><part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list><part id="P1">"#
            + #"<measure number="1">"# + attributes + bar(["C", "D", "E", "F"]) + #"</measure><measure number="2">"# + bar(second) + "</measure></part></score-partwise>"
    }

    /// The page's evidence as the scan server writes it: one system of two staves (middle lines at 100 and 200, a space of
    /// 10) over two bars of 100 pixels, on an image 1000 wide and 400 high; no symbols.
    private var evidence: [String: Any] {
        func staff(_ number: Int, _ middle: Double) -> [String: Any] {
            ["staff": number, "lines": [middle - 20, middle - 10, middle, middle + 10, middle + 20], "space": 10, "barLines": [Any]()]
        }
        let system: [String: Any] = ["box": [0, 80, 200, 220], "staves": [staff(1, 100), staff(2, 200)], "bars": [["x": [0, 100]], ["x": [100, 200]]]]
        return ["evidence": 1, "page": 1, "detector": ["name": "copisteria", "version": "test"],
                "image": ["width": 1000, "height": 400, "dpi": 300, "rotated": 0, "deskewed": 0], "systems": [system], "symbols": [Any]()]
    }

    /// The job's answers: page 1 read by homr, copisteria and audiveris, bar 2's right hand ending on D5, C5 and E5.
    private var job: [String: StubAPI.Answer] {
        var engines: [String: Any] = [:]
        for (engine, second) in [("homr", ["G", "A", "B", "D"]), ("copisteria", ["G", "A", "B", "C"]), ("audiveris", ["G", "A", "B", "E"])] {
            engines[engine] = ["state": "read", "version": "test", "musicxml": reading(second)] as [String: Any]
        }
        let status: [String: Any] = ["id": "j1", "state": "done", "etaSeconds": 0, "pages": [["page": 1, "engines": ["homr": "read", "copisteria": "read", "audiveris": "read"]]]]
        return [
            "http://scan.test/health": ScanSupport.health, "POST http://scan.test/jobs": ScanSupport.answer(202, #"{"id": "j1"}"#),
            "http://scan.test/jobs/j1": ScanSupport.json(status),
            "http://scan.test/jobs/j1/result": ScanSupport.json(["id": "j1", "pages": [["page": 1, "engines": engines, "evidence": evidence] as [String: Any]]]),
            "DELETE http://scan.test/jobs/j1": ScanSupport.answer(204, ""),
        ]
    }

    /// The piece of one picture of a page, read by the job as the library reads it, and opened on its notes' screen.
    private func readAndOpen() async throws -> (WorkspaceSession, OpenPiece) {
        // The page dark where bar 2 stands, so its crop is cut there.
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("scan-check-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        folders.append(folder)
        let url = try ScanSupport.pdfPiece(in: folder, pdf: ScanSupport.scannedPdf(pages: 1), pages: 1)
        StubAPI.reset(job)
        let follow = RecognitionJob(kept: nil, keep: { _ in }, hear: { _ in }, wanted: { true }, ready: { true })
        let read = await RecognitionQueue.recognise(
            url, address: ScanSupport.server, account: Account(options: nil), links: folder, folders: folder, job: follow, session: StubAPI.session(), isOpen: { false }
        )
        guard case .success = read else {
            XCTFail("read: \(read)")
            throw CancellationError()
        }
        let opened = try await OpenPiece.open(fileName: url.lastPathComponent, data: try Data(contentsOf: url), language: "ru").get()
        pieces.append(opened.id)
        XCTAssertFalse(opened.isDocument, "the piece opens on its notes")
        let session = await WorkspaceSession(piece: opened)
        if let fingerprint = session.workspace.score.check.fingerprint {
            reviews.append(ScanReviewStore.root.appendingPathComponent(fingerprint, isDirectory: true))
        }
        return (session, opened)
    }

    /// SCAN-06: the bar no two engines agree on carries a Check flag on the score, with the reason «Движки разошлись»; the bar
    /// they agree on has none. A tap on the flag selects the bar and opens Check on it, where this iPad shows the bar's crop
    /// of the original page and the engines' other readings; one tap puts one in, on the staff they disagree on only, as the
    /// player's corrections, and «Отменить выбор» takes it back whole.
    func testSCAN06ABarTheEnginesDisagreeOnIsFlaggedATapShowsItsCropAndReadingsAndOneIsPutInAsThePlayersCorrection() async throws {
        jobsBefore = await ScanSupport.jobsOn()
        let (session, opened) = try await readAndOpen()
        let workspace = session.workspace
        let score = workspace.score

        let flagged = try XCTUnwrap(score.check.openBars.first { $0.bar == 2 }, "bar 2 is to check")
        let disagree = try XCTUnwrap(flagged.flags.first { $0.reason == CheckShelf.enginesDisagree })
        XCTAssertEqual(disagree.name, "Движки разошлись")
        XCTAssertNil(score.check.openBars.first { $0.bar == 1 }, "the bar they agree on is not")
        let page = try XCTUnwrap(score.scorePage as? RecordingScorePage)
        let recording = page.called
        page.called = { [weak self] command, argument in
            if command == .checks { self?.drawn = (argument as? [String: Any])?["flags"] as? [[String: Any]] }
            recording?(command, argument)
        }
        await session.step("the score drawn again") { score.show(opened, dark: true) }
        try await ScanSupport.wait("the flags drawn") { drawn != nil }
        let flag = try XCTUnwrap(drawn?.first { $0["bar"] as? Int == 2 }, "an amber flag over bar 2")
        XCTAssertEqual(flag["label"] as? String, russian.format("check.flagLabel", "2", CheckShelf.reasonNames(flagged.flags, russian)))
        XCTAssertNil(drawn?.first { $0["bar"] as? Int == 1 })

        await session.step("a tap on the flag") { score.handle(.checkFlag(2)) }
        try await ScanSupport.wait("the bar selected") { score.selectionModel.selection?.firstBar == 2 }
        XCTAssertTrue(workspace.panels[.check], "Check opens on its bar")
        let kept = try XCTUnwrap(score.scanReview?.bar(2), "what this iPad kept of the bar")
        XCTAssertTrue(kept.cropped)
        let crop = try XCTUnwrap(score.scanCrops[2], "the bar's crop of the original page")
        XCTAssertEqual(crop.size, CGSize(width: 100, height: 220), "the bar's box, its staves and four spaces above and below")
        XCTAssertEqual(kept.variants.map(\.engines), [["copisteria"], ["audiveris"]], "the other engines' readings, homr's taken")
        XCTAssertEqual(kept.variants.map { $0.names(rest: russian["scanReview.rest"]) }, ["G5 A5 B5 C5", "G5 A5 B5 E5"], "the right hand only")
        XCTAssertEqual(russian["scanReview.variants"], "Другие прочтения движков")
        XCTAssertEqual(russian["scanReview.variantHint"], "Ставит эти ноты только в этот такт, как вашу правку")
        XCTAssertTrue(score.editsOn, "with «Мои правки» on a reading can be put in")

        let copisteria = try XCTUnwrap(kept.variants.first)
        await session.step("copisteria's reading chosen") { score.chooseVariant(copisteria, bar: 2) }
        try await ScanSupport.wait("put in") { score.variantChoice.engines(in: 2) == ["copisteria"] }
        let chosen = try XCTUnwrap(score.corrections)
        XCTAssertEqual(chosen.bar, 2)
        XCTAssertEqual(chosen.notes.map(\.pitch).sorted(), [48, 72, 79, 81, 83], "copisteria's right hand, the left hand as read")
        XCTAssertGreaterThan(chosen.count, 0, "as the player's corrections")
        XCTAssertEqual(russian.format("scanReview.chosen", "copisteria"), "Сейчас в такте: copisteria")
        XCTAssertEqual(russian["scanReview.undo"], "Отменить выбор")

        await session.step("«Отменить выбор»") { score.undoVariant() }
        try await ScanSupport.wait("taken back whole") { score.corrections?.count == 0 }
        XCTAssertEqual(score.corrections?.notes.map(\.pitch).sorted(), [48, 74, 79, 81, 83], "the bar as merged")
        XCTAssertNil(score.variantChoice.bar)
        XCTAssertEqual(russian["scanReview.missing"], "Вырезка страницы и прочтения движков для этого такта есть только на iPad, который распознавал скан.")
    }
}
