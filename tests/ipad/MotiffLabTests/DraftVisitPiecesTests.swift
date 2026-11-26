import XCTest

@testable import MotiffLab

/// A draft's visit closes each number its view was shown under (r-1): the one before when the view opens again, the one
/// shown when the screen goes, and one that opens after it has gone, at once and never shown.
@MainActor
final class DraftVisitPiecesTests: XCTestCase {
    private func piece(_ id: Int32) -> OpenPiece {
        OpenPiece(id: id, title: "Draft", pages: [], timemap: "")
    }

    func testEachNumberShownIsClosedOnceTheNextShowsOrTheScreenGoes() {
        var closed: [Int32] = []
        let visit = DraftVisitPieces(showing: 1) { closed.append($0) }
        XCTAssertEqual(visit.shown(piece(2))?.id, 2)
        XCTAssertEqual(closed, [1], "the number before goes once the next shows")
        visit.leave()
        XCTAssertEqual(closed, [1, 2], "the number shown goes with the screen")
        visit.leave()
        XCTAssertEqual(closed, [1, 2], "once")
    }

    /// The screen left while the view was opening again: what opened is closed at once, not shown.
    func testAViewOpenedAfterTheScreenWentIsClosedAndNotShown() {
        var closed: [Int32] = []
        let visit = DraftVisitPieces(showing: 1) { closed.append($0) }
        visit.leave()
        XCTAssertNil(visit.shown(piece(2)))
        XCTAssertEqual(closed, [1, 2])
    }

    /// Two openings on their way: each that answers takes the place of the one before, which is closed; none stays open.
    func testTwoOpeningsOnTheirWayLeaveNoneOpen() {
        var closed: [Int32] = []
        let visit = DraftVisitPieces(showing: 1) { closed.append($0) }
        XCTAssertEqual(visit.shown(piece(2))?.id, 2)
        XCTAssertEqual(visit.shown(piece(3))?.id, 3)
        XCTAssertEqual(closed, [1, 2])
        visit.leave()
        XCTAssertNil(visit.shown(piece(4)), "the second's answer after the screen went")
        XCTAssertEqual(Set(closed), [1, 2, 3, 4], "every number closed")
        XCTAssertEqual(closed.count, 4, "each once")
    }
}
