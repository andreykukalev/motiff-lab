import XCTest

@testable import MotiffLab

/// The modes and the two playheads of version 0.5 (product specs interaction-modes and two-playheads): how many fingers
/// scroll the score in each mode, where a tap places a playhead and what the score page is told to draw.
final class ModesTests: XCTestCase {
    func testTheModesAreTheWebAppsWithItsKeysIconsAndWords() {
        XCTAssertEqual(InteractionMode.allCases, [.navigate, .select, .edit, .pen])
        XCTAssertEqual(InteractionMode.allCases.map(\.key), ["h", "s", "e", "p"])
        XCTAssertEqual(InteractionMode.allCases.map(\.icon), ["move", "select", "editnote", "pen"])
        XCTAssertEqual(InteractionMode.edit.label, "mode.edit")
        XCTAssertEqual(InteractionMode.navigate.hint, "mode.navigateHint")
    }

    func testOneFingerScrollsInNavigateAndBeforeTheModesTwoOtherwise() {
        XCTAssertEqual(InteractionMode.scrollFingers(mode: nil, penOn: false), 1)
        XCTAssertEqual(InteractionMode.scrollFingers(mode: nil, penOn: true), 2)
        XCTAssertEqual(InteractionMode.scrollFingers(mode: .navigate, penOn: false), 1)
        XCTAssertEqual(InteractionMode.scrollFingers(mode: .select, penOn: false), 2)
        XCTAssertEqual(InteractionMode.scrollFingers(mode: .edit, penOn: false), 2)
        XCTAssertEqual(InteractionMode.scrollFingers(mode: .navigate, penOn: true), 2)
    }

    func testATapInsideTheSelectionPlacesItsPlayheadElseTheGlobalOne() {
        let selection = 960.0..<1920.0
        XCTAssertEqual(TwoPlayheads.placed(1440, selection: selection), .selection(1440))
        XCTAssertEqual(TwoPlayheads.placed(960, selection: selection), .selection(960))
        XCTAssertEqual(TwoPlayheads.placed(1920, selection: selection), .global(1920))
        XCTAssertEqual(TwoPlayheads.placed(480, selection: selection), .global(480))
        XCTAssertEqual(TwoPlayheads.placed(480, selection: nil), .global(480))
    }

    func testTheSelectionPlayheadWaitsWhereItWasLeftInsideTheSelectionElseAtItsStart() {
        var heads = TwoPlayheads()
        XCTAssertEqual(heads.selectionWaits(in: 960..<1920), 960)
        heads.selectionAt = 1440
        XCTAssertEqual(heads.selectionWaits(in: 960..<1920), 1440)
        XCTAssertEqual(heads.selectionWaits(in: 0..<960), 0, "a playhead left in another selection is not this one's")
        XCTAssertTrue(heads.loopChosen, "Loop is chosen until the player turns it off")
    }

    func testThePageIsToldWhichPlayheadHasThePlayerAndWhereTheOtherWaits() throws {
        var heads = TwoPlayheads()
        heads.globalAt = 480
        heads.selectionAt = 1440

        let piecePlays = heads.page(selection: 960..<1920, holds: false, fade: true)
        XCTAssertEqual(piecePlays["selectionHolds"] as? Bool, false)
        XCTAssertEqual(piecePlays["selection"] as? Double, 1440)
        XCTAssertEqual(piecePlays["fade"] as? Bool, true)

        let selectionPlays = heads.page(selection: 960..<1920, holds: true, fade: false)
        XCTAssertEqual(selectionPlays["selectionHolds"] as? Bool, true)
        XCTAssertEqual(selectionPlays["global"] as? Double, 480)

        let none = heads.page(selection: nil, holds: false, fade: true)
        XCTAssertTrue(none["selection"] is NSNull, "without a selection the page hides its playhead")
        XCTAssertNoThrow(try JSONSerialization.data(withJSONObject: none))
    }
}
