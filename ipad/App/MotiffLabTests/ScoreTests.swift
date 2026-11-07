import XCTest

@testable import MotiffLab

/// Opening a piece as the app does: music-core's MEI laid out by Verovio on A4 pages in Leland.
final class ScoreTests: XCTestCase {
    /// Four pages with the web app's A4 options (Engraver.a4Leland = A4_LAYOUT: scale 50, top margin 500, no vertical
    /// justification); it was three with the iPad's own larger scale before the two were made equal.
    func testTheExampleOpensOnFourA4Pages() async throws {
        let example = try XCTUnwrap(KitPiece.example)
        let clock = ContinuousClock()
        let start = clock.now
        let result = await OpenPiece.open(fileName: example.fileName, data: example.data, language: "ru", title: example.title)
        let elapsed = clock.now - start
        let piece = try result.get()
        defer { _ = MusicCore.close(piece: piece.id) }
        XCTAssertEqual(piece.title, "Für Elise")
        XCTAssertEqual(piece.pages.count, 4)
        XCTAssertTrue(piece.pages.allSatisfy { $0.contains("<svg") })
        print("OPEN-AND-ENGRAVE \(elapsed)")
    }

    /// Even bars (Settings · Notes, testing item 35) add Verovio's spacing by length to the A4 page; off, its own by the notes.
    func testEvenBarsSpaceTheBarsByTheirLength() throws {
        let even = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(Engraver.a4(evenBars: true).utf8)) as? [String: Any])
        XCTAssertEqual((even["spacingLinear"] as? NSNumber)?.doubleValue, 0.08)
        XCTAssertEqual((even["spacingNonLinear"] as? NSNumber)?.doubleValue, 0.9)
        XCTAssertEqual((even["pageMarginTop"] as? NSNumber)?.intValue, 500)
        let byNotes = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(Engraver.a4(evenBars: false).utf8)) as? [String: Any])
        XCTAssertEqual((byNotes["spacingNonLinear"] as? NSNumber)?.doubleValue, 0.6)
    }

    /// Testing log item 26 (2026-11-06): the example is Für Elise whole, one piano part, as on the web.
    func testTheAppsExampleIsTheWholePiece() async throws {
        let example = try XCTUnwrap(OpenPiece.example, "the example piece is in the app")
        let opened = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(MusicCore.open(fileName: example.fileName, data: example.data, request: #"{"language": "en"}"#).utf8)) as? [String: Any])
        let summary = try XCTUnwrap(opened["summary"] as? [String: Any], "\(opened)")
        _ = MusicCore.close(piece: Int32(opened["piece"] as? Int ?? 0))
        XCTAssertEqual(summary["noteCount"] as? Int, 902)
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        defer { _ = MusicCore.close(piece: piece.id) }
        XCTAssertEqual(piece.title, "Für Elise")
        XCTAssertGreaterThan(piece.pages.count, 4)
    }

    func testAFileThatIsNoMusicSaysWhy() async {
        let result = await OpenPiece.open(fileName: "notes.mid", data: Data("not midi".utf8), language: "en")
        guard case .failure(let error) = result else { return XCTFail("a text file opened as MIDI") }
        XCTAssertFalse(error.message.isEmpty)
    }

    func testTheInterfaceSpeaksTheWebAppsWords() {
        XCTAssertEqual(UIText(language: "ru")["home.fromFile"], "Открыть файл")
        XCTAssertEqual(UIText(language: "en")["home.fromFile"], "Open a file")
        XCTAssertEqual(UIText(language: "en")["no.such.key"], "no.such.key")
    }
}
