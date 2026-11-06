import XCTest

@testable import MotiffLab

/// Opening a piece as the app does: music-core's MEI laid out by Verovio on A4 pages in Leland.
final class ScoreTests: XCTestCase {
    /// Four pages with the web app's A4 options (Engraver.a4Leland = A4_LAYOUT: scale 50, top margin 500, no vertical
    /// justification); it was three with the iPad's own larger scale before the two were made equal.
    func testTheExampleOpensOnFourA4Pages() async throws {
        let example = try XCTUnwrap(OpenPiece.example)
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
