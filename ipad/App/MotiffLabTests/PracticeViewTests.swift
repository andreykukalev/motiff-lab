import XCTest

@testable import MotiffLab

/// Practice view (#533 11, PracticeView.swift), the web app's rule (Scripts/layout/practice-view.ts) on the iPad: a tap showing
/// the controls for a few seconds, and what shows in each state; since #582 item 10 the selection controls and the playback
/// controls are two visibilities, each changed without the other, and since v0.6.4 (spec 9) only their own two switches set
/// them (no press hides or shows both), kept on the device.
final class PracticeViewTests: XCTestCase {
    private let start = Date(timeIntervalSinceReferenceDate: 1000)

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

    func testExpandShowsTheNormalWorkspaceAndLeavingItShowsPracticeViewAgain() {
        let on = PracticeView(on: true, selectionHidden: true)
        XCTAssertEqual(on.shown(at: start, focused: true), .off, "Expand has its own bar and pill")
        XCTAssertTrue(on.selectionShown(focused: true))
        XCTAssertEqual(on.shown(at: start, focused: false), .on, "left, practice view is as it was")
        XCTAssertFalse(on.selectionShown(focused: false), "left, the selection controls are hidden again")
        XCTAssertEqual(PracticeView().shown(at: start, focused: false), .off)
    }

    func testPracticeViewHidesTheMenuThePillTheBluePlayheadAndTheHintsAndKeepsTheBlackPlayhead() {
        let normal = PracticeView.chrome(.off)
        XCTAssertEqual(normal, PracticeView.Chrome(menu: true, transport: true, selectionHead: true, pieceHead: true, loopStrip: true, hints: true, controls: false))
        // Both switches off: both groups hidden.
        let on = PracticeView(on: true, selectionHidden: true).chrome(at: start, focused: false)
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
                XCTAssertEqual(view.hiddenKeys.count, (selection ? 0 : 1) + (playback ? 0 : 1), combination)
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

    /// v0.6.4 spec 9: each switch, turned any number of times in any order, ends as it was last set, whatever the other
    /// one did; nothing turns either off on its own (the reported "unchecks itself").
    func testEachSwitchKeepsWhatItWasLastSet() {
        var view = PracticeView()
        var selection = true
        var playback = true
        let presses: [(Bool, Bool)] = [(true, false), (false, false), (true, true), (false, true), (true, false), (true, false), (false, true), (false, false), (true, true)]
        for (isSelection, shown) in presses {
            view = view.after(isSelection ? .selection(shown: shown) : .playback(shown: shown), at: start)
            if isSelection { selection = shown } else { playback = shown }
            // A tap on the page, a touch and the controls' time between presses change neither.
            view = view.after(.tap, at: start).after(.touch, at: start).after(.hide, at: start)
            XCTAssertEqual(!view.selectionHidden, selection)
            XCTAssertEqual(!view.on, playback)
            let chrome = view.chrome(at: start, focused: false)
            XCTAssertEqual(chrome.menu, selection, "the switch says what shows")
            XCTAssertEqual(chrome.transport, playback, "the switch says what shows")
        }
    }

    /// v0.6.4 spec 9: the two switches are kept on the device, so a Notes screen made again (PDF ↔ Notes, the roll, another
    /// piece) shows them as the player left them; both shown the first time; practice view's moment of controls not kept.
    func testTheTwoVisibilitiesAreKeptOnTheDevice() throws {
        let defaults = try XCTUnwrap(UserDefaults(suiteName: "PracticeViewTests"))
        defaults.removePersistentDomain(forName: "PracticeViewTests")
        XCTAssertEqual(PracticeView.saved(in: defaults), PracticeView(), "both shown the first time")
        for selection in [true, false] {
            for playback in [true, false] {
                let view = PracticeView().after(.selection(shown: selection), at: start).after(.playback(shown: playback), at: start).after(.tap, at: start)
                view.keep(in: defaults)
                XCTAssertEqual(PracticeView.saved(in: defaults), PracticeView(on: !playback, selectionHidden: !selection), "selection \(selection), playback \(playback)")
            }
        }
        defaults.removePersistentDomain(forName: "PracticeViewTests")
    }

    /// v0.6.4 spec 9: the switches have words in both languages, and the button says which groups are hidden.
    func testTheSwitchesHaveWordsAndTheButtonNamesWhatIsHidden() {
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            for key in ["practice.list", "practice.selectionControls", "practice.playbackControls", "practice.listHint", "practice.switchesHint", "practice.leave"] {
                XCTAssertNotEqual(text[key], key, "\(key) in \(language)")
            }
        }
        XCTAssertEqual(PracticeView().hiddenKeys, [])
        XCTAssertEqual(PracticeView(on: true, selectionHidden: true).hiddenKeys, ["practice.selectionControls", "practice.playbackControls"])
        XCTAssertEqual(PracticeView(selectionHidden: true).hiddenKeys, ["practice.selectionControls"])
    }
}
