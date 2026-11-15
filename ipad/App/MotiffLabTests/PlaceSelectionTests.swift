import XCTest

@testable import MotiffLab

/// Selection by the place of the touch (v0.6.2, #601, PlaceSelection.swift): the one selection (a span by beats, a hand,
/// notes picked) as the history keeps it and music-core selects it again, the iPad's pick of one note, and the switch in
/// the page's rules. The gesture itself is the page's (tests/web/selection-places.test.mjs).
final class PlaceSelectionTests: XCTestCase {
    func testTheOneSelectionIsSelectedAgainAsItWas() {
        XCTAssertEqual(SelectionSpan(onset: 0, end: 1920).request, #"{"snap":"beat","span":{"end":1920,"onset":0}}"#)
        XCTAssertEqual(SelectionSpan(onset: 480, end: 1920, hand: "left").request, #"{"filter":{"hand":"left"},"snap":"beat","span":{"end":1920,"onset":480}}"#)
        XCTAssertEqual(SelectionSpan(onset: 480, end: 960, hand: "right", picked: ["n1"]).request, #"{"notes":["n1"],"picked":true}"#)
        XCTAssertEqual(SelectionSpan.picking(["a", "b"]), #"{"notes":["a","b"],"picked":true}"#)
    }

    func testTheHistoryKeepsTheHandAndTheNotes() {
        let selection = ScoreSelection(onset: 480, end: 960, firstBar: 1, lastBar: 1, noteIds: ["n1", "n2"], hand: "right", picked: ["n1"])
        let kept = SelectionSpan(selection)
        XCTAssertEqual(kept, SelectionSpan(onset: 480, end: 960, hand: "right", picked: ["n1"]))
        XCTAssertNotEqual(kept, SelectionSpan(onset: 480, end: 960), "another hand or another pick is another selection")
        var history = SelectionHistory<SelectionSpan>()
        history.record(from: nil, to: SelectionSpan(onset: 0, end: 1920), gesture: "g1")
        XCTAssertEqual(history.record(from: SelectionSpan(onset: 0, end: 1920), to: kept, gesture: "g2")?.kind, .change)
        XCTAssertEqual(history.undo()?.after?.picked, ["n1"])
    }

    func testANewSelectionInPlaceOfTheOldIsAReplaceStepWithoutAClear() {
        let one = SelectionSpan(onset: 0, end: 1920)
        let far = SelectionSpan(onset: 7680, end: 9600)
        var history = SelectionHistory<SelectionSpan>()
        XCTAssertEqual(history.record(from: nil, to: one, gesture: "g1", replaces: true)?.kind, .create, "nothing to replace")
        XCTAssertEqual(history.record(from: one, to: far, gesture: "g2", replaces: true)?.kind, .replace)
        XCTAssertEqual(history.record(from: far, to: one, gesture: "g3")?.kind, .change, "an edit is no replace")
        XCTAssertEqual(history.undo()?.before, far)
        XCTAssertEqual(history.undo()?.before, one, "Undo of the replace brings the old selection back")
    }

    func testAPickAddsToTheNotesPickedEachOnce() {
        XCTAssertEqual(ScoreController.pickedNotes(["c", "a"], adding: nil), ["c", "a"])
        XCTAssertEqual(ScoreController.pickedNotes(["b", "c"], adding: ["a", "b"]), ["a", "b", "c"])
        XCTAssertEqual(ScoreController.pickedNotes([], adding: nil), [])
    }

    func testTheSelectionReadsItsHandFromMusicCore() {
        let answer = #"{"selection": {"onset": 0, "end": 1920, "firstBar": 1, "lastBar": 1, "filter": {"hand": "left"}, "noteIds": ["n1"]}}"#
        XCTAssertEqual(ScoreSelection(answer: answer)?.hand, "left")
        XCTAssertNil(ScoreSelection(answer: answer)?.picked, "music-core does not say a pick; the controller does")
        XCTAssertNil(ScoreSelection(answer: #"{"selection": {"onset": 0, "end": 1920, "filter": {}}}"#)?.hand)
    }

    func testThePageIsToldTheSwitchOnTheNotesOnly() {
        var rules = InputRules(stylus: true, tool: .select)
        XCTAssertEqual(rules.page["places"] as? Bool, false)
        rules.places = true
        XCTAssertEqual(rules.page["places"] as? Bool, true)
        XCTAssertEqual(rules.selectStart(.stylus), .now, "the rules of who selects stay as they are")
        XCTAssertEqual(rules.selectStart(.finger), .longPress)
        rules.surface = .pdf
        XCTAssertEqual(rules.page["places"] as? Bool, false)
    }

    func testOneNotePickedOnTheIPadIsASelection() throws {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let opened = try XCTUnwrap(facadeObject(MusicCore.open(fileName: example.fileName, data: example.data, request: nil)))
        let piece = Int32(opened["piece"] as? Int ?? 0)
        defer { MusicCore.close(piece: piece) }
        let bars = try XCTUnwrap(ScoreSelection(answer: MusicCore.call(piece: piece, "select", #"{"bars": {"first": 1, "last": 1}}"#)))
        let note = try XCTUnwrap(bars.noteIds.first)
        let one = try XCTUnwrap(ScoreSelection(answer: MusicCore.call(piece: piece, "select", SelectionSpan.picking([note]))))
        XCTAssertEqual(one.noteIds, [note])
        XCTAssertLessThan(one.onset, one.end)
    }
}
