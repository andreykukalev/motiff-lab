import PhotosUI
import SwiftUI

/// The player's settings on this iPad (0018 section 10), as the web app's DeviceSettings keeps them: music-core's settings
/// state (`settings.state` on piece 0) read from the values kept in the device's preferences, one value to a setting, and
/// the presets applied by music-core. Device-only settings stay on the device (the free tier has no cloud account). The
/// score, the roll and the app's appearance read them here and follow every change at once. The language is the iPad's
/// own choice for the app (iPad Settings), as iPad apps do.
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
    /// What this store does not keep: the language is the iPad's, and saving online and analytics have their own places.
    private static let notKept: Set<String> = ["appearance.language", "account.saveOnline", "account.analytics", "score.layout"]

    private let store: UserDefaults
    private var loaded = false

    init(store: UserDefaults = .standard) {
        self.store = store
    }

    /// Reads the catalogue and the kept values, once.
    func load() async {
        guard !loaded else { return }
        loaded = true
        let answer = await MusicCore.run { MusicCore.call(piece: 0, "settings") }
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

    /// One setting changed and kept on the device.
    func set(_ id: String, _ value: String) {
        guard self[id] != value else { return }
        Task { await update(["set": ["id": id, "value": value]]) }
    }

    /// Every setting the preset governs set as it says.
    func apply(preset: String) {
        Task { await update(["preset": preset]) }
    }

    /// The values with a piece's own choices over them, for the settings a piece can keep.
    func resolved(overrides: [String: String]) -> [String: String] {
        var result = values
        for (id, value) in overrides where catalogue[id]?.scope == "piece" {
            result[id] = value
        }
        return result
    }

    /// Asks music-core for the state from the kept values and the change ("preset", or "set" {id, value}), then keeps
    /// what it answers.
    func update(_ change: [String: Any]) async {
        var request = change
        request["values"] = store.dictionary(forKey: Self.storeKey) as? [String: String] ?? [:]
        guard let data = try? JSONSerialization.data(withJSONObject: request) else { return }
        let body = String(decoding: data, as: UTF8.self)
        let answer = await MusicCore.run { MusicCore.call(piece: 0, "settings.state", body) }
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

/// The Settings sheet (0018 section 10, the web app's SettingsSheet): the sections on the left, the three presets first,
/// then a switch for each element they set; the piece's own choices listed, to clear in one tap. Only the settings the
/// iPad acts on are shown: the others join the sheet with the features they switch.
struct SettingsSheet: View {
    enum SheetSection: String, CaseIterable, Hashable {
        case guidance, score, layers, roll, appearance, account
    }

    @ObservedObject var settings: DeviceSettings
    @ObservedObject var account: Account
    let text: UIText
    /// The open piece's own choices, by id; empty outside a piece.
    var pieceOverrides: [String: String] = [:]
    var clearPiece: (() -> Void)?
    /// "‹ More" when opened from More: puts the sheet away and opens More again (testing item 20); nil from elsewhere.
    var back: (label: String, action: () -> Void)?
    @State var section = SheetSection.guidance
    @Environment(\.dismiss) private var dismiss

    private static let presets = ["learning", "practising", "clean"]
    private static let layers = ["ink", "notes", "bookmarks", "analysis", "check"]

    var body: some View {
        GeometryReader { size in
            // Landscape: the sections on the left, as in the kit (screen 32); portrait: tabs along the top.
            if size.size.width > size.size.height, size.size.width > 600 {
                HStack(alignment: .top, spacing: 0) {
                    navigation(vertical: true)
                        .frame(width: 200)
                        .padding(CGFloat.ds("s-4"))
                        .frame(maxHeight: .infinity, alignment: .top)
                        .background(Color.ds("surface-2"))
                    pane
                }
            } else {
                VStack(alignment: .leading, spacing: 0) {
                    ScrollView(.horizontal, showsIndicators: false) {
                        navigation(vertical: false).padding(CGFloat.ds("s-3"))
                    }
                    .background(Color.ds("surface-2"))
                    pane
                }
            }
        }
        .background(Color.ds("surface"))
        .modifier(PageSizedSheet())
        .task { await settings.load() }
    }

    @ViewBuilder private func navigation(vertical: Bool) -> some View {
        let layout = vertical ? AnyLayout(VStackLayout(alignment: .leading, spacing: 2)) : AnyLayout(HStackLayout(spacing: 2))
        layout {
            if vertical {
                Text(text["settings.title"])
                    .dsType(.heading)
                    .foregroundStyle(Color.ds("ink"))
                    .padding(.bottom, CGFloat.ds("s-3"))
                    .accessibilityAddTraits(.isHeader)
            }
            ForEach(SheetSection.allCases, id: \.self) { item in
                let on = item == section
                Button {
                    section = item
                } label: {
                    Text(name(item))
                        .font(DesignTokens.font(weight: on ? 600 : 500, size: 15, relativeTo: .body))
                        .foregroundStyle(on ? Color.ds("accent") : Color.ds("ink"))
                        .padding(.horizontal, CGFloat.ds("s-3"))
                        .frame(minHeight: CGFloat.ds("h-control"))
                        .frame(maxWidth: vertical ? .infinity : nil, alignment: .leading)
                        .background(on ? Color.ds("accent-tint") : .clear, in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityAddTraits(on ? .isSelected : [])
            }
        }
    }

    private var pane: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack {
                if let back {
                    BackChip(label: back.label, text: text, action: back.action)
                        .padding(.leading, -CGFloat.ds("s-2"))
                }
                Text(name(section)).overline()
                Spacer()
                Button {
                    dismiss()
                } label: {
                    Icon(name: "close").frame(width: 32, height: 32)
                }
                .buttonStyle(BarActionStyle())
                .accessibilityLabel(text["settings.close"])
            }
            .padding(.horizontal, CGFloat.ds("s-6"))
            .padding(.top, CGFloat.ds("s-4"))
            ScrollView {
                VStack(alignment: .leading, spacing: CGFloat.ds("s-5")) {
                    rows
                }
                .padding(.horizontal, CGFloat.ds("s-6"))
                .padding(.vertical, CGFloat.ds("s-4"))
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            if !pieceOverrides.isEmpty, let clearPiece {
                // The piece's own choices, made from the score: listed here and cleared in one tap.
                VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                    Text(text.format("settings.thisPiece", pieceOverrides.keys.sorted().map { overrideName($0, pieceOverrides[$0] ?? "") }.joined(separator: ", ")))
                        .dsType(.caption)
                        .foregroundStyle(Color.ds("ink-2"))
                    Button(text["settings.clearPiece"], action: clearPiece)
                        .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                }
                .padding(.horizontal, CGFloat.ds("s-6"))
                .padding(.vertical, CGFloat.ds("s-3"))
                .frame(maxWidth: .infinity, alignment: .leading)
                .overlay(alignment: .top) { Rectangle().fill(Color.ds("hairline")).frame(height: 1) }
            }
        }
    }

    @ViewBuilder private var rows: some View {
        switch section {
        case .guidance:
            SegmentedChoice(
                options: Self.presets.map { ($0, text["settings.preset.\($0)"]) },
                chosen: settings.preset,
                label: text["settings.presetLabel"]
            ) { settings.apply(preset: $0) }
            Text(text["settings.presetHint.\(settings.preset)"])
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
            switchRow("guidance.brackets", text["settings.brackets"])
            switchRow("guidance.transportPill", text["settings.pillWithSelection"], on: "selection", off: "playing", hint: text["settings.pillWithSelectionHint"])
        case .score:
            choiceRow("phrases.granularity", text["settings.granularity"], hint: text["settings.granularityHint"]) { text[Self.granularityKey($0)] }
            switchRow("playback.follow", text["settings.follow"], hint: text["settings.followHint"])
        case .layers:
            Text(text["settings.layersHow"])
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
            ForEach(Self.layers, id: \.self) { layer in
                switchRow("layers.\(layer)", text["layers.\(layer)"])
            }
        case .roll:
            switchRow("roll.noteNames", text["roll.noteNames"])
            choiceRow("notes.naming", text["settings.noteNaming"]) { text["settings.noteNaming.\($0)"] }
            choiceRow("roll.colour", text["roll.colourBy"]) { text["settings.rollColour.\($0)"] }
        case .appearance:
            choiceRow("appearance.theme", text["settings.theme"], hint: text["settings.themeHint"]) { text["settings.theme.\($0)"] }
            VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                Text(text["settings.language"]).dsType(.label).foregroundStyle(Color.ds("ink"))
                Text(text["settings.languageDevice"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                Button(text["settings.openDeviceSettings"]) {
                    if let url = URL(string: UIApplication.openSettingsURLString) { UIApplication.shared.open(url) }
                }
                .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
            }
        case .account:
            AccountPanel(account: account, text: text)
            if ScanServer.hosted(account) == nil {
                // Without Motiff Lab's server, the player's own scan server (0011), as in the web app's Settings.
                ScanPanel(scan: ScanServer.shared, text: text)
            }
        }
    }

    private func switchRow(_ id: String, _ label: String, on: String = "on", off: String = "off", hint: String? = nil) -> some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            Toggle(label, isOn: Binding(get: { settings[id] == on }, set: { settings.set(id, $0 ? on : off) }))
                .dsType(.label)
                .foregroundStyle(Color.ds("ink"))
                .tint(Color.ds("accent"))
            if let hint {
                Text(hint).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            }
        }
    }

    private func choiceRow(_ id: String, _ label: String, hint: String? = nil, name: @escaping (String) -> String) -> some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(label).dsType(.label).foregroundStyle(Color.ds("ink"))
            SegmentedChoice(options: (settings.catalogue[id]?.choices ?? []).map { ($0, name($0)) }, chosen: settings[id], label: label) { settings.set(id, $0) }
            if let hint {
                Text(hint).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            }
        }
    }

    private func name(_ section: SheetSection) -> String {
        switch section {
        case .guidance: text["settings.section.guidance"]
        case .score: text["settings.section.score"]
        case .layers: text["settings.section.layers"]
        case .roll: text["settings.section.roll"]
        case .appearance: text["settings.appearance"]
        case .account: text["settings.section.account"]
        }
    }

    static func granularityKey(_ choice: String) -> String {
        switch choice {
        case "short": "phrases.levelShort"
        case "sentences": "phrases.levelSentences"
        default: "phrases.levelPhrases"
        }
    }

    /// "Phrases: Short" or "Bookmarks: hidden".
    private func overrideName(_ id: String, _ value: String) -> String {
        if id == "phrases.granularity" { return "\(text["phrases.levelTitle"]): \(text[Self.granularityKey(value)])" }
        if id.hasPrefix("layers.") { return "\(text[id]): \(text[value == "off" ? "settings.hidden" : "settings.shown"])" }
        return id
    }
}

