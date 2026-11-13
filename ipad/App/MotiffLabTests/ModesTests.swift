import XCTest

@testable import MotiffLab

/// The modes of version 0.5 (product spec interaction-modes): their keys, icons and words. Who writes, selects and scrolls
/// in each mode is InputRules' (StylusTests); the one playback position of 0.5.2 is tested in PlaybackPositionTests.
final class ModesTests: XCTestCase {
    func testTheModesAreTheWebAppsWithItsKeysIconsAndWords() {
        XCTAssertEqual(InteractionMode.allCases, [.navigate, .select, .edit, .pen])
        XCTAssertEqual(InteractionMode.allCases.map(\.key), ["h", "s", "e", "p"])
        XCTAssertEqual(InteractionMode.allCases.map(\.icon), ["scroll", "select", "editnote", "pen"])
        XCTAssertEqual(InteractionMode.edit.label, "mode.edit")
        XCTAssertEqual(InteractionMode.navigate.hint, "mode.navigateHint")
    }
}
