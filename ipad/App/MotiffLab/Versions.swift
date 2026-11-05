import SwiftUI
import UniformTypeIdentifiers

/// One bar of the piece compared with another version (facade `performance`, decision record 0022).
struct VersionBar: Identifiable, Equatable, Sendable {
    var bar: Int
    /// Where the bar is in the piece.
    var span: Range<Double>?
    var aligned: Bool
    /// "none" when the bar is the same in both; "addedNotes", "differentBass", … otherwise.
    var kind: String
    /// The label above the bar, in the piece's language: "+3 · added notes".
    var marker: String
    var confidence: Int
    var dismissed: Bool
    var taken: Bool
    /// The printed notes the version does not have, tinted on the score.
    var notHeard: [String]
    /// How many notes the version has that these notes do not.
    var played: Int
    /// How many of its notes are likely artefacts of a recording's hearing.
    var artefacts = 0

    var id: Int { bar }
    var differs: Bool { aligned && kind != "none" }
}

/// Another version as `performances` lists it.
struct VersionEntry: Identifiable, Equatable, Sendable {
    var id: String
    var name: String
    var takenBars: Int
}

/// The other versions of the piece and the one shown, compared bar by bar, as music-core answers `performances` and
/// `performance` (0022): a version is any MIDI or MusicXML file of the piece, a cover's MIDI or an official score.
struct VersionShelf: Equatable, Sendable {
    var entries: [VersionEntry] = []
    var shownId: String?
    /// The printed notes found in the shown version, and how many there are.
    var matched = 0
    var printed = 0
    var bars: [VersionBar] = []

    var shown: VersionEntry? { entries.first { $0.id == shownId } }

    /// The bars the shown version has otherwise and that are still to look at: not marked "not a difference" or taken.
    var differing: [VersionBar] { bars.filter { $0.differs && !$0.dismissed && !$0.taken } }
    var taken: [VersionBar] { bars.filter(\.taken) }
    var dismissed: [VersionBar] { bars.filter(\.dismissed) }
    var alignedCount: Int { bars.filter(\.aligned).count }
    /// The likely artefacts on the comparison layer, which writes only the bars that line up.
    var artefactCount: Int { bars.filter(\.aligned).reduce(0) { $0 + $1.artefacts } }

    /// Reads `performances`; the version shown stays shown while it is there.
    mutating func read(list answer: String) {
        guard let object = facadeObject(answer), object["error"] == nil else { return }
        entries = (object["performances"] as? [[String: Any]] ?? []).compactMap(Self.entry)
        if let shownId, !entries.contains(where: { $0.id == shownId }) {
            self.shownId = nil
            bars = []
            matched = 0
            printed = 0
        }
    }

    /// Reads `performance` (or what a change to one answers): that version is now the one shown. False for an error.
    mutating func read(performance answer: String) -> Bool {
        guard let object = facadeObject(answer), object["error"] == nil, let performance = object["performance"] as? [String: Any],
              let entry = Self.entry(performance) else { return false }
        func number(_ value: Any?) -> Double? { (value as? NSNumber)?.doubleValue }
        if let index = entries.firstIndex(where: { $0.id == entry.id }) { entries[index] = entry } else { entries.append(entry) }
        shownId = entry.id
        matched = Int(number(performance["matched"]) ?? 0)
        printed = Int(number(performance["printed"]) ?? 0)
        bars = (performance["bars"] as? [[String: Any]] ?? []).compactMap { bar in
            guard let number = number(bar["bar"]) else { return nil }
            let span = (bar["span"] as? [String: Any]).flatMap { span in
                (span["onset"] as? NSNumber).flatMap { onset in (span["end"] as? NSNumber).map { onset.doubleValue..<max($0.doubleValue, onset.doubleValue + 1) } }
            }
            return VersionBar(
                bar: Int(number), span: span, aligned: bar["aligned"] as? Bool ?? false, kind: bar["kind"] as? String ?? "none",
                marker: bar["marker"] as? String ?? "", confidence: (bar["confidence"] as? NSNumber)?.intValue ?? 0,
                dismissed: bar["dismissed"] as? Bool ?? false, taken: bar["taken"] as? Bool ?? false,
                notHeard: bar["notHeard"] as? [String] ?? [], played: (bar["played"] as? [Any])?.count ?? 0,
                artefacts: (bar["artefacts"] as? [Any])?.count ?? 0
            )
        }
        return true
    }

