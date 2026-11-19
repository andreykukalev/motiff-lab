import XCTest
@testable import MotiffLab

/// Piece DNA's playhead (Andrei, v0.8 finding 15): the handle's place along the bar axis and back. Dragged anywhere, it is
/// let go on the roll's sixteenth grid inside the piece, so the roll's black playhead rules (Player.handle,
/// PlaybackPositionTests) get a place they can play from.
final class DnaPlayheadTests: XCTestCase {
    private func map(_ starts: [Double]) -> DnaMap {
        var map = DnaMap()
        map.barStarts = starts
        return map
    }

    func testATickLiesAlongTheMapFromItsStartToItsEnd() {
        let piece = map([0, 1920, 3840, 5760])
        XCTAssertEqual(piece.fraction(of: 0), 0)
        XCTAssertEqual(piece.fraction(of: 2880), 0.5)
        XCTAssertEqual(piece.fraction(of: 5760), 1)
        XCTAssertEqual(piece.fraction(of: -480), 0, "before the piece: at its start")
        XCTAssertEqual(piece.fraction(of: 9999), 1, "after it: at its end")
        XCTAssertEqual(map([]).fraction(of: 480), 0, "no bars: nowhere to go")
    }

    func testThePlaceLetGoIsASixteenthInsideThePiece() {
        let piece = map([960, 2880, 4800])
        XCTAssertEqual(piece.tick(at: 0), 960)
        XCTAssertEqual(piece.tick(at: 0.5), 2880)
        XCTAssertEqual(piece.tick(at: 0.3333), 2280, "2239.9 goes to the nearest sixteenth of its bar")
        XCTAssertEqual(piece.tick(at: 1), 4680, "the end is not a place to play from: its bar's last sixteenth")
        XCTAssertEqual(piece.tick(at: 1.4), 4680, "dragged past the end")
        XCTAssertEqual(piece.tick(at: -0.2), 960, "dragged before the start")
    }

    func testTheGridIsCountedFromTheStartOfTheBar() {
        // Bars of 1000 ticks are not whole sixteenths: the grid starts again at each bar line, as on the roll.
        let piece = map([0, 1000, 2000])
        XCTAssertEqual(piece.tick(at: 0.565), 1120, "1130 in the second bar: a sixteenth after its start, not 1080")
        XCTAssertEqual(piece.tick(at: 0.495), 960, "990 in the first bar: its last sixteenth, never past its end")
    }

    func testAPlaceAndItsTickAgree() {
        let piece = map([0, 1920, 3840])
        for tick in [0.0, 480, 1920, 3000] {
            XCTAssertEqual(piece.tick(at: piece.fraction(of: tick)), tick)
        }
    }
}
