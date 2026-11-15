import XCTest

@testable import MotiffLab

/// Expand as a focused practice view (#521 17, 19; #533 2): the way back by the way in, only with a selection, Play and the
/// playhead scoped to the passage, and nothing in it that changes the selection or edits notes.
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

    /// The playhead on Expand's roll (#533 2): taken while playing, playback stands; dragged either way it scrubs inside the
    /// passage; let go, the notes fade and it plays on. Never a step of the loop, which goes on round the passage.
    func testThePlayheadInExpandIsDraggedInsideThePassageAndPlaysOn() {
        let passage = 1920.0..<3840.0
        let taken = ExpandRules.gesture(.grab(.black), in: passage, drag: nil, tick: 2400, transport: .playing)
        XCTAssertEqual(taken.steps, [.pause], "held, the music stands")
        XCTAssertEqual(taken.drag, PlayheadDrag(handle: .blue, resume: true, last: 2400), "the passage's playhead, playing on when let go")
        let back = ExpandRules.gesture(.move(2040), in: passage, drag: taken.drag, tick: 2400, transport: .stopped)
        XCTAssertEqual(back.steps, [.scrub(from: 2400, to: 2040)], "back: the passed notes sound")
        let past = ExpandRules.gesture(.move(9000), in: passage, drag: back.drag, tick: 2040, transport: .stopped)
        XCTAssertEqual(past.steps, [.scrub(from: 2040, to: 3839)], "on: no further than the passage's end")
        let before = ExpandRules.gesture(.move(0), in: passage, drag: past.drag, tick: 3839, transport: .stopped)
        XCTAssertEqual(before.steps, [.scrub(from: 3839, to: 1920)], "back: no further than its start")
        let letGo = ExpandRules.gesture(.release, in: passage, drag: before.drag, tick: 1920, transport: .stopped)
        XCTAssertEqual(letGo.steps, [.scrubEnd, .play], "plays on round the passage from there")
        XCTAssertNil(letGo.drag)

        let still = ExpandRules.gesture(.grab(.blue), in: passage, drag: nil, tick: 1920, transport: .stopped)
        XCTAssertEqual(still.steps, [], "stopped, taking it plays nothing")
        let moved = ExpandRules.gesture(.move(2400), in: passage, drag: still.drag, tick: 1920, transport: .stopped)
        XCTAssertEqual(ExpandRules.gesture(.release, in: passage, drag: moved.drag, tick: 2400, transport: .stopped).steps, [.scrubEnd], "stopped, it stays stopped (T16)")
        XCTAssertEqual(ExpandRules.gesture(.release, in: passage, drag: nil, tick: 2400, transport: .stopped).steps, [], "nothing held, nothing to let go")
    }

    /// v0.6.4 spec 8: the playback model holds in Expand too (one Play, the black playhead with its handle on the card), over
    /// Expand's passage; the page's selection and Цикл are the model's only out of Expand (T18), and without the modes
    /// nothing changes.
    func testExpandHasThePlaybackModelOverItsPassage() {
        let passage = 1920.0..<3840.0
        XCTAssertTrue(PlaybackScope.onePosition(modes: true), "in Expand and out of it")
        XCTAssertFalse(PlaybackScope.onePosition(modes: false))
        XCTAssertEqual(PlaybackScope.expandPassage(focused: true, playRange: passage), passage)
        XCTAssertNil(PlaybackScope.expandPassage(focused: false, playRange: passage))
        XCTAssertTrue(PlaybackScope.holdsSelection(modes: true, focused: false))
        XCTAssertFalse(PlaybackScope.holdsSelection(modes: true, focused: true), "Expand loops its passage and leaves Цикл as it is")
        XCTAssertFalse(PlaybackScope.holdsSelection(modes: false, focused: false))

        // The pill in Expand: the selection's cobalt Play, at the playhead inside the passage, else naming its start, even
        // with Цикл off on the page; out of Expand the player's model as it is.
        let pageModel = PlaybackModel.State(selection: passage, loop: false, tick: 5000, transport: .stopped)
        let inExpand = PlaybackModel.view(of: PlaybackScope.shown(pageModel, passage: passage))
        XCTAssertEqual(inExpand.context, .selection)
        XCTAssertEqual(inExpand.at, 1920, "Play will start at the passage's start")
        XCTAssertEqual(inExpand.blue, 1920, "the card's playhead waits at the passage's start")
        let inside = PlaybackModel.view(of: PlaybackScope.shown(PlaybackModel.State(selection: passage, loop: false, tick: 2400, transport: .playing), passage: passage))
        XCTAssertEqual(inside.blue, 2400)
        XCTAssertEqual(inside.context, .selection)
        XCTAssertEqual(PlaybackScope.shown(pageModel, passage: nil), pageModel)
        XCTAssertEqual(PlaybackModel.view(of: pageModel).context, .piece, "out of Expand past the selection with Цикл off: ink")
    }

    /// v0.6.4 spec 8: the card's playhead taken while the position is outside the passage is taken where it is drawn, at
    /// the passage's start, so the first move sounds only what lies inside.
    func testThePlayheadTakenOutsideThePassageIsTakenAtItsStart() {
        let passage = 1920.0..<3840.0
        let taken = ExpandRules.gesture(.grab(.black), in: passage, drag: nil, tick: 100, transport: .stopped)
        XCTAssertEqual(taken.drag, PlayheadDrag(handle: .blue, resume: false, last: 1920))
        let moved = ExpandRules.gesture(.move(2400), in: passage, drag: taken.drag, tick: 100, transport: .stopped)
        XCTAssertEqual(moved.steps, [.scrub(from: 1920, to: 2400)])
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
