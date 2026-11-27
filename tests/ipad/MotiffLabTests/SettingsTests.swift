import XCTest

@testable import MotiffLab

/// The Settings feature on its model, controller and views (W2.12, v1.6 i-14): the profile's controller (the name cut
/// and trimmed, the save).
@MainActor
final class SettingsTests: XCTestCase {
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
