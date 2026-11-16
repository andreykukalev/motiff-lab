import XCTest

@testable import MotiffLab

/// Selection by where you touch on the roll (#601): «Рамка | Лассо» kept on the device and
/// shown only in Select, what the roll page is told, and what it says back (notes picked, picked notes moved).
final class RollPlaceTests: XCTestCase {
    func testTheBoxOrTheLassoIsKeptOnTheDeviceTheBoxFirst() throws {
        let suite = "RollPlaceTests.shape"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
        defaults.removePersistentDomain(forName: suite)
        defer { defaults.removePersistentDomain(forName: suite) }
        XCTAssertEqual(RollPickShape.saved(in: defaults), .box, "the box until the player chooses")
        RollPickShape.lasso.keep(in: defaults)
        XCTAssertEqual(RollPickShape.saved(in: defaults), .lasso)
        defaults.set("triangle", forKey: RollPickShape.key)
        XCTAssertEqual(RollPickShape.saved(in: defaults), .box, "an unknown value is the box")
        XCTAssertEqual(RollPickShape.allCases, [.box, .lasso])
    }

    func testTheSwitchShowsOnlyInSelect() {
        XCTAssertTrue(RollPickShape.shown(tool: .select, rollMode: .navigate))
        XCTAssertFalse(RollPickShape.shown(tool: .navigate, rollMode: .navigate))
        XCTAssertFalse(RollPickShape.shown(tool: .pen, rollMode: .navigate))
        XCTAssertFalse(RollPickShape.shown(tool: .select, rollMode: .edit), "Edit notes adds notes, no box")
    }

    func testTheSwitchSpeaksBothLanguages() {
        for language in ["ru", "en"] {
            let text = UIText(language: language)
            for key in RollPickShape.allCases.map(\.label) + ["rollPick.label", "rollPick.hint"] {
                XCTAssertNotEqual(text[key], key, "\(key) in \(language)")
            }
        }
        XCTAssertEqual(UIText(language: "ru")[RollPickShape.box.label], "Рамка")
        XCTAssertEqual(UIText(language: "ru")[RollPickShape.lasso.label], "Лассо")
        XCTAssertEqual(UIText(language: "en")[RollPickShape.lasso.label], "Lasso")
    }

    func testThePageIsToldThePlaceRuleIsOnAndWhetherAStylusIsUsed() {
        let page = RollInput(stylus: true, mode: .navigate, tool: .select).page
        let told = RollPlaceInput(stylus: true).added(to: page)
        XCTAssertEqual(told["place"] as? Bool, true)
        XCTAssertEqual(told["stylus"] as? Bool, true)
        XCTAssertEqual(told["mode"] as? String, "select", "RollInput's own fields stay")
        XCTAssertEqual(RollPlaceInput(stylus: false).added(to: page)["place"] as? Bool, true, "always, since v0.6.3")
    }

    func testNotesPickedComeWithHowTheyChangeTheSelectionAndTheirGesture() {
        let replaced = RollNotesPick(["ids": ["n1", "n2"], "change": "replace", "gesture": "g-1"] as [String: Any])
        XCTAssertEqual(replaced, RollNotesPick(ids: ["n1", "n2"], change: .replace, gesture: "g-1"))
        XCTAssertEqual(replaced?.replaces, true, "a box from outside replaces: the toast and a step back")
        XCTAssertEqual(replaced?.add, false)
        let added = RollNotesPick(["ids": ["n3"], "change": "add"] as [String: Any])
        XCTAssertEqual(added?.add, true, "a box from inside adds")
        XCTAssertEqual(added?.replaces, false)
        let narrowed = RollNotesPick(["ids": ["n1"], "change": "edit"] as [String: Any])
        XCTAssertEqual(narrowed?.replaces, false, "an edge narrowing picked notes is an edit, no toast")
        XCTAssertEqual(narrowed?.add, false)
        XCTAssertEqual(RollNotesPick(["ids": ["n1"]] as [String: Any])?.change, .create, "an unknown change is a new selection")
        XCTAssertNil(RollNotesPick(["ids": [String]()] as [String: Any]), "no notes, no pick")
        XCTAssertNil(RollNotesPick(nil))
    }

    func testPickedNotesMovedAreChangesOnTheDraft() {
        let moved = RollNoteEdit.moved([
            ["id": "n1", "onset": 600, "pitch": 73, "length": 480],
            ["id": "n2", "onset": 1080, "pitch": 75, "length": 240],
            ["id": "n3", "pitch": 75],
        ] as [[String: Any]])
        XCTAssertEqual(moved, [.change(id: "n1", onset: 600, pitch: 73, length: 480), .change(id: "n2", onset: 1080, pitch: 75, length: 240)], "a malformed entry is left out")
        XCTAssertEqual(RollNoteEdit.moved(nil), [])
    }
}
