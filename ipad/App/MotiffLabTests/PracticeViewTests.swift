import XCTest

@testable import MotiffLab

/// Practice view (#533 11, PracticeView.swift), the web app's rule (Scripts/layout/practice-view.ts) on the iPad: one action in
/// and out, a tap showing the controls for a few seconds, and what shows in each state; since #582 item 10 the selection
/// controls and the playback controls are two visibilities, each changed without the other.
final class PracticeViewTests: XCTestCase {
    private let start = Date(timeIntervalSinceReferenceDate: 1000)

    func testOneActionEntersAndTheSameLeavesShowingNoControls() {
        let on = PracticeView().after(.toggle, at: start)
        XCTAssertTrue(on.on)
        XCTAssertTrue(on.selectionHidden, "one press hides both groups (#582 item 10)")
        XCTAssertEqual(on.shown(at: start), .on)
        XCTAssertEqual(on.after(.toggle, at: start), PracticeView())
        XCTAssertEqual(PracticeView().after(.enter, at: start), PracticeView(on: true, selectionHidden: true))
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

    func testTheBarsButtonIsLitWhileTheMenuAndTheControlsShowAndDarkInPracticeView() {
        XCTAssertTrue(PracticeView.lit(.off), "lit: the selection menu and the controls show")
        XCTAssertFalse(PracticeView.lit(.on), "dark: practice view, they are hidden")
        XCTAssertFalse(PracticeView.lit(.reveal), "dark: practice view's own controls are not the menu")
        XCTAssertFalse(PracticeView.lit(.off, selectionShown: false), "dark: the selection controls are hidden (#582 item 10)")
    }

    func testEveryWayInAndOutMovesTheButtonWithWhatShows() {
        // The bar's button and the controls' Leave (toggle), the menu's Hide (enter), «Выйти из практики» (leave), a tap
        // and the controls' time: the button follows the one state, never a flag of its own.
        var view = PracticeView()
        let ways: [(PracticeView.Event, Bool)] = [(.toggle, false), (.tap, false), (.hide, false), (.toggle, true), (.enter, false), (.touch, false), (.leave, true), (.tap, true)]
        for (event, lit) in ways {
            view = view.after(event, at: start)
            XCTAssertEqual(view.lit(at: start, focused: false), lit, "after \(event)")
            let chrome = view.chrome(at: start, focused: false)
            XCTAssertEqual(view.lit(at: start, focused: false), chrome.menu && chrome.transport)
        }
    }

    func testExpandShowsTheNormalWorkspaceAndLeavingItShowsPracticeViewAgain() {
        let on = PracticeView(on: true)
        XCTAssertEqual(on.shown(at: start, focused: true), .off, "Expand has its own bar and pill")
        XCTAssertTrue(PracticeView.lit(on.shown(at: start, focused: true)))
        XCTAssertEqual(on.shown(at: start, focused: false), .on, "left, practice view is as it was")
        XCTAssertFalse(PracticeView.lit(on.shown(at: start, focused: false)))
        XCTAssertEqual(PracticeView().shown(at: start, focused: false), .off)
    }

    func testPracticeViewHidesTheMenuThePillTheBluePlayheadAndTheHintsAndKeepsTheBlackPlayhead() {
        let normal = PracticeView.chrome(.off)
        XCTAssertEqual(normal, PracticeView.Chrome(menu: true, transport: true, selectionHead: true, pieceHead: true, loopStrip: true, hints: true, controls: false))
        // The bar's one press: both groups hidden.
        let on = PracticeView().after(.toggle, at: start).chrome(at: start, focused: false)
        XCTAssertEqual(on, PracticeView.Chrome(menu: false, transport: false, selectionHead: false, pieceHead: true, loopStrip: false, hints: false, controls: false))
        let shown = PracticeView.chrome(.reveal, selectionShown: false)
        XCTAssertEqual(shown, PracticeView.Chrome(menu: false, transport: false, selectionHead: false, pieceHead: true, loopStrip: false, hints: false, controls: true))
    }

    /// #582 item 10: both shown, both hidden, playback without selection, selection without playback, from the list's two
    /// switches; what each group shows follows its own state only.
    func testTheTwoVisibilitiesGiveAllFourCombinations() {
        for selection in [true, false] {
            for playback in [true, false] {
                let view = PracticeView()
                    .after(.selection(shown: selection), at: start)
                    .after(.playback(shown: playback), at: start)
                let chrome = view.chrome(at: start, focused: false)
                let combination = "selection \(selection), playback \(playback)"
                XCTAssertEqual(chrome.menu, selection, combination)
                XCTAssertEqual(chrome.hints, selection, combination)
                XCTAssertEqual(chrome.transport, playback, combination)
                XCTAssertEqual(chrome.selectionHead, playback, combination)
                XCTAssertEqual(chrome.loopStrip, playback, combination)
                XCTAssertTrue(chrome.pieceHead, combination)
                XCTAssertEqual(view.lit(at: start, focused: false), selection && playback, combination)
            }
        }
    }

    /// #582 item 10: changing one visibility never changes the other, nor do a tap, a touch or the controls' time.
    func testChangingOneVisibilityNeverChangesTheOther() {
        let states = [
            PracticeView(), PracticeView(on: true), PracticeView(selectionHidden: true), PracticeView(on: true, selectionHidden: true),
            PracticeView(on: true, revealUntil: start.addingTimeInterval(2), selectionHidden: true),
        ]
        for state in states {
            for shown in [true, false] {
                XCTAssertEqual(state.after(.selection(shown: shown), at: start).on, state.on, "selection \(shown) from \(state)")
                XCTAssertEqual(state.after(.selection(shown: shown), at: start).revealUntil, state.revealUntil)
                XCTAssertEqual(state.after(.playback(shown: shown), at: start).selectionHidden, state.selectionHidden, "playback \(shown) from \(state)")
            }
            for event in [PracticeView.Event.tap, .touch, .hide] {
                XCTAssertEqual(state.after(event, at: start.addingTimeInterval(1)).selectionHidden, state.selectionHidden, "\(event) from \(state)")
            }
        }
        // The selection menu's Hide and the controls' «Выйти из практики» are one group each.
        let menuHidden = PracticeView(on: true).after(.selection(shown: false), at: start)
        XCTAssertEqual(menuHidden, PracticeView(on: true, selectionHidden: true))
        XCTAssertEqual(PracticeView(on: true, selectionHidden: true).after(.playback(shown: true), at: start), PracticeView(selectionHidden: true))
    }

    /// #582 item 10: the bar's one press shows both again whenever either is hidden, and hides both from the normal workspace.
    func testTheBarsPressShowsBothWhileEitherIsHidden() {
        for hidden in [PracticeView(on: true), PracticeView(selectionHidden: true), PracticeView(on: true, selectionHidden: true)] {
            XCTAssertFalse(hidden.lit(at: start, focused: false))
            XCTAssertEqual(hidden.after(.toggle, at: start), PracticeView(), "from \(hidden)")
        }
        XCTAssertEqual(PracticeView().after(.toggle, at: start), PracticeView(on: true, selectionHidden: true))
        // Expand has its own bar: in it the selection controls are as normal, and they are hidden again once it is left.
        XCTAssertTrue(PracticeView(selectionHidden: true).selectionShown(focused: true))
        XCTAssertFalse(PracticeView(selectionHidden: true).selectionShown(focused: false))
    }
}
