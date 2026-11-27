import XCTest

@testable import MotiffLab

/// BookmarkShelf (Marks/Bookmarks.swift): the Contents list the bookmarks in the player's own order as music-core answers
/// them, and in the score's order by where each starts, the one that ends first going first when two start together.
final class BookmarkShelfTests: XCTestCase {
    private func shelf(_ spans: [(id: String, onset: Double, end: Double)]) throws -> BookmarkShelf {
        let bookmarks: [[String: Any]] = spans.map { span in
            ["id": span.id, "title": span.id, "category": "practise", "span": ["onset": span.onset, "end": span.end], "bars": ["first": 1, "last": 1]]
        }
        let categories: [[String: Any]] = [["id": "practise", "name": "Practise", "ink": "forest"]]
        return try XCTUnwrap(BookmarkShelf(answer: facadeRequest(["categories": categories, "bookmarks": bookmarks])))
    }

    func testTheScoresOrderIsByOnsetThenByEnd() throws {
        let shelf = try shelf([
            (id: "third bar", onset: 960, end: 1920),
            (id: "from the start, longer", onset: 0, end: 1920),
            (id: "from the start, shorter", onset: 0, end: 960),
            (id: "second beat", onset: 480, end: 960),
        ])
        XCTAssertEqual(shelf.bookmarks.map(\.id), ["third bar", "from the start, longer", "from the start, shorter", "second beat"], "the player's own order stays as answered")
        XCTAssertEqual(shelf.inScoreOrder.map(\.id), ["from the start, shorter", "from the start, longer", "second beat", "third bar"])
    }

    func testTwoThatStartTogetherGoByTheirEndWhicheverWasMadeFirst() throws {
        let longFirst = try shelf([(id: "long", onset: 480, end: 2400), (id: "short", onset: 480, end: 1440)])
        let shortFirst = try shelf([(id: "short", onset: 480, end: 1440), (id: "long", onset: 480, end: 2400)])
        XCTAssertEqual(longFirst.inScoreOrder.map(\.id), ["short", "long"])
        XCTAssertEqual(shortFirst.inScoreOrder.map(\.id), ["short", "long"])
    }
}
