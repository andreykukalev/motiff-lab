import XCTest
@testable import MotiffLab

/// Piece DNA's playhead (Andrei, v0.8 finding 15): the handle's place along the bar axis and back. Dragged anywhere, it is
/// let go on a whole tick inside the piece, so the roll's black playhead rules (Player.handle, PlaybackPositionTests) get a
/// place they can play from.
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

    func testThePlaceLetGoIsAWholeTickInsideThePiece() {
        let piece = map([960, 2880, 4800])
        XCTAssertEqual(piece.tick(at: 0), 960)
        XCTAssertEqual(piece.tick(at: 0.5), 2880)
        XCTAssertEqual(piece.tick(at: 0.3333), 2240, "rounded to a whole tick")
        XCTAssertEqual(piece.tick(at: 1), 4799, "the end is not a place to play from: its last tick")
        XCTAssertEqual(piece.tick(at: 1.4), 4799, "dragged past the end")
        XCTAssertEqual(piece.tick(at: -0.2), 960, "dragged before the start")
    }

    func testAPlaceAndItsTickAgree() {
        let piece = map([0, 1920, 3840])
        for tick in [0.0, 480, 1920, 3000] {
            XCTAssertEqual(piece.tick(at: piece.fraction(of: tick)), tick)
        }
    }
}
