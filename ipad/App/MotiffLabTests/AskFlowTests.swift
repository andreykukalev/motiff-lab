import CoreGraphics
import XCTest

@testable import MotiffLab

/// The chat's interaction model on the iPad (#582 item 16, AskFlow in AskField.swift): open → focus → keyboard → type →
/// hide the keyboard → go on using the chat, the same with the Pencil and with a finger, on every surface.
final class AskFlowTests: XCTestCase {
    func testTheWholeSequenceKeepsTheChatAndItsControls() {
        var flow = AskFlow(surface: .popover)
        flow.apply(.open)
        XCTAssertTrue(flow.controlsShown)
        XCTAssertFalse(flow.focused, "the field waits to be pressed: no keyboard comes up on its own")
        XCTAssertFalse(flow.canSend)

        // A press on the field (the Pencil's, Scribble declined, or a finger's) focuses it; the keyboard comes.
        flow.apply(.focus)
        flow.apply(.keyboard(true))
        XCTAssertTrue(flow.focused)
        XCTAssertTrue(flow.controlsShown, "the keyboard never takes the chat's controls")

        flow.apply(.type("  Where does this return?  "))
        XCTAssertTrue(flow.canSend)
        XCTAssertEqual(flow.apply(.send), "Where does this return?")
        XCTAssertEqual(flow.draft, "")
        XCTAssertTrue(flow.focused, "after Send the field keeps the focus for the next question")

        // The keyboard's hide key: the field lets go, the chat stays open with its controls.
        flow.apply(.type("And the left hand?"))
        flow.apply(.keyboard(false))
        flow.apply(.blur)
        XCTAssertTrue(flow.isOpen)
        XCTAssertTrue(flow.controlsShown)
        XCTAssertFalse(flow.focused)
        XCTAssertEqual(flow.draft, "And the left hand?", "the text typed stays")

        // And the chat goes on: pressed again, the field takes the focus and Send asks.
        flow.apply(.focus)
        XCTAssertEqual(flow.apply(.send), "And the left hand?")
    }

    func testTheKeyboardGoingAloneChangesNothingButItself() {
        var flow = AskFlow(surface: .contextual)
        flow.apply(.open)
        flow.apply(.focus)
        flow.apply(.keyboard(true))
        flow.apply(.type("Remove the stray note"))
        flow.apply(.keyboard(false))
        XCTAssertTrue(flow.isOpen)
        XCTAssertTrue(flow.focused, "a hardware keyboard's bar going leaves the field focused")
        XCTAssertEqual(flow.draft, "Remove the stray note")
    }

    func testAPressOutsideWhileTypingOnlyPutsTheKeyboardAway() {
        for surface in [AskFlow.Surface.popover, .contextual, .panel] {
            var flow = AskFlow(surface: surface)
            flow.apply(.open)
            flow.apply(.focus)
            flow.apply(.type("Why here?"))
            XCTAssertFalse(flow.closesOnOutsidePress)
            flow.apply(.pressOutside)
            XCTAssertTrue(flow.isOpen, "\(surface): hiding the keyboard never closes the chat")
            XCTAssertFalse(flow.focused)
            XCTAssertEqual(flow.draft, "Why here?")
        }
    }

    func testOnlyTheTemporaryChatsCloseFromOutsideOnceNothingIsTyped() {
        var popover = AskFlow(surface: .popover)
        popover.apply(.open)
        XCTAssertTrue(popover.closesOnOutsidePress)
        popover.apply(.pressOutside)
        XCTAssertFalse(popover.isOpen)

        var contextual = AskFlow(surface: .contextual)
        contextual.apply(.open)
        contextual.apply(.pressOutside)
        XCTAssertFalse(contextual.isOpen)

        var panel = AskFlow(surface: .panel)
        panel.apply(.open)
        XCTAssertFalse(panel.closesOnOutsidePress)
        panel.apply(.pressOutside)
        XCTAssertTrue(panel.isOpen, "the full assistant's panel closes only by its own button")
    }

    func testCloseForgetsTheDraftAndOpeningAgainWaitsForAPress() {
        var flow = AskFlow(surface: .panel)
        flow.apply(.open)
        flow.apply(.focus)
        flow.apply(.type("Half typed"))
        flow.apply(.close)
        XCTAssertEqual(flow, AskFlow(surface: .panel))
        flow.apply(.open)
        XCTAssertFalse(flow.focused)
        XCTAssertEqual(flow.draft, "")
    }

    func testNothingIsSentEmptyOrWhileClosed() {
        var flow = AskFlow(surface: .popover)
        flow.apply(.type("ignored"))
        flow.apply(.focus)
        XCTAssertFalse(flow.focused, "a closed chat takes no focus")
        XCTAssertNil(flow.apply(.send))
        flow.apply(.open)
        flow.apply(.type("   "))
        XCTAssertNil(flow.apply(.send))
    }

    func testTheKeyboardsOverlapIsWhatItCoversFromBelow() {
        let sheet = CGRect(x: 600, y: 80, width: 380, height: 640)
        XCTAssertEqual(KeyboardRoom.overlap(of: sheet, keyboard: nil), 0)
        // A docked keyboard over the lower part of the sheet.
        XCTAssertEqual(KeyboardRoom.overlap(of: sheet, keyboard: CGRect(x: 0, y: 420, width: 1024, height: 348)), 300)
        // A keyboard below the sheet, or floating beside it, covers nothing.
        XCTAssertEqual(KeyboardRoom.overlap(of: sheet, keyboard: CGRect(x: 0, y: 730, width: 1024, height: 38)), 0)
        XCTAssertEqual(KeyboardRoom.overlap(of: sheet, keyboard: CGRect(x: 40, y: 300, width: 320, height: 250)), 0)
        // Never more than the sheet's own height.
        XCTAssertEqual(KeyboardRoom.overlap(of: sheet, keyboard: CGRect(x: 0, y: 0, width: 1024, height: 768)), 640)
    }
}
