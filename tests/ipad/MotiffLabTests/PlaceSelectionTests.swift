import XCTest

@testable import MotiffLab

/// Selection by the place of the touch (v0.6.2, #601, PlaceSelection.swift): the one selection (a span by beats, a hand,
/// notes picked) as the history keeps it and music-core selects it again, the iPad's pick of one note, and the switch in
/// the page's rules. The gesture itself is the page's (tests/shared/selection-places.test.mjs).
final class PlaceSelectionTests: XCTestCase {
    func testTheOneSelectionIsSelectedAgainAsItWas() {
        XCTAssertEqual(SelectionSpan(onset: 0, end: 1920).request, #"{"empty":true,"span":{"end":1920,"onset":0}}"#)
        // v0.6.4: the span exactly as the page drew it on the sixteenth grid, never taken out to the beats.
        XCTAssertEqual(SelectionSpan(onset: 600, end: 840).request, #"{"empty":true,"span":{"end":840,"onset":600}}"#)
        XCTAssertEqual(SelectionSpan(onset: 480, end: 1920, hand: "left").request, #"{"empty":true,"filter":{"hand":"left"},"span":{"end":1920,"onset":480}}"#)
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
        let answer = #"{"selection": {"onset": 0, "end": 1920, "firstBar": 1, "lastBar": 1, "firstBeat": 1, "lastBeat": 4, "filter": {"hand": "left"}, "noteIds": ["n1"]}}"#
        XCTAssertEqual(ScoreSelection(answer: answer)?.hand, "left")
        XCTAssertNil(ScoreSelection(answer: answer)?.picked, "music-core does not say a pick; the controller does")
        let both = #"{"selection": {"onset": 0, "end": 1920, "firstBar": 1, "lastBar": 1, "firstBeat": 1, "lastBeat": 4, "filter": {}, "noteIds": ["n1"]}}"#
        XCTAssertNil(ScoreSelection(answer: both)?.hand)
    }

    func testThePageIsToldTheSwitchOnTheNotesOnly() {
        var rules = InputRules(stylus: true, tool: .select)
        XCTAssertEqual(rules.page["places"] as? Bool, true, "always on the notes, since v0.6.5")
        XCTAssertEqual(rules.selectStart(.stylus), .now, "the rules of who selects stay as they are")
        XCTAssertEqual(rules.selectStart(.finger), .longPress)
        rules.surface = .pdf
        XCTAssertEqual(rules.page["places"] as? Bool, false)
    }

    func testThePagesMessagesSayAReplaceALengthRefusedAndWhereToZoom() {
        XCTAssertTrue(ScoreController.replaces(["how": "replace", "gesture": "g"]))
        XCTAssertFalse(ScoreController.replaces(["how": "edit"]))
        XCTAssertFalse(ScoreController.replaces([:]))
        XCTAssertEqual(ScoreController.refusal("A selection spans at most 32 bars. (Parameter 'span')"), "selection.tooLong")
        XCTAssertEqual(ScoreController.refusal("There are no notes in that passage."), "selection.noNotes")
        XCTAssertEqual(UIText(language: "en")["selection.noNotes"], "No notes there: the selection stays as it was", "the iPad's words (answer 12)")
        XCTAssertEqual(UIText(language: "ru")["selection.noNotes"], "Здесь нет нот: выделение осталось прежним")
        XCTAssertNil(ScoreController.refusal("Send \"bars\" {first, last}."))
        XCTAssertNil(ScoreController.refusal(nil))
        let box: [String: Any] = ["left": 100, "top": 200, "right": 300, "bottom": 260]
        XCTAssertEqual(ScoreController.zoomRect(box, margin: 24), CGRect(x: 76, y: 176, width: 248, height: 108))
        XCTAssertNil(ScoreController.zoomRect(nil, margin: 24))
        XCTAssertNil(ScoreController.zoomRect(["left": 100, "top": 200, "right": 100, "bottom": 260], margin: 24))
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

    func testThePencilsSwitchToPreviousTakesSelectAndTheToolBeforeItInTurns() {
        let fromPen = StylusRules.selectTurn(current: .pen, before: nil)
        XCTAssertEqual(fromPen.chosen, .select)
        XCTAssertEqual(fromPen.before, .pen)
        let back = StylusRules.selectTurn(current: .select, before: fromPen.before)
        XCTAssertEqual(back.chosen, .pen)
        XCTAssertNil(back.before)
        let fromNavigate = StylusRules.selectTurn(current: .navigate, before: nil)
        XCTAssertEqual(fromNavigate.chosen, .select)
        XCTAssertEqual(StylusRules.selectTurn(current: .select, before: fromNavigate.before).chosen, .navigate, "back to Navigate, not the Pen")
        XCTAssertEqual(StylusRules.selectTurn(current: .select, before: nil).chosen, .pen, "with no tool before, the Pen, as before")
    }

    func testTheSelectionKeptOnTheDeviceKeepsItsHandAndNotesAndReadsWhatWasKeptBefore() throws {
        let defaults = try XCTUnwrap(UserDefaults(suiteName: "PlaceSelectionTests"))
        defer { defaults.removePersistentDomain(forName: "PlaceSelectionTests") }
        var state = NotesViewState()
        state.selection = SelectionSpanKept(ScoreSelection(onset: 480, end: 960, firstBar: 1, lastBar: 1, hand: "left", picked: ["n4"]))
        ViewStates.keep(state, "p_1", in: defaults)
        let kept = try XCTUnwrap(ViewStates.notes("p_1", in: defaults)?.selection)
        XCTAssertEqual(kept, SelectionSpanKept(onset: 480, end: 960, hand: "left", picked: ["n4"]))
        XCTAssertEqual(kept.span.request, #"{"notes":["n4"],"picked":true}"#)
        // What version 0.6.1 kept: a span only.
        defaults.set(Data(#"{"selection": {"onset": 0, "end": 1920}, "zoom": 1, "left": 0, "position": 0, "loop": false, "rollOpen": false}"#.utf8), forKey: ViewStates.notesKey("p_2"))
        let old = try XCTUnwrap(ViewStates.notes("p_2", in: defaults)?.selection)
        XCTAssertEqual(old.span.request, #"{"empty":true,"span":{"end":1920,"onset":0}}"#)
    }

    func testARollSpanIsReadAsDrawnAndTheTupletsAsBlocks() throws {
        let span = try XCTUnwrap(RollSpan(["start": 600, "end": 840, "how": "replace", "gesture": "g1"]))
        XCTAssertEqual(span.start, 600)
        XCTAssertEqual(span.end, 840)
        XCTAssertEqual(span.how, "replace")
        XCTAssertEqual(span.gesture, "g1")
        XCTAssertEqual(RollSpan(["start": 600, "end": 840])?.how, "create", "a span without how is a new one")
        XCTAssertNil(RollSpan(["start": 600, "end": 600]), "never an empty stretch")
        XCTAssertNil(RollSpan(["start": 600]))
        let blocks = ScoreController.tuplets([["start": 0, "end": 480], ["start": 960], ["start": 960, "end": 960]])
        XCTAssertEqual(blocks, [["start": 0, "end": 480]], "only groups with both ends")
    }

    func testTheToastStandsAboveTheTransportAsTallAsItIs() {
        XCTAssertEqual(ToastPlace.bottom(pillBottom: 16, transport: 52, gap: 12), 80, "above the pill with Loop and the metronome")
        XCTAssertEqual(ToastPlace.bottom(pillBottom: 316, transport: 104, gap: 12), 432, "over a drawer, above a taller transport")
        XCTAssertEqual(ToastPlace.bottom(pillBottom: 16, transport: 0, gap: 12), 16, "nothing floats: at the transport's place")
    }
}
