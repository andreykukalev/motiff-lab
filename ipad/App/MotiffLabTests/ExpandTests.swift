import XCTest

@testable import MotiffLab

/// Expand as a focused practice view (#521 17, 19): the way back by the way in, only with a selection, Play scoped to the
/// passage, and nothing in it that changes the selection or edits notes.
final class ExpandTests: XCTestCase {
    /// From the score «К странице» with the back arrow, from the roll «Свернуть» with the kit's collapse (#521 17).
    func testTheWayBackIsNamedByTheWayIn() {
        XCTAssertEqual(ExpandEntry.notes.backKey, "focus.back")
        XCTAssertEqual(ExpandEntry.notes.backIcon, "back")
        XCTAssertEqual(ExpandEntry.roll.backKey, "focus.collapse")
        XCTAssertEqual(ExpandEntry.roll.backIcon, "collapse")
        XCTAssertNotEqual(ExpandEntry.notes.backHintKey, ExpandEntry.roll.backHintKey)
        let text = UIText(language: "en")
        for entry in [ExpandEntry.notes, .roll] {
            XCTAssertNotEqual(text[entry.backKey], entry.backKey, "\(entry.backKey) has words")
            XCTAssertNotEqual(text[entry.backHintKey], entry.backHintKey, "\(entry.backHintKey) has words")
        }
        XCTAssertNotEqual(text["roll.expandNeedsSelection"], "roll.expandNeedsSelection")
    }

    /// Only with a selection, never while the Pen writes (#521 19.1).
    func testExpandOpensOnlyOnASelection() {
        XCTAssertTrue(ExpandRules.opens(selected: true, penOn: false))
        XCTAssertFalse(ExpandRules.opens(selected: false, penOn: false), "no selection: the button says why instead")
        XCTAssertFalse(ExpandRules.opens(selected: true, penOn: true))
    }

    /// Play starts at the playhead inside the passage, else at its start; a playhead moved stays inside (#521 19.4).
    func testPlayInExpandStaysInThePassage() {
        let passage = 1920.0..<3840.0
        XCTAssertEqual(ExpandRules.playStart(at: 2400, in: passage), 2400, "the playhead inside: from there")
        XCTAssertEqual(ExpandRules.playStart(at: 100, in: passage), 1920, "before it: from its start")
        XCTAssertEqual(ExpandRules.playStart(at: 3840, in: passage), 1920, "at its end or after: from its start again")
        XCTAssertEqual(ExpandRules.place(100, in: passage), 1920)
        XCTAssertEqual(ExpandRules.place(5000, in: passage), 3839, "never past its end")
        XCTAssertEqual(ExpandRules.place(2000, in: passage), 2000)
    }

    /// No selection controls in the stylus menu, no Undo or Redo, and the roll in Navigate, edits waiting (#521 19.3).
    func testNothingInExpandChangesTheSelectionOrEditsNotes() {
        XCTAssertEqual(ExpandRules.ringControls(focused: true, otherwise: StylusRules.ringControls(.select)), [])
        XCTAssertEqual(ExpandRules.ringControls(focused: false, otherwise: StylusRules.ringControls(.select)), [.undo, .redo, .clearSelection])
        XCTAssertEqual(ExpandRules.undoOwner(focused: true, otherwise: .selection), .nothing)
        XCTAssertEqual(ExpandRules.undoOwner(focused: false, otherwise: .ink), .ink)
        XCTAssertEqual(RollMode.shown(chosen: .edit, editorOpen: true, bound: true), .navigate, "Expand edits no notes")
        XCTAssertEqual(RollMode.shown(chosen: .edit, editorOpen: true, bound: false), .edit, "back in the drawer, Edit notes as it was")
        let input = RollInput(stylus: true, mode: RollMode.shown(chosen: .edit, editorOpen: true, bound: true))
        XCTAssertFalse(input.edits(.stylus))
        XCTAssertFalse(input.edits(.finger))
    }
}
