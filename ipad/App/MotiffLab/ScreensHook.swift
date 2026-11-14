import Foundation

#if DEBUG
/// The way in for the screenshot UI test (`MotiffLabScreens`, the `Screens` scheme), in Debug builds only:
/// `-ml-screens-reset` removes the projects in Documents before the library first reads them, and
/// `-ml-screens-open <path>` then opens that file as if it was picked in Files. Read once per launch. With `-ml-screens`
/// the score also has a hidden button that chooses the next phrase, as a tap on its bracket would.
@MainActor
enum ScreensHook {
    private static var used = false
    private static var phrase = 1

    static let active = ProcessInfo.processInfo.arguments.contains("-ml-screens")

    /// Phrase 2, then 3, and so on: each press chooses another phrase, so it never lifts the chosen one into focus.
    static func nextPhrase() -> Int {
        phrase += 1
        return phrase
    }

    static func start(arguments: [String] = ProcessInfo.processInfo.arguments) -> URL? {
        guard !used else { return nil }
        used = true
        if arguments.contains("-ml-screens-reset") {
            for entry in ProjectFiles.list() { try? FileManager.default.removeItem(at: entry.url) }
        }
        guard let index = arguments.firstIndex(of: "-ml-screens-open"), arguments.indices.contains(index + 1) else { return nil }
        return URL(fileURLWithPath: arguments[index + 1])
    }
}
#endif
