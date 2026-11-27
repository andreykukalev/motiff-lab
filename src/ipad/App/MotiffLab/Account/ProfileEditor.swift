import PhotosUI
import SwiftUI

/// Editing the profile (Andrei, 2026-11-06): the name, and a photo from the library cut to its middle square, saved with
/// the account as the web app's account panel saves it. The draft and its save are `ProfileController`'s.
struct ProfileEditor: View {
    @ObservedObject var account: Account
    let text: UIText
    let done: () -> Void

    @StateObject private var profile: ProfileController
    @State private var picked: PhotosPickerItem?

    init(account: Account, text: UIText, done: @escaping () -> Void) {
        _account = ObservedObject(wrappedValue: account)
        self.text = text
        self.done = done
        _profile = StateObject(wrappedValue: ProfileController(profile: account.profile) { await account.saveProfile(name: $0, photo: $1) })
    }

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text["profile.name"]).dsType(.label).foregroundStyle(Color.ds("ink"))
            TextField(account.player ?? "", text: $profile.name)
                .dsType(.body)
                .modifier(FieldLook())
                .textInputAutocapitalization(.words)
                .submitLabel(.done)
                .accessibilityLabel(text["profile.name"])
            Text(text["profile.nameHint"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            HStack(spacing: CGFloat.ds("s-3")) {
                PlayerAvatar(name: profile.name.isEmpty ? (account.player ?? "") : profile.name, image: PlayerProfile.image(profile.photo))
                PhotosPicker(selection: $picked, matching: .images) {
                    Text(text["profile.choosePhoto"])
                }
                .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                if profile.photo != nil {
                    Button(text["profile.removePhoto"]) { profile.removePhoto() }
                        .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                }
            }
            HStack(spacing: CGFloat.ds("s-2")) {
                Button(text["profile.save"]) {
                    Task { if await profile.save() { done() } }
                }
                .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                .disabled(profile.saving)
                Button(text["common.cancel"], action: done)
                    .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
            }
            if let message = profile.message {
                Text(messageText(message)).dsType(.caption).foregroundStyle(Color.ds("danger"))
            }
        }
        .onChange(of: picked) { _, item in
            guard let item else { return }
            // Picked again, the same photo is a new choice (E-settings-3): the picker keeps no selection.
            picked = nil
            Task { await profile.choosePhoto { try? await item.loadTransferable(type: Data.self) } }
        }
    }

    private func messageText(_ message: ProfileController.Message) -> String {
        switch message {
        case .photoUnreadable: text["profile.photoUnreadable"]
        case .notSaved(let problem): problem
        }
    }
}
