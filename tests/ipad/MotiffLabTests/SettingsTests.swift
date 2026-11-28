import XCTest

@testable import MotiffLab

/// The Settings feature on its model, controller and views (W2.12, v1.6 i-14): the device's settings changed one after
/// another through music-core, and the profile's controller (the name cut and trimmed, the photo, the save); the
/// robustness rules of v1.6 i-14 (docs/ui-edge-cases.md, E-settings-1 to 3).
@MainActor
final class SettingsTests: XCTestCase {
    /// What the account was asked to keep and what it answers.
    private final class Saves {
        var names: [String?] = []
        var problem: String?
    }

    private func freshStore() throws -> UserDefaults {
        try XCTUnwrap(UserDefaults(suiteName: "SettingsTests-\(UUID().uuidString)"))
    }

    /// A picture of one colour, as the photo library hands it.
    private func picture(_ colour: UIColor) throws -> Data {
        let format = UIGraphicsImageRendererFormat()
        format.scale = 1
        let image = UIGraphicsImageRenderer(size: CGSize(width: 40, height: 30), format: format).image { context in
            colour.setFill()
            context.fill(CGRect(x: 0, y: 0, width: 40, height: 30))
        }
        return try XCTUnwrap(image.pngData())
    }

    private func square(_ data: Data) throws -> String {
        try XCTUnwrap(PlayerProfile.squarePhoto(XCTUnwrap(UIImage(data: data))))
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
        let saves = Saves()
        let profile = ProfileController(profile: PlayerProfile(name: "Андрей", photo: nil)) { name, _ in
            saves.names.append(name)
            return saves.problem
        }
        XCTAssertEqual(profile.name, "Андрей", "the editor starts from the account's profile")
        profile.name = String(repeating: "я", count: ProfileController.longestName + 10)
        XCTAssertEqual(profile.name.count, ProfileController.longestName)

        profile.name = "   "
        let cleared = await profile.save()
        XCTAssertTrue(cleared)
        XCTAssertEqual(saves.names, [nil], "only spaces: no name")

        saves.problem = "Не сохранилось."
        profile.name = "  Андрей "
        let saved = await profile.save()
        XCTAssertFalse(saved)
        XCTAssertEqual(saves.names, [nil, "Андрей"])
        XCTAssertEqual(profile.message, .notSaved("Не сохранилось."))
        XCTAssertFalse(profile.saving)
    }

    /// E-settings-2: Save tapped again while the profile is on its way saves it once.
    func testSaveTappedTwiceSavesOnce() async throws {
        let saves = Saves()
        // Only the first save is held on its way, until the test lets it go; a let-go given early is kept. A second save
        // goes through at once, so it fails the assertions below rather than waiting on the same stream.
        let (held, letGo) = AsyncStream<Void>.makeStream()
        let profile = ProfileController(profile: PlayerProfile(name: "Андрей", photo: nil)) { name, _ in
            saves.names.append(name)
            if saves.names.count == 1 {
                for await _ in held { break }
            }
            return nil
        }

        let first = Task { await profile.save() }
        _ = await profile.$saving.values.first { $0 }
        let second = await profile.save()
        XCTAssertFalse(second, "the second tap saves nothing")
        XCTAssertEqual(saves.names, ["Андрей"])

        letGo.yield()
        let firstSaved = await first.value
        XCTAssertTrue(firstSaved)
        XCTAssertFalse(profile.saving)
        XCTAssertEqual(saves.names, ["Андрей"], "kept once")
    }

    /// E-settings-3: a photo chosen, or the photo removed, while another still loads wins over it; a picture that cannot
    /// be read says so and keeps the photo.
    func testThePhotoChosenLastIsShown() async throws {
        let profile = ProfileController(profile: nil) { _, _ in nil }
        let red = try picture(.red)
        let blue = try picture(.blue)

        await profile.choosePhoto {
            await profile.choosePhoto { blue }
            return red
        }
        XCTAssertEqual(profile.photo, try square(blue), "the later choice shows, the earlier one coming after it is left")
        XCTAssertNotEqual(profile.photo, try square(red))

        await profile.choosePhoto {
            profile.removePhoto()
            return red
        }
        XCTAssertNil(profile.photo, "removed while it loaded: no photo")

        await profile.choosePhoto { red }
        XCTAssertEqual(profile.photo, try square(red))
        XCTAssertNil(profile.message)
        await profile.choosePhoto { Data("not a picture".utf8) }
        XCTAssertEqual(profile.message, .photoUnreadable)
        XCTAssertEqual(profile.photo, try square(red), "the photo before stays")
    }

    /// «Управление на листе» went in v1.7 e-1: its two kept switches leave the device once on launch.
    func testTheOldPageControlsSwitchesAreForgotten() throws {
        let store = try freshStore()
        for key in PageControlsShown.keys { store.set(false, forKey: key) }
        PageControlsShown.forget(store)
        for key in PageControlsShown.keys { XCTAssertNil(store.object(forKey: key), key) }
        XCTAssertEqual(PageControlsShown.keys, ["score.selectionControlsShown", "score.playbackControlsShown"], "the keys PracticeView kept")
    }
}
