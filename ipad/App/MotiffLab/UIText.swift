import Foundation

/// The interface's words, from the web app's own string files (src/MotiffLab.Web/wwwroot/i18n/*.json, copied into the
/// app as i18n/), so both apps say the same thing (0018 section 14). Russian when the iPad prefers it, else English.
/// Musical text never comes from here: music-core writes it in the piece's language.
struct UIText: Sendable {
    let language: String
    private let words: [String: String]
    private let fallback: [String: String]

    static let current = UIText(language: Locale.preferredLanguages.first?.hasPrefix("ru") == true ? "ru" : "en")

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

    private static func load(_ language: String, _ bundle: Bundle) -> [String: String] {
        guard let url = bundle.url(forResource: language, withExtension: "json", subdirectory: "i18n"),
              let data = try? Data(contentsOf: url),
              let object = try? JSONSerialization.jsonObject(with: data) as? [String: String]
        else { return [:] }
        return object
    }
}
