import SwiftUI

/// The player's settings on this iPad (0018 section 10), as the web app's DeviceSettings keeps them: music-core's settings
/// state (`settings.state` on piece 0) read from the values kept in the device's preferences, one value to a setting, and
/// the presets applied by music-core. Device-only settings stay on the device (the free tier has no cloud account). The
/// score, the roll and the app's appearance read them here and follow every change at once. The language is kept apart
/// (InterfaceLanguage), as only a language the player picked is kept.
@MainActor
final class DeviceSettings: ObservableObject {
    static let shared = DeviceSettings()

    /// One setting of music-core's catalogue (facade `settings`): its kind, choices, default and scope.
    struct Info: Sendable {
        var id: String
        var kind: String
        var choices: [String]
        var defaultValue: String
        var scope: String
    }

    /// Every setting's value now, by id.
    @Published private(set) var values: [String: String] = [:]
    /// "learning", "practising", "clean" or "custom".
    @Published private(set) var preset = "practising"
    @Published private(set) var catalogue: [String: Info] = [:]

    /// The preferences key that holds the kept values.
    static let storeKey = "motiff.settings"
    /// What this store does not keep: it holds every value music-core states, defaults too, and the language is kept only
    /// once picked (`UIText.languageKey`); saving online and analytics have their own places.
    private static let notKept: Set<String> = ["appearance.language", "account.saveOnline", "account.analytics", "score.layout"]

    private let store: UserDefaults
    private var loaded = false
    /// The change asked last: each change waits for the one before it, so it starts from the values that one kept
    /// (E-settings-1).
    private var lastChange: Task<Void, Never>?

    init(store: UserDefaults = .standard) {
        self.store = store
    }

    /// Reads the catalogue and the kept values, once.
    func load() async {
        guard !loaded else { return }
        loaded = true
        let answer = await MusicCore.run { PieceCore.sendWithoutPiece(.settings) }
        var infos: [String: Info] = [:]
        for entry in facadeObject(answer)?["settings"] as? [[String: Any]] ?? [] {
            guard let id = entry["id"] as? String else { continue }
            infos[id] = Info(
                id: id, kind: entry["kind"] as? String ?? "switch", choices: entry["choices"] as? [String] ?? [],
                defaultValue: entry["default"] as? String ?? "", scope: entry["scope"] as? String ?? "device"
            )
        }
        catalogue = infos
        await update([:])
    }

    subscript(id: String) -> String {
        values[id] ?? catalogue[id]?.defaultValue ?? ""
    }

    func isOn(_ id: String) -> Bool { self[id] == "on" }

    /// One setting changed and kept on the device, after the changes asked before it; nothing when it already has the
    /// value by then.
    func set(_ id: String, _ value: String) {
        queueChange { [self] in
            guard self[id] != value else { return }
            await ask(["set": ["id": id, "value": value]])
        }
    }

    /// Every setting the preset governs set as it says, after the changes asked before it.
    func apply(preset: String) {
        queueChange { [self] in await ask(["preset": preset]) }
    }

    /// The values with a piece's own choices over them, for the settings a piece can keep.
    func resolved(overrides: [String: String]) -> [String: String] {
        var result = values
        for (id, value) in overrides where catalogue[id]?.scope == "piece" {
            result[id] = value
        }
        return result
    }

    /// Asks music-core for the state from the kept values and the change ("preset", or "set" {id, value}), after the
    /// changes asked before it, then keeps what it answers.
    func update(_ change: [String: Any]) async {
        await queueChange { [self] in await ask(change) }.value
    }

    /// Runs the change once the one asked before it is done (E-settings-1: two changes in a row each read the kept
    /// values before either was kept, so the answer to the second dropped the first, and a switch turned on and off
    /// at once stayed on).
    @discardableResult
    private func queueChange(_ change: @escaping @MainActor () async -> Void) -> Task<Void, Never> {
        let before = lastChange
        let next = Task { @MainActor in
            await before?.value
            await change()
        }
        lastChange = next
        return next
    }

    /// Asks music-core for the state from the kept values and the change, then keeps what it answers.
    private func ask(_ change: [String: Any]) async {
        var request = change
        var kept = store.dictionary(forKey: Self.storeKey) as? [String: String] ?? [:]
        // The language as the interface shows it: the one picked, else the iPad's.
        kept["appearance.language"] = UIText.language(in: store)
        request["values"] = kept
        let body = facadeRequest(request)
        let answer = await MusicCore.run { PieceCore.sendWithoutPiece(.settingsState, body) }
        guard let object = facadeObject(answer), let state = object["values"] as? [String: String] else { return }
        values = state
        preset = object["preset"] as? String ?? "custom"
        store.set(state.filter { !Self.notKept.contains($0.key) }, forKey: Self.storeKey)
    }

    /// The appearance the app takes: the iPad's own, or Daylight or Night stand as chosen.
    var colorScheme: ColorScheme? {
        switch self["appearance.theme"] {
        case "daylight": .light
        case "night", "slate": .dark
        default: nil
        }
    }
}

/// The score's two visibilities of before v1.7 e-1 («Управление на листе»: the selection's controls and the playback
/// controls, #582 item 10), kept on this device under `keys`: no longer read, as «Пюпитр» lays nothing on the page to
/// hide, and taken off the device once on launch, as `SelectBy.forget` does.
enum PageControlsShown {
    static let keys = ["score.selectionControlsShown", "score.playbackControlsShown"]

    static func forget(_ defaults: UserDefaults = .standard) {
        for key in keys { defaults.removeObject(forKey: key) }
    }
}
