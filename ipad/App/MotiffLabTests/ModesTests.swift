import XCTest

@testable import MotiffLab

/// The modes of version 0.5 (product spec interaction-modes): how many fingers scroll the score in each mode. The one
/// playback position of 0.5.2 is tested in PlaybackPositionTests.
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
}
