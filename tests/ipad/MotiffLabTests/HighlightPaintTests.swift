import XCTest

@testable import MotiffLab

/// The pen's colour bars and the eraser on the highlights (v0.10, finding 31, HighlightPaint): a passage coloured lays it
/// in its colour, other colours give way there and the same colour joins it; a passage all that colour already loses it;
/// the eraser takes the colour from what it passed; a highlight cut keeps its colour in what is left. Each change names
/// the highlights it takes by value, so the Pen's Undo and Redo (v1.4 i-5) find them again after music-core gave them new
/// ids.
final class HighlightPaintTests: XCTestCase {
    private func lit(_ id: String, _ onset: Double, _ end: Double, _ colour: String) -> HighlightMark {
        HighlightMark(id: id, onset: onset, end: end, firstBar: 1, lastBar: 1, colour: colour)
    }

    private func laid(_ onset: Double, _ end: Double, _ colour: String) -> Recolouring.Highlight {
        Recolouring.Highlight(onset: onset, end: end, colour: colour)
    }

    func testAPassageOnBareMusicIsLaidInItsColour() {
        let change = HighlightPaint.colouring(480..<1920, "forest", over: [])
        XCTAssertEqual(change, Recolouring(taken: [], laid: [laid(480, 1920, "forest")]), "from mid-bar, as Select selects")
        XCTAssertTrue(HighlightPaint.colouring(960..<960, "amber", over: []).isEmpty, "a passage of no length colours nothing")
    }

    func testTheSameColourAgainTakesItOffAndAPartOfItSplitsIt() {
        let bar = [lit("h1", 0, 1920, "amber")]
        XCTAssertEqual(
            HighlightPaint.colouring(0..<1920, "amber", over: bar), Recolouring(taken: [laid(0, 1920, "amber")], laid: []), "a tap again with the same colour"
        )
        XCTAssertEqual(
            HighlightPaint.colouring(480..<960, "amber", over: bar),
            Recolouring(taken: [laid(0, 1920, "amber")], laid: [laid(0, 480, "amber"), laid(960, 1920, "amber")]),
            "a beat inside a coloured bar loses its colour, the rest keeps it"
        )
        let two = [lit("h1", 0, 960, "amber"), lit("h2", 960, 1920, "amber")]
        XCTAssertEqual(
            HighlightPaint.colouring(480..<1440, "amber", over: two).taken, [laid(0, 960, "amber"), laid(960, 1920, "amber")],
            "covered by two of the same colour is covered"
        )
    }

    func testTheSameColourOverlappingOrTouchingBecomesOne() {
        let first = [lit("h1", 0, 1920, "carmine")]
        XCTAssertEqual(
            HighlightPaint.colouring(1920..<3840, "carmine", over: first), Recolouring(taken: [laid(0, 1920, "carmine")], laid: [laid(0, 3840, "carmine")])
        )
        XCTAssertEqual(
            HighlightPaint.colouring(960..<2880, "carmine", over: first),
            Recolouring(taken: [laid(0, 1920, "carmine")], laid: [laid(0, 2880, "carmine")]),
            "a partly coloured passage is coloured whole, as one"
        )
    }

    func testAnotherColourGivesWayWhereTheNewOneIsLaid() {
        let under = [lit("h1", 0, 3840, "ochre"), lit("h2", 5760, 7680, "forest")]
        XCTAssertEqual(
            HighlightPaint.colouring(1920..<2400, "amber", over: under),
            Recolouring(taken: [laid(0, 3840, "ochre")], laid: [laid(0, 1920, "ochre"), laid(2400, 3840, "ochre"), laid(1920, 2400, "amber")]),
            "the ochre round it stays, the highlight elsewhere is not touched"
        )
    }

    func testTheEraserTakesTheColourFromWhatItPassedWhateverItIs() {
        let marks = [lit("h1", 0, 1920, "amber"), lit("h2", 1920, 3840, "forest"), lit("h3", 7680, 9600, "ochre"), lit("h4", 100, 100, "amber")]
        XCTAssertEqual(
            HighlightPaint.uncolouring([1800..<2040, 3720..<3840], over: marks),
            Recolouring(taken: [laid(0, 1920, "amber"), laid(1920, 3840, "forest")], laid: [laid(0, 1800, "amber"), laid(2040, 3720, "forest")]),
            "both colours where it passed, the rest kept; a highlight of no length is left alone"
        )
        XCTAssertTrue(HighlightPaint.uncolouring([4000..<5000], over: marks).isEmpty, "nothing coloured there")
    }

    /// A change is kept by value: the highlights it takes are found again by span, colour and notes whatever their ids
    /// are now, each once, and one no longer there is left out; reversed, it lays back what it took as it was.
    func testAChangeFindsWhatItTakesByValueAndReversedPutsItBack() {
        let pushed = HighlightPaint.colouring(1920..<2400, "amber", over: [lit("h1", 0, 3840, "ochre")])
        let after = [lit("h_0007", 0, 1920, "ochre"), lit("h_0008", 2400, 3840, "ochre"), lit("h_0009", 1920, 2400, "amber"), lit("h_0010", 0, 1920, "ochre")]
        XCTAssertEqual(HighlightPaint.ids(of: pushed.reversed.taken, over: after), ["h_0007", "h_0008", "h_0009"], "by value, each id once")
        XCTAssertEqual(pushed.reversed.laid, [laid(0, 3840, "ochre")], "Undo lays back the colour it pushed out, whole")
        XCTAssertEqual(pushed.reversed.reversed, pushed, "and Redo does the change again")
        XCTAssertEqual(HighlightPaint.ids(of: [laid(0, 960, "forest")], over: after), [], "a highlight no longer there")

        var noted = lit("h2", 0, 1920, "forest")
        noted.notes = ["n1"]
        let erased = HighlightPaint.uncolouring([0..<1920], over: [noted])
        XCTAssertEqual(erased.taken, [Recolouring.Highlight(onset: 0, end: 1920, colour: "forest", notes: ["n1"])], "an older file's notes come back with it")
        XCTAssertEqual(HighlightPaint.ids(of: erased.taken, over: [lit("h3", 0, 1920, "forest"), noted]), ["h2"], "the one with those notes")
    }
}