    private static func entry(_ object: [String: Any]) -> VersionEntry? {
        guard let id = object["id"] as? String else { return nil }
        return VersionEntry(id: id, name: object["name"] as? String ?? "", takenBars: (object["takenBars"] as? NSNumber)?.intValue ?? 0)
    }
}

/// Other versions (0022, the web app's `.perf-sheet`), from More or a version's label on the score: the versions as chips,
/// the bars the one shown has otherwise with Show, Not a difference and Take into the score, the bars taken to restore,
/// its name, and a MIDI or MusicXML file to add. Over the right of the page in landscape and the lower half in portrait,
/// as Check.
struct VersionsDrawer: View {
    @ObservedObject var score: ScoreController
    let text: UIText
    let close: () -> Void
    @State private var choosing = false
    @State private var name = ""
    @State private var removing = false

    /// What a version can be: a MIDI file, or a score in MusicXML.
    static let versionFiles: [UTType] = [
        .midi,
        UTType(filenameExtension: "musicxml") ?? .xml,
        UTType(filenameExtension: "mxl") ?? .data,
        .xml,
    ]

    var body: some View {
        let versions = score.versions
        VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .top, spacing: CGFloat.ds("s-2")) {
                VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                    Text(text["performance.title"]).overline()
                    Text(versions.shown?.name ?? text["performance.add"])
                        .dsType(.heading)
                        .foregroundStyle(Color.ds("ink"))
                        .accessibilityAddTraits(.isHeader)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                Button(action: close) {
                    Icon(name: "close").frame(width: 32, height: 32)
                }
                .buttonStyle(BarActionStyle())
                .accessibilityLabel(text["performance.close"])
            }
            .padding(.leading, CGFloat.ds("s-6"))
            .padding(.trailing, CGFloat.ds("s-4"))
            .padding(.top, CGFloat.ds("s-4"))
            ScrollView {
                VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
                    if versions.entries.count > 1 {
                        chips(versions)
                    }
                    if let status = score.versionStatus {
                        Text(status)
                            .dsType(.caption)
                            .foregroundStyle(Color.ds("ink-2"))
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    if let shown = versions.shown {
                        comparison(versions)
                        nameAndRemove(shown)
                    }
                    Text(text["performance.how"])
                        .dsType(.caption)
                        .foregroundStyle(Color.ds("ink-2"))
                        .fixedSize(horizontal: false, vertical: true)
                    Button(text["performance.addMidi"]) { choosing = true }
                        .buttonStyle(DSButtonStyle(kind: .secondary))
                }
                .padding(.leading, CGFloat.ds("s-6"))
                .padding(.trailing, CGFloat.ds("s-5"))
                .padding(.vertical, CGFloat.ds("s-4"))
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .background(Color.ds("surface"))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["performance.title"])
        .fileImporter(isPresented: $choosing, allowedContentTypes: Self.versionFiles) { result in
            guard case .success(let url) = result else { return }
            let lent = url.startAccessingSecurityScopedResource()
            defer { if lent { url.stopAccessingSecurityScopedResource() } }
            guard let data = try? Data(contentsOf: url) else { return }
            let fileName = url.lastPathComponent
            Task { await score.addVersion(fileName: fileName, data: data) }
        }
        .onAppear { name = versions.shown?.name ?? "" }
        .onChange(of: versions.shown?.name) { _, shown in
            name = shown ?? ""
            removing = false
        }
    }

    private func chips(_ versions: VersionShelf) -> some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: CGFloat.ds("s-2")) {
                ForEach(versions.entries) { entry in
                    Button(entry.name) { score.showVersion(entry.id) }
                        .buttonStyle(DSChipStyle(on: entry.id == versions.shownId))
                        .accessibilityAddTraits(entry.id == versions.shownId ? .isSelected : [])
                }
            }
        }
        .accessibilityLabel(text["performance.chips"])
    }

    @ViewBuilder private func comparison(_ versions: VersionShelf) -> some View {
        Text(text.format("performance.summary", String(versions.matched), String(versions.printed), String(versions.alignedCount), String(versions.bars.count)))
            .dsType(.caption)
            .foregroundStyle(Color.ds("ink-2"))
        let differing = versions.differing
        if differing.isEmpty {
            Text(text["performance.noDifferences"])
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
        } else {
            Text(text.count("performance.differs", differing.count)).overline()
            ForEach(differing) { bar in differingEntry(bar) }
        }
        if !versions.taken.isEmpty {
            Text(text["performance.takenHeading"]).overline().padding(.top, CGFloat.ds("s-2"))
            ForEach(versions.taken) { bar in
                doneEntry(bar, icon: true, again: text["performance.restore"]) { score.restoreVersionBar(bar.bar) }
            }
        }
        if !versions.dismissed.isEmpty {
            Text(text["performance.dismissedHeading"]).overline().padding(.top, CGFloat.ds("s-2"))
            ForEach(versions.dismissed) { bar in
                doneEntry(bar, icon: false, again: text["performance.undismiss"]) { score.dismissVersionBar(bar.bar, false) }
            }
        }
    }

    private func differingEntry(_ bar: VersionBar) -> some View {
        let selected = score.selection?.firstBar == bar.bar
        return VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            HStack(spacing: CGFloat.ds("s-2")) {
                Text(text.format("performance.barLine", String(bar.bar), String(bar.confidence)))
                    .font(DesignTokens.font(weight: 600, size: 15))
                    .foregroundStyle(Color.ds("ink"))
                Tag(text: bar.marker, kind: .saved)
            }
            if let counts = counts(bar) {
                Text(counts)
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
            }
            HStack(spacing: CGFloat.ds("s-2")) {
                Button(text["scanCheck.show"]) { score.showVersionBar(bar.bar) }
                    .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                    .disabled(bar.span == nil)
                Button(text["performance.dismiss"]) { score.dismissVersionBar(bar.bar, true) }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                Button(text["performance.take"]) { score.takeVersionBar(bar.bar) }
                    .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
            }
        }
        .padding(CGFloat.ds("s-3"))
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(selected ? Color.ds("accent-tint") : Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(selected ? Color.ds("accent") : Color.ds("hairline")))
        .accessibilityElement(children: .contain)
    }

    private func doneEntry(_ bar: VersionBar, icon: Bool, again: String, action: @escaping () -> Void) -> some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            HStack(spacing: CGFloat.ds("s-2")) {
                if icon {
                    Icon(name: "check", size: CGFloat.ds("icon-s")).foregroundStyle(Color.ds("ink-2"))
                }
                Text(text.format("check.bar", String(bar.bar)))
                    .font(DesignTokens.font(weight: 600, size: 15))
                    .foregroundStyle(Color.ds("ink"))
            }
            HStack(spacing: CGFloat.ds("s-2")) {
                Button(text["scanCheck.show"]) { score.showVersionBar(bar.bar) }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                    .disabled(bar.span == nil)
                Button(again, action: action)
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            }
        }
        .padding(.vertical, CGFloat.ds("s-1"))
    }

    @ViewBuilder private func nameAndRemove(_ shown: VersionEntry) -> some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            Text(text["performance.name"])
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
            TextField(text["performance.name"], text: $name)
                .dsType(.label)
                .textFieldStyle(.roundedBorder)
                .submitLabel(.done)
                .onSubmit { score.renameVersion(name) }
        }
        .padding(.top, CGFloat.ds("s-2"))
        Button(text[removing ? "performance.removeSure" : "performance.remove"]) {
            if removing {
                removing = false
                score.removeVersion()
            } else {
                removing = true
            }
        }
        .buttonStyle(DSButtonStyle(kind: removing ? .danger : .quiet, compact: true))
    }

    /// "in the version, not in these notes: 3 · in these notes, not in the version: 1".
    private func counts(_ bar: VersionBar) -> String? {
        var parts: [String] = []
        if bar.played > 0 { parts.append(text.format("performance.playedNotPrinted", String(bar.played))) }
        if !bar.notHeard.isEmpty { parts.append(text.format("performance.printedNotHeard", String(bar.notHeard.count))) }
        return parts.isEmpty ? nil : parts.joined(separator: " · ")
    }
}
