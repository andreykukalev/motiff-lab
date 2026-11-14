import XCTest

@testable import MotiffLab

/// The selection's Undo and Redo (#511 section 11), the same cases as music-core's SelectionHistoryTests on the web, and
/// the stylus menu's rules (#511 4.2, option B): the pure parts.
final class SelectionHistoryTests: XCTestCase {
    private let one = SelectionSpan(onset: 0, end: 1920)
    private let two = SelectionSpan(onset: 0, end: 3840)
    private let far = SelectionSpan(onset: 7680, end: 9600)

    func testAMadeSelectionIsAStepThatUndoTakesAwayAndRedoBringsBack() {
        var history = SelectionHistory<SelectionSpan>()
        XCTAssertEqual(history.record(from: nil, to: one, gesture: "g1")?.kind, .create)
        XCTAssertNil(history.undo()?.before)
        XCTAssertTrue(history.canRedo)
        XCTAssertFalse(history.canUndo)
        XCTAssertEqual(history.redo()?.after, one)
    }

    func testAHoldThatClearsAndADragAfterItAreOneReplaceStep() {
        var history = SelectionHistory<SelectionSpan>()
        history.record(from: nil, to: one, gesture: "g1")
        history.record(from: one, to: nil, gesture: "g2")
        XCTAssertEqual(history.record(from: nil, to: far, gesture: "g2")?.kind, .replace)
        XCTAssertEqual(history.done.count, 2)
        XCTAssertEqual(history.undo()?.before, one, "Undo brings the old selection back, not none")
    }

    func testAClearOnItsOwnIsAClearStep() {
        var history = SelectionHistory<SelectionSpan>()
        history.record(from: nil, to: one)
        XCTAssertEqual(history.record(from: one, to: nil)?.kind, .clear)
    }

    func testEdgeMovesOfOneGestureJoinAndAChangeEndingWhereItBeganIsNoStep() {
        var history = SelectionHistory<SelectionSpan>()
        history.record(from: nil, to: one, gesture: "g1")
        XCTAssertEqual(history.record(from: one, to: two, gesture: "g2")?.kind, .change)
        XCTAssertNil(history.record(from: two, to: one, gesture: "g2"))
        XCTAssertEqual(history.done.count, 1)
        XCTAssertNil(history.record(from: one, to: one, gesture: "g3"), "a touch that changes nothing")
        XCTAssertEqual(history.done.count, 1)
    }

    func testANewStepDropsRedo() {
        var history = SelectionHistory<SelectionSpan>()
        history.record(from: nil, to: one, gesture: "g1")
        history.record(from: one, to: two, gesture: "g2")
        _ = history.undo()
        history.record(from: one, to: far, gesture: "g3")
        XCTAssertFalse(history.canRedo)
    }

    func testACancelledGestureLeavesNoStep() {
        var history = SelectionHistory<SelectionSpan>()
        history.record(from: nil, to: one, gesture: "g1")
        history.record(from: one, to: nil, gesture: "g2")
        XCTAssertEqual(history.revert("g2")?.before, one)
        XCTAssertNil(history.revert("g9"))
        XCTAssertEqual(history.done.count, 1)
    }

    func testOnlyTheLastFiftyStepsAreKept() {
        var history = SelectionHistory<SelectionSpan>()
        for index in 0..<60 {
            history.record(from: SelectionSpan(onset: Double(index), end: Double(index + 1)), to: SelectionSpan(onset: Double(index + 1), end: Double(index + 2)))
        }
        XCTAssertEqual(history.done.count, 50)
        XCTAssertEqual(history.done.first?.before?.onset, 10)
    }

    func testNothingToUndoOrRedoGivesNothing() {
        var history = SelectionHistory<SelectionSpan>()
        XCTAssertNil(history.undo())
        XCTAssertNil(history.redo())
    }

    /// PDF | Notes makes the screen anew: the same piece finds its history again, another piece starts its own.
    @MainActor
    func testTheHistoryOutlivesTheScreenForTheSamePiece() {
        var history = SelectionHistory<SelectionSpan>()
        history.record(from: nil, to: one, gesture: "g1")
        SelectionHistories.keep(history, of: "/pieces/a.motiff")
        XCTAssertTrue(SelectionHistories.history(of: "/pieces/a.motiff").canUndo)
        XCTAssertFalse(SelectionHistories.history(of: "/pieces/b.motiff").canUndo)
    }

    // MARK: - The stylus menu (option B)

    func testSwitchToPreviousTakesThePenAndSelectInTurnUnderTheModes() {
        XCTAssertEqual(StylusRules.previous(penOn: false, mode: .navigate).penOn, true)
        XCTAssertEqual(StylusRules.previous(penOn: false, mode: .navigate).mode, .navigate, "the Pen comes on over the mode, which comes back after it")
        let off = StylusRules.previous(penOn: true, mode: .navigate)
        XCTAssertFalse(off.penOn)
        XCTAssertEqual(off.mode, .select)
        XCTAssertFalse(off.previousTool)
        let without = StylusRules.previous(penOn: true, mode: nil)
        XCTAssertTrue(without.penOn && without.previousTool, "without the modes the tool before, as before")
        XCTAssertEqual(StylusRules.previous(penOn: false, mode: nil).penOn, true, "outside the Pen it comes on")
    }

