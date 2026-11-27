import Combine
import UIKit

/// The profile being edited (Andrei, 2026-11-06): the name typed and the photo chosen, from the profile the account
/// has, and their save with the account. `ProfileEditor` shows it and sends the player's taps here.
@MainActor
final class ProfileController: ObservableObject {
    /// What the editor says under it: a picture that could not be read, or why the profile was not saved.
    enum Message: Equatable {
        case photoUnreadable
        case notSaved(String)
    }

    /// The longest name kept.
    static let longestName = 60

    /// The name typed, cut at the longest name kept.
    @Published var name: String {
        didSet {
            if name.count > Self.longestName { name = String(name.prefix(Self.longestName)) }
        }
    }
    /// The photo chosen, as a data URL of its middle square; nil for none.
    @Published private(set) var photo: String?
    @Published private(set) var message: Message?
    @Published private(set) var saving = false

    /// Keeps the name (nil clears it) and the photo with the account; nil when kept, else why not in the player's words.
    private let keep: @MainActor (String?, String?) async -> String?

    init(profile: PlayerProfile?, keep: @escaping @MainActor (String?, String?) async -> String?) {
        self.keep = keep
        name = profile?.name ?? ""
        photo = profile?.photo
    }

    /// The picture `load` reads, cut to its middle square; a picture that cannot be read says so.
    func choosePhoto(_ load: () async -> Data?) async {
        let data = await load()
        if let data, let image = UIImage(data: data), let square = PlayerProfile.squarePhoto(image) {
            photo = square
            message = nil
        } else {
            message = .photoUnreadable
        }
    }

    func removePhoto() {
        photo = nil
    }

    /// Saves the name (none when it is only spaces) and the photo; true once kept.
    func save() async -> Bool {
        saving = true
        defer { saving = false }
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        if let problem = await keep(trimmed.isEmpty ? nil : trimmed, photo) {
            message = .notSaved(problem)
            return false
        }
        return true
    }
}
