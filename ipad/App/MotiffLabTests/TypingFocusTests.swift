import CoreGraphics
import XCTest

@testable import MotiffLab

/// The keyboard's focus on the iPad (v0.6.4, TypingFocus in TypingFocus.swift): a tap outside the field puts the keyboard
/// away, a press on the field brings it back, and the press that puts it away never reaches the score or the roll.
final class TypingFocusTests: XCTestCase {
    /// Two fields, kept alive so their identities differ.
    private let fields = (NSObject(), NSObject())
    private var field: ObjectIdentifier { ObjectIdentifier(fields.0) }
    private var other: ObjectIdentifier { ObjectIdentifier(fields.1) }

    func testNothingTypedInMeansNoPressHidesAnythingAndTheMusicIsOpen() {
        let focus = TypingFocus()
        XCTAssertFalse(focus.editing)
        XCTAssertFalse(focus.shieldsMusic, "with no keyboard the score and the roll take every press as before")
        for place in [TypingFocus.Place.editor, .otherField, .keeper, .systemMenu, .music, .elsewhere] {
            XCTAssertFalse(focus.hides(place))
        }
    }

    func testATapOutsideTheFieldPutsTheKeyboardAwayAndThePressOnTheFieldBringsItBack() {
        var focus = TypingFocus()
        focus.began(field)
        XCTAssertTrue(focus.editing)
        XCTAssertTrue(focus.hides(.elsewhere), "the chat's ground, the panel, a button: the keyboard goes")
        XCTAssertTrue(focus.hides(.music), "the score behind: the keyboard goes")
        focus.ended(field)
        XCTAssertFalse(focus.editing)
        // Pressed again, the field takes the focus; the iPad brings the keyboard.
        focus.began(field)
        XCTAssertTrue(focus.editing)
    }

    func testTheFieldItsRowAnotherFieldAndTheSystemsMenuKeepTheKeyboard() {
        var focus = TypingFocus()
        focus.began(field)
        XCTAssertFalse(focus.hides(.editor), "a press in the field moves the caret")
        XCTAssertFalse(focus.hides(.keeper), "Send and the microphone keep the keyboard up")
        XCTAssertFalse(focus.hides(.otherField), "another field takes the keyboard itself")
        XCTAssertFalse(focus.hides(.systemMenu), "Paste must reach the field")
    }

    func testWhileTypingTheMusicTakesNoPressSoTheTapThatHidesTheKeyboardDoesNothingElse() {
        var focus = TypingFocus()
        focus.began(field)
        XCTAssertTrue(focus.shieldsMusic, "no selection change and no playhead move from the press that hides the keyboard")
        focus.ended(field)
        XCTAssertFalse(focus.shieldsMusic, "the next press is the music's")
    }

    func testAnotherFieldTakingTheFocusKeepsTypingWhicheverOrderTheIPadTellsIt() {
        var focus = TypingFocus()
        focus.began(field)
        // The new field begins before the old one ends.
        focus.began(other)
        focus.ended(field)
        XCTAssertTrue(focus.editing)
        XCTAssertTrue(focus.shieldsMusic)
        focus.ended(other)
        XCTAssertFalse(focus.editing)

        // The old one ends first, then the new one begins.
        focus.began(field)
        focus.ended(field)
        focus.began(other)
        XCTAssertTrue(focus.editing)
    }

    func testThePillRisesOnlyAboveAHiddenKeyboardsBar() {
        let touch = CGFloat.ds("h-touch")
        XCTAssertFalse(KeyboardLift.isBar(nil))
        XCTAssertFalse(KeyboardLift.isBar(.zero))
        // iPadOS's bar of a hidden keyboard (the language, Undo): a strip at the bottom.
        XCTAssertTrue(KeyboardLift.isBar(CGRect(x: 0, y: 1310, width: 1024, height: 56)))
        // The whole keyboard: the screen ignores it and the pill stays.
        XCTAssertFalse(KeyboardLift.isBar(CGRect(x: 0, y: 1000, width: 1024, height: touch * 8)))
        let pill = CGRect(x: 300, y: 1290, width: 420, height: 52)
        let bar = CGRect(x: 0, y: 1310, width: 1024, height: 56)
        XCTAssertEqual(KeyboardLift.lift(of: pill, keyboard: bar, gap: 12), pill.maxY - bar.minY + 12)
    }
}
