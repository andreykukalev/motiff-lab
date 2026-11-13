import XCTest

@testable import MotiffLab

/// The piano roll as a place to edit notes (#479 item 23, the approved mockup v0.5.2): its own two modes and who edits in
/// them, the zoom's values in words, and the one selection a pick on the roll makes.
final class RollTests: XCTestCase {
    func testEditNotesLeavesTheBarForTheRoll() {
        XCTAssertEqual(InteractionMode.inBar, [.navigate, .select, .pen], "the bar has no Edit notes on the iPad")
        XCTAssertEqual(RollMode.allCases, [.navigate, .edit])
        XCTAssertEqual(RollMode.allCases.map(\.icon), ["scroll", "editnote"])
        XCTAssertEqual(RollMode.edit.label, "rollMode.edit")
        XCTAssertEqual(UIText(language: "ru")[RollMode.navigate.label], "Навигация")
        XCTAssertEqual(UIText(language: "ru")[RollMode.edit.label], "Правка нот")
        XCTAssertEqual(UIText(language: "en")[RollMode.edit.label], "Edit notes")
    }

    func testChoosingAModeOpensTheEditorAndNavigateKeepsADraftWithChanges() {
        XCTAssertEqual(RollMode.choose(.edit, editorOpen: false, changed: false).openEditor, true)
        XCTAssertEqual(RollMode.choose(.edit, editorOpen: true, changed: true).openEditor, false, "an open editor is not opened again")
        XCTAssertEqual(RollMode.choose(.navigate, editorOpen: true, changed: false).closeEditor, true, "nothing changed: the editor closes")
        XCTAssertEqual(RollMode.choose(.navigate, editorOpen: true, changed: true).closeEditor, false, "changes wait for Cancel or Save")
        XCTAssertEqual(RollMode.choose(.navigate, editorOpen: false, changed: false).closeEditor, false)
        XCTAssertEqual(RollMode.shown(chosen: .edit, editorOpen: true), .edit)
        XCTAssertEqual(RollMode.shown(chosen: .edit, editorOpen: false), .navigate, "Edit notes holds only while the editor is open")
        XCTAssertEqual(RollMode.shown(chosen: .navigate, editorOpen: true), .navigate)
    }

    func testInNavigateEveryPointerMovesTheRollWithOneFinger() {
        for stylus in [true, false] {
            let rules = RollInput(stylus: stylus, mode: .navigate)
            XCTAssertFalse(rules.edits(.stylus))
            XCTAssertFalse(rules.edits(.finger))
            XCTAssertEqual(rules.scrollFingers, 1)
        }
    }

    func testInEditNotesTheStylusEditsAndAFingerMovesTheRoll() {
        let rules = RollInput(stylus: true, mode: .edit)
        XCTAssertTrue(rules.edits(.stylus))
        XCTAssertFalse(rules.edits(.finger), "a finger moves the roll while the iPad is used with a stylus")
        XCTAssertEqual(rules.scrollFingers, 1)
        XCTAssertEqual(rules.page["pen"] as? Bool, true)
        XCTAssertEqual(rules.page["touch"] as? Bool, false)
        XCTAssertEqual(rules.page["scrollFingers"] as? Int, 1)
    }

    func testWithoutAStylusOneFingerEditsAndTwoMoveTheRoll() {
        let rules = RollInput(stylus: false, mode: .edit)
        XCTAssertTrue(rules.edits(.finger))
        XCTAssertTrue(rules.edits(.stylus), "without a stylus the Pencil goes as a finger does")
        XCTAssertEqual(rules.scrollFingers, 2)
        XCTAssertEqual(rules.page["touch"] as? Bool, true)
        XCTAssertEqual(rules.page["scrollFingers"] as? Int, 2)
    }

    func testTheZoomSaysBarsAndOctavesInWords() {
        let ru = UIText(language: "ru")
        XCTAssertEqual(RollZoom.barsText(9, ru), "9 тактов")
        XCTAssertEqual(RollZoom.barsText(1, ru), "1 такт")
        XCTAssertEqual(RollZoom.barsText(3, ru), "3 такта")
        XCTAssertEqual(RollZoom.barsText(21, ru), "21 такт")
        XCTAssertEqual(RollZoom.octavesText(3, ru), "3 октавы")
        XCTAssertEqual(RollZoom.octavesText(5, ru), "5 октав")
        XCTAssertEqual(RollZoom.octaveWord(3, ru), "октавы")
        XCTAssertEqual(RollZoom.octaveWord(1, ru), "октава")
        let en = UIText(language: "en")
        XCTAssertEqual(RollZoom.barsText(9, en), "9 bars")
        XCTAssertEqual(RollZoom.barsText(1, en), "1 bar")
        XCTAssertEqual(RollZoom.octavesText(2, en), "2 octaves")
        XCTAssertEqual(RollZoom.octaveWord(1, en), "octave")
    }

    func testTheZoomIsReadFromTheRollsReport() throws {
        let read = try XCTUnwrap(RollZoom(["bars": 9, "octaves": 3, "timeIn": true, "timeOut": false, "pitchIn": true, "pitchOut": true]))
        XCTAssertEqual(read, RollZoom(bars: 9, octaves: 3, timeOut: false))
        XCTAssertNil(RollZoom(["bars": 9]), "a report without the octaves is not read")
    }

    func testADragOnTheRollSelectsByBeatsWhereTheScoreDoesAndATapSelectsItsBar() {
        let drag: [String: Any] = ["first": 9, "last": 12, "extend": false, "click": false, "from": 6720, "to": 5760]
        XCTAssertEqual(RollPick(drag, byBeats: true), .beats(5760..<6720), "the ticks either way round, for the one selection by beats")
        XCTAssertEqual(RollPick(drag, byBeats: false), .bars(first: 9, last: 12, extend: false), "by bars where the score selects bars")
        let tap: [String: Any] = ["first": 9, "last": 9, "extend": false, "click": true, "from": 5760, "to": 5760]
        XCTAssertEqual(RollPick(tap, byBeats: true), .bars(first: 9, last: 9, extend: false), "a tap on a bar number selects that bar")
        let still: [String: Any] = ["first": 9, "last": 9, "click": false, "from": 5760, "to": 5760]
        XCTAssertEqual(RollPick(still, byBeats: true), .beats(5760..<5761), "never an empty stretch")
        XCTAssertNil(RollPick(["from": 0, "to": 10], byBeats: true))
    }
}
