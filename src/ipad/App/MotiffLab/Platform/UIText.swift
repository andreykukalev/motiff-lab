import Foundation

/// The interface's words, from the shared string files (src/shared/wwwroot/i18n/*.json, copied into the app as i18n/: what
/// the pages and more than one feature folder say, so both apps say the same thing, 0018 section 14) and each feature
/// folder's own (src/ipad/App/MotiffLab/<Folder>/Words/<Folder>.en.json, .ru.json, at the app's top), in the language the
/// player picked in Settings, else the iPad's. Musical text never comes from here: music-core writes it in the language
/// the app asks for, this one.
struct UIText: Sendable {
    let language: String
    private let words: [String: String]
    private let fallback: [String: String]

    /// The languages the interface speaks, music-core's choices for `appearance.language`, Russian first.
    static let languages = ["ru", "en"]
    /// Where the player's own choice is kept on this iPad; nothing is kept until they pick one (Andrei, 2026-11-15).
    static let languageKey = "motiff.language"

    /// The words in the interface's language now. Each screen takes them as it is made, so a new choice builds the screens
    /// again (InterfaceLanguage).
    static var current: UIText { of(language()) }

    /// The interface's language: the one picked in Settings and kept in `store`, else Russian when the iPad prefers it,
    /// else English.
    static func language(in store: UserDefaults = .standard, preferred: [String] = Locale.preferredLanguages) -> String {
        if let chosen = store.string(forKey: languageKey), languages.contains(chosen) { return chosen }
        return preferred.first?.hasPrefix("ru") == true ? "ru" : "en"
    }

    /// The words of one language, read from the app once.
    static func of(_ language: String) -> UIText { language == "ru" ? russian : english }

    private static let english = UIText(language: "en")
    private static let russian = UIText(language: "ru")

    init(language: String, bundle: Bundle = .main) {
        self.language = language
        words = Self.load(language, bundle)
        fallback = language == "en" ? [:] : Self.load("en", bundle)
    }

    /// The text for a key; the key itself when neither language has it, so a missing string shows.
    subscript(key: String) -> String {
        words[key] ?? fallback[key] ?? key
    }

    /// The text for a key with its {0}, {1}, … filled in, as the web app's Text.Format does.
    func format(_ key: String, _ values: String...) -> String {
        var result = self[key]
        for (index, value) in values.enumerated() {
            result = result.replacingOccurrences(of: "{\(index)}", with: value)
        }
        return result
    }

    /// The text for a count, in the plural form the language takes: "library.ideas.one" or ".many" in English, and
    /// ".one", ".few" or ".many" in Russian, as the web app's Text.Count does.
    func count(_ key: String, _ number: Int) -> String {
        format("\(key).\(Self.pluralForm(language, number))", String(number))
    }

    static func pluralForm(_ language: String, _ count: Int) -> String {
        guard language == "ru" else { return count == 1 ? "one" : "many" }
        let tens = count % 100
        let units = count % 10
        if units == 1 && tens != 11 { return "one" }
        if (2...4).contains(units) && !(12...14).contains(tens) { return "few" }
        return "many"
    }

    /// The words of one language: the shared file, then the folders' files by name, merged; a key is in one file only
    /// (tests/shared/i18n.test.mjs), and one in two asserts in Debug builds, the first file's text standing.
    private static func load(_ language: String, _ bundle: Bundle) -> [String: String] {
        let folders = (bundle.urls(forResourcesWithExtension: "json", subdirectory: nil) ?? [])
            .filter { $0.lastPathComponent.hasSuffix(".\(language).json") }
            .sorted { $0.lastPathComponent < $1.lastPathComponent }
        let shared = bundle.url(forResource: language, withExtension: "json", subdirectory: "i18n")
        let files = (shared.map { [$0] } ?? []) + folders
        var words: [String: String] = [:]
        var fileOf: [String: String] = [:]
        for url in files {
            for (key, text) in read(url) {
                if let first = fileOf[key] {
                    assertionFailure("the text \(key) is in \(first) and in \(url.lastPathComponent)")
                    continue
                }
                words[key] = text
                fileOf[key] = url.lastPathComponent
            }
        }
        return words
    }

    private static func read(_ url: URL) -> [String: String] {
        guard let data = try? Data(contentsOf: url),
              let object = try? JSONSerialization.jsonObject(with: data) as? [String: String]
        else { return [:] }
        return object
    }
}