    func testUndoIsTheHistoryOfTheToolInForceAndNavigateHasNone() {
        // #521 9: each editing tool its own history; Navigate none, so ⌘Z there takes back no selection step.
        XCTAssertEqual(StylusRules.undoOwner(penOn: true, mode: .select, rollEditing: false), .ink)
        XCTAssertEqual(StylusRules.undoOwner(penOn: false, mode: .select, rollEditing: false), .selection)
        XCTAssertEqual(StylusRules.undoOwner(penOn: false, mode: .navigate, rollEditing: false), .nothing)
        XCTAssertEqual(StylusRules.undoOwner(penOn: false, mode: .navigate, rollEditing: true), .noteEdits, "the roll in Edit notes")
        XCTAssertEqual(StylusRules.undoOwner(penOn: false, mode: nil, rollEditing: false), .selection, "before the modes, as before")
    }

    func testTheStylusMenuShowsOnlyTheChosenToolsControls() {
        // #521 8, 11.
        XCTAssertEqual(StylusRules.ringControls(.select), [.undo, .redo, .clearSelection])
        XCTAssertEqual(StylusRules.ringControls(.pen), [.undo, .redo])
        XCTAssertEqual(StylusRules.ringControls(.navigate), [], "Navigate has no history and no settings")
    }

    func testClearSelectionHasItsOwnIconAndNotTheCrossThatCloses() {
        // #533 5: the same icon wherever the action is (tests/web/design.test.mjs checks the catalogue holds every kit icon).
        XCTAssertEqual(StylusRules.RingControl.clearSelection.icon, "deselect")
        XCTAssertNotEqual(StylusRules.RingControl.clearSelection.icon, "close")
        XCTAssertEqual(StylusRules.RingControl.undo.icon, "undo")
        XCTAssertEqual(StylusRules.RingControl.redo.icon, "undo", "Redo draws Undo's mirrored")
    }

    func testThePensHistoryUndoesAndRedoesAndANewStrokeDropsRedo() {
        var ink = InkHistory()
        XCTAssertFalse(ink.canUndo || ink.canRedo)
        ink.record(InkStep(added: ["a"], strokes: [["id": "a"]]))
        ink.record(InkStep(added: ["b"], strokes: [["id": "b"]]))
        let undone = ink.undo()
        XCTAssertEqual(undone?.added, ["b"])
        XCTAssertTrue(ink.canRedo)
        let redone = ink.redo()
        XCTAssertEqual(redone?.added, ["b"])
        XCTAssertFalse(ink.canRedo)
        _ = ink.undo()
        ink.record(InkStep(added: ["c"]))
        XCTAssertFalse(ink.canRedo, "a new stroke drops what Redo had")
        XCTAssertEqual(ink.done.map(\.added), [["a"], ["c"]])
    }

    func testSelectsOptionsChooseBarsOrBeatsUnderTheModes() {
        XCTAssertTrue(StylusRules.byBeats(modes: true, chosen: true, experiment: false))
        XCTAssertFalse(StylusRules.byBeats(modes: true, chosen: false, experiment: true), "by bars as Select's options say")
        XCTAssertTrue(StylusRules.byBeats(modes: false, chosen: false, experiment: true), "without the modes the experiment decides")
        let defaults = UserDefaults(suiteName: "SelectionHistoryTests")!
        defaults.removePersistentDomain(forName: "SelectionHistoryTests")
        XCTAssertTrue(SelectBy.saved(defaults), "by beats until bars are chosen")
        SelectBy.keep(false, defaults)
        XCTAssertFalse(SelectBy.saved(defaults))
    }

    /// The bug of the evaluation: outside the Pen, «switch to eraser» and «switch to previous» changed the tool unseen.
    @MainActor
    func testOutsideThePenTheEraserAndPreviousTurnThePenOn() {
        let pdf = PdfController()
        let saved = PenMemory.saved()
        defer { PenMemory.keep(saved) }
        pdf.pen = PenState(active: .pencil, previous: .marker, pencil: ToolSettings(width: 0, ink: .carmine))
        pdf.penOn = false
        pdf.pencilGesture(.eraser, at: nil)
        XCTAssertTrue(pdf.penOn)
        XCTAssertEqual(pdf.tool, .eraser)
        pdf.penOn = false
        pdf.pencilGesture(.eraser, at: nil)
        XCTAssertEqual(pdf.tool, .eraser, "the eraser chosen already stays, the Pen comes on with it")
        XCTAssertTrue(pdf.penOn)
        pdf.penOn = false
        pdf.pencilGesture(.previous, at: nil)
        XCTAssertTrue(pdf.penOn)
        XCTAssertEqual(pdf.tool, .eraser, "the Pen comes on with its tool; the tool before comes only while it is on")
    }
}
