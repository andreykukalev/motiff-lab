import SwiftUI

/// The interface's language on this iPad (Andrei, 2026-11-15): Русский or English, picked in Settings · Appearance
/// (music-core's setting `appearance.language`) and kept on the device; until the player picks one, the iPad's own. The
/// whole interface and everything the app asks music-core and the assistant to write in a language follow it.
/// The screens and their controllers take their words as they are made, so a new choice builds the interface again under
/// the new language (`id`): the piece on screen is first saved with its place, as for PDF | Notes, closed, and opened
/// again where it was once the library is built in the new language.
@MainActor
final class InterfaceLanguage: ObservableObject {
    static let shared = InterfaceLanguage()

    /// The piece to open again in the new language: its project file and where it lands.
    struct Reopen: Equatable {
        var file: URL
        var landing: Landing
    }

    /// The language the interface is built in; the app builds its screens again when it changes.
    @Published private(set) var id: String

    private let store: UserDefaults
    /// The piece's screen shown now and how it hands its piece over; nil outside a piece.
    private var screen: (piece: Int32, handOver: @MainActor () async -> Reopen?)?
    private var reopen: Reopen?
    private var changing = false

    init(store: UserDefaults = .standard, preferred: [String] = Locale.preferredLanguages) {
        self.store = store
        id = UIText.language(in: store, preferred: preferred)
    }

    /// A piece's screen came: it saves its piece and says where it opens again, when the language changes.
    func shows(piece: Int32, handOver: @escaping @MainActor () async -> Reopen?) {
        screen = (piece, handOver)
    }

    /// A piece's screen went; another that came meanwhile stays.
    func left(piece: Int32) {
        if screen?.piece == piece { screen = nil }
    }

    /// The player picked a language: kept on the device; another than the one shown builds the interface again in it, once
    /// the piece on screen is saved.
    func choose(_ language: String) async {
        guard UIText.languages.contains(language), !changing else { return }
        guard language != id else {
            // The iPad's language picked as the player's own: kept, so it stays when the iPad's changes.
            store.set(language, forKey: UIText.languageKey)
            return
        }
        changing = true
        defer { changing = false }
        let handOver = screen?.handOver
        screen = nil
        reopen = await handOver?()
        store.set(language, forKey: UIText.languageKey)
        id = language
    }

    /// The piece to open again, once: the library takes it as it is built in the new language.
    func takeReopen() -> Reopen? {
        defer { reopen = nil }
        return reopen
    }
}

extension View {
    /// A piece's screen hands its piece over while it shows, when the interface's language changes; nil hands nothing over.
    @MainActor
    func handsOver(piece: Int32?, _ handOver: @escaping @MainActor () async -> InterfaceLanguage.Reopen?) -> some View {
        onAppear {
            if let piece { InterfaceLanguage.shared.shows(piece: piece, handOver: handOver) }
        }
        .onDisappear {
            if let piece { InterfaceLanguage.shared.left(piece: piece) }
        }
    }
}
