import XCTest

@testable import MotiffLab

/// The Settings feature on its model, controller and views (W2.12, v1.6 i-14): the device's settings changed one after
/// another through music-core, and the profile's controller (the name cut and trimmed, the save); the robustness rules
/// of v1.6 i-14 (docs/ui-edge-cases.md, E-settings-1 to 3).
@MainActor
final class SettingsTests: XCTestCase {
    private func freshStore() throws -> UserDefaults {
        try XCTUnwrap(UserDefaults(suiteName: "SettingsTests-\(UUID().uuidString)"))
    }

    /// E-settings-1: switches changed in a row are each kept, and one turned off and on again at once ends on.
    func testChangesInARowAreEachKept() async throws {
        let store = try freshStore()
        let settings = DeviceSettings(store: store)
        await settings.load()
        for id in ["layers.notes", "layers.bookmarks", "layers.ink"] {
            XCTAssertEqual(settings[id], "on", "Practising shows \(id)")
        }

        settings.set("layers.notes", "off")
        settings.set("layers.bookmarks", "off")
        settings.set("layers.ink", "off")
        settings.set("layers.ink", "on")
        // A change asked after them comes after them.
        await settings.update([:])
        XCTAssertEqual(settings["layers.notes"], "off")
        XCTAssertEqual(settings["layers.bookmarks"], "off", "the second change kept the first")
        XCTAssertEqual(settings["layers.ink"], "on", "turned off and on again, on")

        let again = DeviceSettings(store: store)
        await again.load()
        XCTAssertEqual(again["layers.notes"], "off", "both kept on the device")
        XCTAssertEqual(again["layers.bookmarks"], "off")
        XCTAssertEqual(again["layers.ink"], "on")
    }

    /// The name is cut at the longest kept; only spaces clear it; a profile not kept says why and stays open.
    func testTheNameIsCutAndOnlySpacesClearIt() async {
        var kept: [String?] = []
        var problem: String?
        let profile = ProfileController(profile: PlayerProfile(name: "Андрей", photo: nil)) { name, _ in
            kept.append(name)
            return problem
        }
        XCTAssertEqual(profile.name, "Андрей", "the editor starts from the account's profile")
        profile.name = String(repeating: "я", count: ProfileController.longestName + 10)
        XCTAssertEqual(profile.name.count, ProfileController.longestName)

        profile.name = "   "
        let cleared = await profile.save()
        XCTAssertTrue(cleared)
        XCTAssertEqual(kept, [nil], "only spaces: no name")

        problem = "Не сохранилось."
        profile.name = "  Андрей "
        let saved = await profile.save()
        XCTAssertFalse(saved)
        XCTAssertEqual(kept, [nil, "Андрей"])
        XCTAssertEqual(profile.message, .notSaved("Не сохранилось."))
        XCTAssertFalse(profile.saving)
    }
}
