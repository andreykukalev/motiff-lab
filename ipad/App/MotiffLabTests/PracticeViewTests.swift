import XCTest

@testable import MotiffLab

/// Practice view (#533 11, PracticeView.swift), the web app's rule (Scripts/layout/practice-view.ts) on the iPad: one action in
/// and out, a tap showing the controls for a few seconds, and what shows in each state.
final class PracticeViewTests: XCTestCase {
    private let start = Date(timeIntervalSinceReferenceDate: 1000)

    func testOneActionEntersAndTheSameLeavesShowingNoControls() {
        let on = PracticeView().after(.toggle, at: start)
        XCTAssertTrue(on.on)
        XCTAssertEqual(on.shown(at: start), .on)
        XCTAssertEqual(on.after(.toggle, at: start), PracticeView())
        XCTAssertEqual(PracticeView().after(.enter, at: start), PracticeView(on: true))
        XCTAssertEqual(on.after(.leave, at: start), PracticeView())
    }

    func testATapShowsTheControlsForAFewSecondsThenTheyHide() {
        let tapped = PracticeView(on: true).after(.tap, at: start)
        XCTAssertEqual(tapped.shown(at: start), .reveal)
        XCTAssertEqual(tapped.shown(at: start.addingTimeInterval(PracticeView.revealFor - 0.01)), .reveal)
        XCTAssertEqual(tapped.shown(at: start.addingTimeInterval(PracticeView.revealFor)), .on)
        XCTAssertEqual(tapped.revealLeft(at: start.addingTimeInterval(1)), PracticeView.revealFor - 1, accuracy: 0.001)
        // Another tap starts the time again: a tap still places the playhead, so it never hides them.
        let again = tapped.after(.tap, at: start.addingTimeInterval(2))
        XCTAssertEqual(again.shown(at: start.addingTimeInterval(2 + PracticeView.revealFor - 0.01)), .reveal)
    }

    func testATouchOnTheShownControlsKeepsThemAndTheirTimeOverHidesThem() {
        let tapped = PracticeView(on: true).after(.tap, at: start)
        let touched = tapped.after(.touch, at: start.addingTimeInterval(2))
        XCTAssertEqual(touched.shown(at: start.addingTimeInterval(2 + PracticeView.revealFor - 0.01)), .reveal)
        // A touch after they hid brings none back.
        let late = tapped.after(.touch, at: start.addingTimeInterval(PracticeView.revealFor + 1))
        XCTAssertEqual(late.shown(at: start.addingTimeInterval(PracticeView.revealFor + 1)), .on)
        // The timer may wake a moment early: hiding does not depend on the clock, and practice view stays.
        XCTAssertEqual(tapped.after(.hide, at: start), PracticeView(on: true))
    }

    func testOutsidePracticeViewATapOrATouchChangesNothing() {
        XCTAssertEqual(PracticeView().after(.tap, at: start), PracticeView())
        XCTAssertEqual(PracticeView().after(.touch, at: start), PracticeView())
        XCTAssertEqual(PracticeView().after(.hide, at: start), PracticeView())
        XCTAssertEqual(PracticeView().shown(at: start), .off)
        XCTAssertEqual(PracticeView().revealLeft(at: start), 0)
    }

    func testPracticeViewHidesTheMenuThePillTheBluePlayheadAndTheHintsAndKeepsTheBlackPlayhead() {
        let normal = PracticeView.chrome(.off)
        XCTAssertEqual(normal, PracticeView.Chrome(menu: true, transport: true, selectionHead: true, pieceHead: true, loopStrip: true, hints: true, controls: false))
        let on = PracticeView.chrome(.on)
        XCTAssertEqual(on, PracticeView.Chrome(menu: false, transport: false, selectionHead: false, pieceHead: true, loopStrip: false, hints: false, controls: false))
        let shown = PracticeView.chrome(.reveal)
        XCTAssertEqual(shown, PracticeView.Chrome(menu: false, transport: false, selectionHead: false, pieceHead: true, loopStrip: false, hints: false, controls: true))
    }
}
