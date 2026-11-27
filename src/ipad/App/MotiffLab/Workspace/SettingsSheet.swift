import SwiftUI

/// The Settings sheet (0018 section 10, the web app's SettingsSheet): the sections on the left, the three presets first,
/// then a switch for each element they set; the piece's own choices listed, to clear in one tap. Only the settings the
/// iPad acts on are shown: the others join the sheet with the features they switch. It shows the device's settings
/// (`DeviceSettings`) with the account's panels (Account), the scan server's (Import) and the kinds of extra
/// information (Marks), as the screens compose the features, so it is Workspace's (W2.12, v1.6 i-14).
struct SettingsSheet: View {
    enum SheetSection: String, CaseIterable, Hashable {
        case guidance, score, layers, roll, appearance, account, about
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
    private static let layers = ["ink", "notes", "bookmarks", "analysis"]

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
        // The API scales to zero: a plan or profile that did not come at launch is asked again when Settings opens.
        .task { await account.refreshIfMissing() }
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
                    SteadyWord(text: name(item), on: on, size: 15, relativeTo: .body, alignment: .leading)
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
        case .score:
            switchRow("playback.follow", text["settings.follow"], hint: text["settings.followHint"])
            switchRow("score.evenBars", text["settings.evenBars"], hint: text["settings.evenBarsHint"])
        case .layers:
            Text(text["settings.layersHow"])
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
            ForEach(Self.layers, id: \.self) { layer in
                switchRow("layers.\(layer)", text["layers.\(layer)"])
            }
            // Extra information (testing item 33): off by default, each kind with its own switch.
            switchRow("layers.extra", text["extra.title"])
            ForEach(MarksModel.extra, id: \.self) { item in
                switchRow("layers.\(item)", text["extra.\(item)"])
            }
        case .roll:
            switchRow("roll.noteNames", text["roll.noteNames"])
            choiceRow("notes.naming", text["settings.noteNaming"]) { text["settings.noteNaming.\($0)"] }
            choiceRow("roll.colour", text["roll.colourBy"]) { text["settings.rollColour.\($0)"] }
        case .appearance:
            choiceRow("appearance.theme", text["settings.theme"], hint: text["settings.themeHint"]) { text["settings.theme.\($0)"] }
            // Русский or English, picked here (Andrei, 2026-11-15); until then the iPad's. The screens are built again in it.
            VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                Text(text["settings.language"]).dsType(.label).foregroundStyle(Color.ds("ink"))
                SegmentedChoice(options: UIText.languages.map { ($0, text["settings.language.\($0)"]) }, chosen: text.language, label: text["settings.language"]) { picked in
                    Task { await InterfaceLanguage.shared.choose(picked) }
                }
                Text(text["settings.languagePadHint"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            }
        case .account:
            AccountPanel(account: account, text: text)
            // Main or Test (Andrei, 2026-11-12): only in a version build or for a tester.
            EnvironmentPanel(account: account, text: text)
            if ScanServer.hosted(account) == nil {
                // Without Motiff Lab's server, the player's own scan server (0011), as in the web app's Settings.
                ScanPanel(scan: ScanServer.shared, text: text)
            }
            // The engine that reads scans (v0.5.7; a setting since v0.6.5): «Четыре движка» in the background (version 1.3, the
            // default since the 1.3 hotfix), «Авто» (0029), which reads a notation program's pages on the iPad, or homr or
            // copisteria on the scan server.
            choiceRow(ScanServer.engineChoice, text["settings.scanEngineChoice"], hint: text["settings.scanEngineHint"]) { engineName($0) }
        case .about:
            // Which build this is and where it works (Andrei, 2026-11-13); in Test, clearing the test library.
            AboutPanel(account: account, text: text)
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

    /// A choice of «Движок» as the player reads it: Motiff Lab's own two by their words, an engine by its name.
    private func engineName(_ choice: String) -> String {
        switch choice {
        case ScanServer.allEngines: text["settings.scanEngineAll"]
        case ScanServer.auto: text["settings.scanEngineAuto"]
        default: choice
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
        case .about: text["settings.section.about"]
        }
    }

    /// "Bookmarks: hidden".
    private func overrideName(_ id: String, _ value: String) -> String {
        if id.hasPrefix("layers.") {
            let layer = String(id.dropFirst("layers.".count))
            let name = layer == "extra" ? text["extra.title"] : MarksModel.extra.contains(layer) ? text["extra.\(layer)"] : text[id]
            return "\(name): \(text[value == "off" ? "settings.hidden" : "settings.shown"])"
        }
        return id
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
