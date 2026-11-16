import XCTest

@testable import MotiffLab

/// The interface's language picked in Settings (Andrei, 2026-11-15): Русский or English, kept on the device; until one is
/// picked, the iPad's; a new one builds the interface again, the piece on screen handed over to open where it was.
final class InterfaceLanguageTests: XCTestCase {
    private let suite = "motiff-language-test"

    private func freshStore() throws -> UserDefaults {
        let store = try XCTUnwrap(UserDefaults(suiteName: suite))
        store.removePersistentDomain(forName: suite)
        return store
    }

    func testWithoutAChoiceTheInterfaceFollowsTheIPad() throws {
        let store = try freshStore()
        XCTAssertEqual(UIText.language(in: store, preferred: ["ru-RU", "en-GB"]), "ru")
        XCTAssertEqual(UIText.language(in: store, preferred: ["en-GB", "ru-RU"]), "en")
        XCTAssertEqual(UIText.language(in: store, preferred: ["de-DE"]), "en", "a language the app does not speak: English")
        XCTAssertEqual(UIText.language(in: store, preferred: []), "en")
        // Something kept that is no language of the app is no choice.
        store.set("fr", forKey: UIText.languageKey)
        XCTAssertEqual(UIText.language(in: store, preferred: ["ru"]), "ru")
    }

    @MainActor
    func testTheLanguagePickedWinsOverTheIPadsAndIsKept() async throws {
        let store = try freshStore()
        let language = InterfaceLanguage(store: store, preferred: ["en-US"])
        XCTAssertEqual(language.id, "en")

        await language.choose("ru")
        XCTAssertEqual(language.id, "ru")
        XCTAssertEqual(store.string(forKey: UIText.languageKey), "ru")
        XCTAssertEqual(UIText.language(in: store, preferred: ["en-US"]), "ru")
        // The next launch starts in it, whatever the iPad's language.
        XCTAssertEqual(InterfaceLanguage(store: store, preferred: ["en-US"]).id, "ru")

        await language.choose("de")
        XCTAssertEqual(language.id, "ru", "only the app's languages are picked")
        XCTAssertEqual(store.string(forKey: UIText.languageKey), "ru")
    }

    @MainActor
    func testTheIPadsLanguagePickedIsKeptWithoutBuildingAgain() async throws {
        let store = try freshStore()
        let language = InterfaceLanguage(store: store, preferred: ["ru-RU"])
        var handed = 0
        language.shows(piece: 7) {
            handed += 1
            return nil
        }
        await language.choose("ru")
        XCTAssertEqual(language.id, "ru")
        XCTAssertEqual(handed, 0, "the language shown already: the piece stays on screen")
        XCTAssertEqual(store.string(forKey: UIText.languageKey), "ru", "kept, so it stays when the iPad's changes")
        XCTAssertEqual(InterfaceLanguage(store: store, preferred: ["en-US"]).id, "ru")
    }

    @MainActor
    func testTheOpenPieceIsHandedOverOnceAndComesBackWhereItWas() async throws {
        let store = try freshStore()
        let language = InterfaceLanguage(store: store, preferred: ["en-US"])
        let file = URL(fileURLWithPath: "/tmp/piece.motiff.json")
        var handed: [String] = []
        language.shows(piece: 3) {
            // Saved before the language changes: the screens still speak the old one.
            handed.append(language.id)
            return InterfaceLanguage.Reopen(file: file, landing: .tick(1920))
        }
        await language.choose("ru")
        XCTAssertEqual(handed, ["en"])
        XCTAssertEqual(language.takeReopen(), InterfaceLanguage.Reopen(file: file, landing: .tick(1920)))
        XCTAssertNil(language.takeReopen(), "opened again once")

        // The screen handed over is gone: back to English hands nothing over.
        await language.choose("en")
        XCTAssertEqual(handed, ["en"])
        XCTAssertNil(language.takeReopen())
    }

    @MainActor
    func testOnlyTheScreenThatWentLetsGoOfTheHandOver() async throws {
        let store = try freshStore()
        let language = InterfaceLanguage(store: store, preferred: ["en-US"])
        var handed: [Int32] = []
        language.shows(piece: 1) {
            handed.append(1)
            return nil
        }
        // PDF | Notes: the next screen comes before the first one goes.
        language.shows(piece: 2) {
            handed.append(2)
            return nil
        }
        language.left(piece: 1)
        await language.choose("ru")
        XCTAssertEqual(handed, [2])

        language.shows(piece: 4) {
            handed.append(4)
            return nil
        }
        language.left(piece: 4)
        await language.choose("en")
        XCTAssertEqual(handed, [2], "back in the library, no piece to hand over")
    }

    @MainActor
    func testTheChoiceIsMusicCoresSettingAndBothLanguagesNameIt() async throws {
        let store = try freshStore()
        let settings = DeviceSettings(store: store)
        await settings.load()
        let choices = try XCTUnwrap(settings.catalogue["appearance.language"]?.choices)
        XCTAssertEqual(Set(choices), Set(UIText.languages))
        XCTAssertEqual(settings.catalogue["appearance.language"]?.scope, "device")
        for language in UIText.languages {
            let text = UIText(language: language)
            XCTAssertEqual(text["settings.language.ru"], "Русский", "each language by its own name")
            XCTAssertEqual(text["settings.language.en"], "English")
            XCTAssertNotEqual(text["settings.languagePadHint"], "settings.languagePadHint")
        }
        XCTAssertNotEqual(UIText(language: "ru")["settings.languagePadHint"], UIText(language: "en")["settings.languagePadHint"])
        XCTAssertEqual(UIText.of("ru").language, "ru")
        XCTAssertEqual(UIText.of("en").language, "en")
    }
}