/// The account and its plan, in the Settings sheet's Account section, as on the web app's Settings page, with the same
/// words (settings.*, account.*). Testers switch between Pro and no Pro here.
struct AccountPanel: View {
    @ObservedObject var account: Account
    let text: UIText
    @State private var editing = false

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-4")) {
            if !account.isAvailable {
                Text(text["settings.accountWhy"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            } else if let player = account.player {
                Text(text["account.title"]).dsType(.label).foregroundStyle(Color.ds("ink"))
                HStack(spacing: CGFloat.ds("s-3")) {
                    PlayerAvatar(name: account.shownName ?? player, image: account.profile?.image)
                    VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                        Text(account.shownName ?? player).dsType(.label).foregroundStyle(Color.ds("ink"))
                        Text(text["settings.signedIn"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                    }
                    Spacer()
                }
                .accessibilityElement(children: .combine)
                // The profile (Andrei, 2026-11-06): the name and photo the player chose, kept with the account on any plan.
                if editing {
                    ProfileEditor(account: account, text: text) { editing = false }
                } else {
                    Button(text["profile.edit"]) { editing = true }
                        .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                }
                Button(text["settings.signOut"]) { account.signOut() }
                    .buttonStyle(DSButtonStyle(kind: .danger, compact: true))
                plan
            } else {
                Text(text["settings.accountTitle"]).dsType(.label).foregroundStyle(Color.ds("ink"))
                Text(text["settings.accountWhy"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                HStack(spacing: CGFloat.ds("s-2")) {
                    Button(text["settings.signIn"]) { Task { await account.signIn() } }
                        .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                    Button(text["settings.createAccount"]) { Task { await account.signIn(create: true) } }
                        .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                }
                Text(text["settings.signInHow"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                if let problem = account.problem {
                    Text(problem).dsType(.caption).foregroundStyle(Color.ds("danger"))
                }
            }
        }
        .disabled(account.busy)
        .overlay { if account.busy { ProgressView() } }
    }

    /// The plan (0018 section 12): what runs on the device is free, what needs the cloud is Pro.
    @ViewBuilder private var plan: some View {
        HStack {
            Text(text["settings.plan"]).dsType(.label).foregroundStyle(Color.ds("ink-2"))
            Spacer()
            Text(planName).dsType(.label).foregroundStyle(account.plan?.isPro == true ? Color.ds("plum") : Color.ds("ink"))
        }
        if account.plan?.isTester == true {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                Text(text["settings.testPlan"]).dsType(.label).foregroundStyle(Color.ds("ink"))
                SegmentedChoice(
                    options: [(Plan?.none, text["settings.testPlanAccount"]), (Plan?.some(.pro), text["settings.testPlanPro"]), (Plan?.some(.free), text["settings.testPlanFree"])],
                    chosen: account.choice,
                    label: text["settings.testPlan"]
                ) { choice in Task { await account.choose(choice) } }
                Text(text["settings.testPlanHint"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            }
        }
    }

    private var planName: String {
        guard let plan = account.plan else { return text["settings.planUnknown"] }
        return plan.isPro ? text["settings.planPro"] : text["settings.planFree"]
    }
}

/// The player's picture (0018 section 9, "Account"): the photo they chose for their profile, else the first letter of
/// their name on cobalt, as the web app's avatar shows it.
struct PlayerAvatar: View {
    let name: String
    let image: UIImage?
    var size: CGFloat = 44

    var body: some View {
        Group {
            if let image {
                Image(uiImage: image).resizable().scaledToFill()
            } else {
                Text(Self.initial(name))
                    .font(DesignTokens.font(weight: 700, size: size * 0.4))
                    .foregroundStyle(Color.ds("on-accent"))
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                    .background(Color.ds("accent"))
            }
        }
        .frame(width: size, height: size)
        .clipShape(Circle())
        .accessibilityHidden(true)
    }

    /// The first letter or digit of the name, upper case; "?" when it has none (the web's AccountButton.Initial).
    static func initial(_ name: String) -> String {
        name.first(where: { $0.isLetter || $0.isNumber }).map { String($0).uppercased() } ?? "?"
    }
}

/// Editing the profile (Andrei, 2026-11-06): the name, and a photo from the library cut to its middle square, saved with
/// the account as the web app's account panel saves it.
private struct ProfileEditor: View {
    @ObservedObject var account: Account
    let text: UIText
    let done: () -> Void

    @State private var name = ""
    @State private var photo: String?
    @State private var picked: PhotosPickerItem?
    @State private var message: String?
    @State private var saving = false

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text["profile.name"]).dsType(.label).foregroundStyle(Color.ds("ink"))
            TextField(account.player ?? "", text: $name)
                .dsType(.body)
                .textFieldStyle(.roundedBorder)
                .textInputAutocapitalization(.words)
                .submitLabel(.done)
                .onChange(of: name) { _, value in
                    if value.count > 60 { name = String(value.prefix(60)) }
                }
                .accessibilityLabel(text["profile.name"])
            Text(text["profile.nameHint"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            HStack(spacing: CGFloat.ds("s-3")) {
                PlayerAvatar(name: name.isEmpty ? (account.player ?? "") : name, image: PlayerProfile.image(photo))
                PhotosPicker(selection: $picked, matching: .images) {
                    Text(text["profile.choosePhoto"])
                }
                .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                if photo != nil {
                    Button(text["profile.removePhoto"]) { photo = nil }
                        .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                }
            }
            HStack(spacing: CGFloat.ds("s-2")) {
                Button(text["profile.save"]) { Task { await save() } }
                    .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                    .disabled(saving)
                Button(text["common.cancel"], action: done)
                    .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
            }
            if let message {
                Text(message).dsType(.caption).foregroundStyle(Color.ds("danger"))
            }
        }
        .onAppear {
            name = account.profile?.name ?? ""
            photo = account.profile?.photo
        }
        .onChange(of: picked) { _, item in
            guard let item else { return }
            Task { await choose(item) }
        }
    }

    private func choose(_ item: PhotosPickerItem) async {
        if let data = try? await item.loadTransferable(type: Data.self), let image = UIImage(data: data),
           let square = PlayerProfile.squarePhoto(image) {
            photo = square
            message = nil
        } else {
            message = text["profile.photoUnreadable"]
        }
    }

    private func save() async {
        saving = true
        defer { saving = false }
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        if let problem = await account.saveProfile(name: trimmed.isEmpty ? nil : trimmed, photo: photo) {
            message = problem
        } else {
            done()
        }
    }
}

/// A sheet the size of a page on iPadOS 18 and later, wide enough in landscape for the sections on the left; the
/// system's form sheet before.
private struct PageSizedSheet: ViewModifier {
    func body(content: Content) -> some View {
        if #available(iOS 18.0, *) {
            content.presentationSizing(.page)
        } else {
            content
        }
    }
}
