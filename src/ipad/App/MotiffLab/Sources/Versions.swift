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
    /// "score" for notes (a MusicXML file, a PDF read); "midi" or "recording" for a performance.
    var source = "midi"
    /// When a merge draft of it was last applied; nil when none is applied now (testing item 32).
    var mergedAt: Date?
    /// Whether the choices of a merge are kept with it, to open again.
    var mergeKept = false

    /// A time music-core writes (.NET's round trip, seven digits of fraction).
    static func date(_ text: String?) -> Date? {
        guard let text else { return nil }
        let reader = ISO8601DateFormatter()
        return reader.date(from: text) ?? reader.date(from: text.replacingOccurrences(of: #"\.\d+"#, with: "", options: .regularExpression))
    }
}

extension VersionEntry {
    /// A version as music-core writes its head (`VersionHead`); nil without its id.
    init?(_ head: VersionHead) {
        guard let id = head.id else { return nil }
        self.init(
            id: id, name: head.name ?? "", takenBars: head.takenBars ?? 0, source: head.source ?? "midi",
            mergedAt: VersionEntry.date(head.mergedAt), mergeKept: head.mergeKept ?? false
        )
    }
}

/// The merge draft open now, as music-core answers `performance.merge` (testing items 32 and 34, contract 7): nothing in
/// the piece changes until Apply, and every step can be undone, redone or reset.
struct MergeInfo: Equatable, Sendable {
    struct Bar: Identifiable, Equatable, Sendable {
        var bar: Int
        var kind: String
        /// "addedNotes", "otherNotes" or "rhythm".
        var group: String
        var marker: String
        var confidence: Int
        var sure: Bool
        /// "mine", "version", "own" or "notADifference"; nil while undecided.
        var choice: String?
        var id: Int { bar }
    }

    var id: String
    var name: String
    var decided: Int
    var total: Int
    var sure: Int
    /// How many bars change after Apply.
    var taking: Int
    var canUndo: Bool
    var canRedo: Bool
    var changed: Bool
    var bars: [Bar]

    /// The draft music-core answers (`MergeAnswer`, `Answer.Merge`).
    init(_ draft: MergeAnswer.Draft) {
        id = draft.id
        name = draft.name
        decided = draft.decided
        total = draft.total
        sure = draft.sure
        taking = draft.taking
        canUndo = draft.canUndo
        canRedo = draft.canRedo
        changed = draft.changed
        bars = draft.bars.map { Bar(bar: $0.bar, kind: $0.kind, group: $0.group, marker: $0.marker, confidence: $0.confidence, sure: $0.sure, choice: $0.choice) }
    }

    func choice(at bar: Int) -> String? {
        bars.first { $0.bar == bar }?.choice
    }
}

/// The piece's sources as music-core answers `sources` (testing item 16, contract 6): the base, the line on what the
/// player's version holds beyond it, and the PDF kept inside the piece.
struct SourcesInfo: Equatable, Sendable {
    /// "file", "scan", "recording" or "pdf".
    var base = "file"
    var baseName = ""
    var summary = ""
    /// The PDF kept in the piece: its file name and how many pages it has; nil when it keeps none.
    var document: KeptDocument?

    struct KeptDocument: Equatable, Sendable {
        var fileName: String
        var pages: Int
        /// Where each page starts in the notes, in ticks, for PDF | Notes.
        var starts: [Double] = []
    }

    init() {}

    init?(answer: String) {
        guard let object = facadeObject(answer), object["error"] == nil else { return nil }
        base = object["base"] as? String ?? "file"
        baseName = object["baseName"] as? String ?? ""
        summary = object["summary"] as? String ?? ""
        if let kept = object["document"] as? [String: Any] {
            document = KeptDocument(
                fileName: kept["fileName"] as? String ?? "", pages: (kept["pages"] as? NSNumber)?.intValue ?? 0,
                starts: (kept["starts"] as? [NSNumber] ?? []).map(\.doubleValue)
            )
        }
    }
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
    /// How much of the shown version lines up, in whole per cent of its bars; nil with no bars.
    var alignedPercent: Int? { bars.isEmpty ? nil : alignedCount * 100 / bars.count }
    /// The bars Take everything sure would take: lined up, at least 80 % sure, not dismissed or taken.
    var sureCount: Int { differing.filter { $0.confidence >= 80 }.count }
    /// The bars that differ, sure or not, whatever was decided about them: what a merge draft goes through.
    var differingAll: [VersionBar] { bars.filter(\.differs) }

    /// Reads `performances` (or what removing one answers, `PerformancesAnswer`); the version shown stays shown while it is
    /// there.
    mutating func read(list answer: String) {
        guard let list = FacadeAnswer.decode(PerformancesAnswer.self, answer, of: "performances") else { return }
        entries = (list.performances ?? []).compactMap { VersionEntry($0) }
        if let shownId, !entries.contains(where: { $0.id == shownId }) {
            self.shownId = nil
            bars = []
            matched = 0
            printed = 0
        }
    }

    /// Reads `performance` (or what a change to one answers) in its text, for the calls not yet made through `PieceCore`.
    mutating func read(performance answer: String) -> Bool {
        read(performance: FacadeAnswer.decode(PerformanceAnswer.self, answer, of: "performance"))
    }

    /// Reads a version compared (`PerformanceAnswer`): that version is now the one shown. False for a refusal or an answer
    /// that cannot be read.
    mutating func read(performance answer: PerformanceAnswer?) -> Bool {
        guard let performance = answer?.performance, let entry = VersionEntry(performance.head) else { return false }
        if let index = entries.firstIndex(where: { $0.id == entry.id }) { entries[index] = entry } else { entries.append(entry) }
        shownId = entry.id
        matched = performance.matched ?? 0
        printed = performance.printed ?? 0
        bars = (performance.bars ?? []).compactMap { bar in
            guard let number = bar.bar else { return nil }
            let span = bar.span.flatMap { span in span.onset.flatMap { onset in span.end.map { onset..<max($0, onset + 1) } } }
            return VersionBar(
                bar: number, span: span, aligned: bar.aligned ?? false, kind: bar.kind ?? "none",
                marker: bar.marker ?? "", confidence: bar.confidence ?? 0,
                dismissed: bar.dismissed ?? false, taken: bar.taken ?? false,
                notHeard: bar.notHeard ?? [], played: bar.played?.count ?? 0,
                artefacts: bar.artefacts?.count ?? 0
            )
        }
        return true
    }
}

/// What a version can be: a MIDI file, or a score in MusicXML (Sources' «Добавить»).
enum VersionFiles {
    static let types: [UTType] = [
        .midi,
        UTType(filenameExtension: "musicxml") ?? .xml,
        UTType(filenameExtension: "mxl") ?? .data,
        .xml,
    ]
}

/// The colour of a draft's view (testing item 34): Mine graphite, Version plum, Result green.
func mergeViewColor(_ view: String) -> Color {
    switch view {
    case "version": Color.ds("plum")
    case "result": Color.ds("success")
    default: Color.ds("ink-2")
    }
}

/// A version's page in Sources (testing items 32 and 34): the overview or the draft, what was merged, the window, the
/// version's name and Remove.
struct VersionPage: View {
    @ObservedObject var score: ScoreController
    let text: UIText
    let version: VersionEntry
    @Binding var name: String
    @ObservedObject private var settings = DeviceSettings.shared

    var body: some View {
        let versions = score.versions
        let differing = versions.differingAll
        let sure = differing.filter { $0.confidence >= 80 }.count
        if let merge = score.merge, merge.id == version.id {
            DraftBlock(score: score, text: text, merge: merge)
        } else {
            if let at = version.mergedAt {
                VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Icon(name: "check", size: CGFloat.ds("icon-s"))
                        Text(text.format("merge.appliedAt", at.formatted(date: .omitted, time: .shortened), text.count("merge.bars", version.takenBars)))
                            .font(DesignTokens.font(weight: 600, size: 15))
                    }
                    .foregroundStyle(Color.ds("ink"))
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Button(text["merge.reopen"]) { score.startMerge("saved", direct: false) }
                            .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                        Button { score.undoAppliedMerge() } label: {
                            Label { Text(text["merge.undoMerge"]) } icon: { Icon(name: "undo", size: CGFloat.ds("icon-s")) }
                        }
                        .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                    }
                }
                .padding(CGFloat.ds("s-3"))
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(Color.ds("plum-tint"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
            } else if version.mergeKept {
                VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                    Text(text["merge.keptChoices"]).dsType(.label).foregroundStyle(Color.ds("ink"))
                    Button(text["merge.reopen"]) { score.startMerge("saved", direct: false) }
                        .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                }
                .padding(CGFloat.ds("s-3"))
                .frame(maxWidth: .infinity, alignment: .leading)
                .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("plum"), style: StrokeStyle(lineWidth: 1, dash: [4, 3])))
            }
            VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
                Text(text["merge.overview"]).overline()
                Text(verdict(differing.count, sure))
                    .dsType(.label)
                    .foregroundStyle(Color.ds("ink"))
                    .fixedSize(horizontal: false, vertical: true)
                BarStrip(score: score, text: text, bars: versions.bars)
                if !differing.isEmpty {
                    VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                        startButton("mine", text["merge.keepMine"], swatch: "mine")
                        startButton("sure", text.format("merge.takeSure", String(sure)), swatch: "version")
                            .disabled(sure == 0)
                        startButton("all", text.format("merge.takeAll", String(differing.count)), swatch: "version")
                    }
                    directToggle
                }
            }
        }
        if !versions.differingAll.isEmpty || score.merge != nil {
            Button { score.comparing = true } label: {
                Label { Text(text["merge.window"] + " ⤢") } icon: { Icon(name: "layers") }
            }
            .buttonStyle(DSButtonStyle(kind: .quiet))
        }
        if let status = score.versionStatus {
            Text(status)
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .fixedSize(horizontal: false, vertical: true)
        }
        Rectangle().fill(Color.ds("hairline")).frame(height: 1)
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            Text(text["performance.name"])
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
            TextField(text["performance.name"], text: $name)
                .dsType(.label)
                .modifier(FieldLook())
                .submitLabel(.done)
                .onSubmit { score.renameVersion(name) }
        }
        Button { score.removeVersion() } label: {
            Label { Text(text["merge.remove"]) } icon: { Icon(name: "trash", size: CGFloat.ds("icon-s")) }
        }
        .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
        .foregroundStyle(Color.ds("danger"))
        Text(text["merge.removeHow"])
            .dsType(.caption)
            .foregroundStyle(Color.ds("ink-2"))
            .fixedSize(horizontal: false, vertical: true)
    }

    private var directToggle: some View {
        Toggle(text["merge.direct"], isOn: Binding(get: { settings["merge.direct"] == "on" }, set: { settings.set("merge.direct", $0 ? "on" : "off") }))
            .dsType(.label)
            .foregroundStyle(Color.ds("ink"))
            .tint(Color.ds("accent"))
    }

    private func startButton(_ start: String, _ title: String, swatch: String) -> some View {
        Button { score.startMerge(start) } label: {
            HStack(spacing: CGFloat.ds("s-2")) {
                RoundedRectangle(cornerRadius: 2).fill(mergeViewColor(swatch)).frame(width: 10, height: 10)
                Text(title)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .buttonStyle(DSButtonStyle(kind: .secondary))
    }

    /// "fur-elise-cover plays 2 bars differently, 1 of them sure."
    private func verdict(_ differing: Int, _ sure: Int) -> String {
        differing == 0
            ? text.format("merge.verdictSame", version.name)
            : text.format("merge.verdict", version.name, text.count("merge.bars", differing), text.count("merge.sure", sure))
    }
}

/// The bars of the piece in a strip: grey where the version agrees, plum where it differs, the sure ones darker, those
/// decided in the draft in their view's colour; a tap goes to the bar.
struct BarStrip: View {
    @ObservedObject var score: ScoreController
    let text: UIText
    let bars: [VersionBar]

    var body: some View {
        HStack(spacing: 1) {
            ForEach(bars) { bar in
                Button {
                    if score.merge != nil { score.goToMergeBar(bar.bar) } else { score.showVersionBar(bar.bar) }
                } label: {
                    Rectangle()
                        .fill(fill(bar))
                        .frame(maxWidth: .infinity, minHeight: 18, maxHeight: 18)
                        .overlay(Rectangle().strokeBorder(score.mergeAt == bar.bar ? Color.ds("accent") : Color.clear, lineWidth: 2))
                }
                .buttonStyle(.plain)
                .accessibilityLabel(text.format("check.bar", String(bar.bar)) + ": " + (bar.kind == "none" ? text["merge.same"] : bar.marker))
            }
        }
        .clipShape(RoundedRectangle(cornerRadius: CGFloat.ds("r-xs")))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["merge.strip"])
    }

    private func fill(_ bar: VersionBar) -> Color {
        if let choice = score.merge?.choice(at: bar.bar) {
            switch choice {
            case "version": return Color.ds("plum")
            case "own": return Color.ds("amber")
            case "notADifference": return Color.ds("hairline")
            default: return Color.ds("ink-2")
            }
        }
        if !bar.aligned { return Color.ds("surface-2") }
        if !bar.differs { return Color.ds("hairline") }
        return bar.confidence >= 80 ? Color.ds("plum") : Color.ds("plum-tint")
    }
}

/// The name of a bar's state in the draft: not decided, mine, from the version, your own or not a difference.
func mergeChoiceName(_ choice: String?, _ text: UIText) -> String {
    text["merge.state.\(choice == "notADifference" ? "notDifference" : choice ?? "open")"]
}

/// The draft in the drawer (dashed plum): what changes, ‹ › through the changes, Bar or Phrase, Take mine and Take from the
/// version for the change shown, the list of changes, the three views to hear, and Undo, Redo, Reset, Cancel and Apply.
struct DraftBlock: View {
    @ObservedObject var score: ScoreController
    let text: UIText
    let merge: MergeInfo
    @ObservedObject private var settings = DeviceSettings.shared

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            HStack {
                Text(text["merge.draft"]).overline()
                Spacer()
                Text(text.format("merge.decided", String(merge.decided), String(merge.total)))
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
            }
            Text(text.count("merge.changes", merge.taking))
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
            if let at = score.mergeAt, let current = merge.bars.first(where: { $0.bar == at }) {
                MergeStep(score: score, text: text, current: current)
                MergeScope(score: score, text: text)
                HStack(spacing: CGFloat.ds("s-2")) {
                    MergeChoiceButton(text: text["merge.takeMine"], view: "mine", on: current.choice == "mine") { score.chooseMerge("mine") }
                    MergeChoiceButton(text: text["merge.takeVersion"], view: "version", on: current.choice == "version") { score.chooseMerge("version") }
                }
                Text(text.format("merge.stateLine", mergeChoiceName(current.choice, text)))
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
            }
            VStack(spacing: CGFloat.ds("s-1")) {
                ForEach(merge.bars) { bar in
                    MergeRow(score: score, text: text, bar: bar, detail: bar.marker)
                }
            }
            MergeListen(score: score, text: text)
            HStack(spacing: CGFloat.ds("s-1")) {
                Button { score.undoDraft() } label: {
                    Label { Text(text["merge.undo"]) } icon: { Icon(name: "undo", size: CGFloat.ds("icon-s")) }
                }
                .disabled(!merge.canUndo)
                Button { score.redoDraft() } label: {
                    Label { Text(text["merge.redo"]) } icon: { Icon(name: "undo", size: CGFloat.ds("icon-s")).scaleEffect(x: -1, y: 1) }
                }
                .disabled(!merge.canRedo)
                Button(text["merge.reset"]) { score.resetDraft() }
                    .disabled(!merge.changed)
            }
            .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            HStack(spacing: CGFloat.ds("s-2")) {
                Spacer()
                Button(text["merge.cancel"]) { score.cancelDraft() }
                    .buttonStyle(DSButtonStyle(kind: .quiet))
                Button { score.applyMerge() } label: {
                    Label { Text(text[score.versions.shown?.mergedAt != nil ? "merge.applyAgain" : "merge.apply"]) } icon: { Icon(name: "check", size: CGFloat.ds("icon-s")) }
                }
                .buttonStyle(DSButtonStyle(kind: .primary))
            }
            Toggle(text["merge.direct"], isOn: Binding(get: { settings["merge.direct"] == "on" }, set: { settings.set("merge.direct", $0 ? "on" : "off") }))
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .tint(Color.ds("accent"))
        }
        .padding(CGFloat.ds("s-3"))
        .frame(maxWidth: .infinity, alignment: .leading)
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("plum"), style: StrokeStyle(lineWidth: 1.5, dash: [5, 3])))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["merge.draft"])
    }
}

/// ‹ Bar N · +3 · added notes · 82 % sure ›
struct MergeStep: View {
    @ObservedObject var score: ScoreController
    let text: UIText
    let current: MergeInfo.Bar

    var body: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            Button { score.stepMerge(-1) } label: { Icon(name: "back").frame(width: 32, height: 32) }
                .buttonStyle(BarActionStyle())
                .accessibilityLabel(text["merge.previous"])
            VStack(alignment: .leading, spacing: 2) {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Text(text.format("check.bar", String(current.bar)))
                        .font(DesignTokens.font(weight: 600, size: 15))
                        .foregroundStyle(Color.ds("ink"))
                    Tag(text: current.marker, kind: .saved)
                }
                Text(text.format("merge.confidence", String(current.confidence)))
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            Button { score.stepMerge(1) } label: { Icon(name: "fwd").frame(width: 32, height: 32) }
                .buttonStyle(BarActionStyle())
                .accessibilityLabel(text["merge.next"])
        }
    }
}

/// Take for: Bar | Phrase.
struct MergeScope: View {
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        SegmentedChoice(options: [(false, text["merge.scopeBar"]), (true, text["merge.scopePhrase"])], chosen: score.mergePhrase, label: text["merge.scope"]) {
            score.mergePhrase = $0
        }
    }
}

/// A choice in its view's colour: Take mine graphite, Take from the version plum.
struct MergeChoiceButton: View {
    let text: String
    let view: String
    let on: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: CGFloat.ds("s-2")) {
                RoundedRectangle(cornerRadius: 2).fill(mergeViewColor(view)).frame(width: 10, height: 10)
                Text(text)
            }
        }
        .buttonStyle(DSButtonStyle(kind: .secondary))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(on ? mergeViewColor(view) : Color.clear, lineWidth: 2))
        .accessibilityAddTraits(on ? .isSelected : [])
    }
}

/// A change in the list: its bar, what differs and its state; a tap goes to it.
struct MergeRow: View {
    @ObservedObject var score: ScoreController
    let text: UIText
    let bar: MergeInfo.Bar
    let detail: String

    var body: some View {
        let on = score.mergeAt == bar.bar
        Button { score.goToMergeBar(bar.bar) } label: {
            HStack(spacing: CGFloat.ds("s-2")) {
                Text(text.format("check.bar", String(bar.bar)))
                    .font(DesignTokens.font(weight: 600, size: 14))
                    .foregroundStyle(Color.ds("ink"))
                Text(detail)
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
                    .lineLimit(1)
                Spacer(minLength: CGFloat.ds("s-2"))
                Text(mergeChoiceName(bar.choice, text))
                    .font(DesignTokens.font(weight: 600, size: 12, relativeTo: .caption))
                    .foregroundStyle(stateColor)
            }
            .padding(.horizontal, CGFloat.ds("s-2"))
            .padding(.vertical, CGFloat.ds("s-1"))
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(on ? Color.ds("accent-tint") : Color.clear, in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(on ? .isSelected : [])
    }

    private var stateColor: Color {
        switch bar.choice {
        case "version": Color.ds("plum")
        case "own": Color.ds("amber")
        case "mine": Color.ds("ink")
        default: Color.ds("ink-3")
        }
    }
}

/// Listen: Mine · Version · Result · In turn, each in its view's colour.
struct MergeListen: View {
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            Text(text["merge.listen"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            ForEach(["mine", "version", "result", "alternate"], id: \.self) { view in
                Button { score.hearMerge(view) } label: {
                    HStack(spacing: 4) {
                        if view != "alternate" { Circle().fill(mergeViewColor(view)).frame(width: 8, height: 8) }
                        Text(text["merge.view.\(view)"])
                    }
                }
                .buttonStyle(DSButtonStyle(kind: score.mergeHearing == view ? .secondary : .quiet, compact: true))
                .accessibilityLabel(text.format("merge.hearView", text["merge.view.\(view)"]))
            }
        }
    }
}

/// While a draft is open, what the score shows: "Show: Mine · Version · Result", floating over the score.
struct MergeShowSwitch: View {
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            Text(text["merge.show"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            ForEach(["mine", "version", "result"], id: \.self) { view in
                let on = score.mergeShow == view
                Button { score.showMergeView(view) } label: {
                    HStack(spacing: 4) {
                        Circle().fill(mergeViewColor(view)).frame(width: 8, height: 8)
                        SteadyWord(text: text["merge.view.\(view)"], on: on)
                            .foregroundStyle(on ? Color.ds("ink") : Color.ds("ink-2"))
                    }
                    .padding(.horizontal, CGFloat.ds("s-2"))
                    .frame(height: 32)
                    .background(on ? Color.ds("surface") : Color.clear, in: Capsule())
                }
                .buttonStyle(.plain)
                .accessibilityAddTraits(on ? .isSelected : [])
            }
            Button { score.hearMerge(score.mergeShow) } label: {
                Icon(name: "play", size: CGFloat.ds("icon-s")).frame(width: 32, height: 32)
            }
            .buttonStyle(BarActionStyle())
            .accessibilityLabel(text.format("merge.hearView", text["merge.view.\(score.mergeShow)"]))
        }
        .padding(.horizontal, CGFloat.ds("s-3"))
        .padding(.vertical, CGFloat.ds("s-1"))
        .background(Color.ds("surface-2"), in: Capsule())
        .overlay(Capsule().strokeBorder(Color.ds("hairline")))
        .dsShadow("shadow-menu")
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["merge.show"])
    }
}

/// What was just done with a version, with Undo: a merge applied or undone, a version removed.
struct MergeToastView: View {
    @ObservedObject var score: ScoreController
    let text: UIText
    let toast: ScoreController.MergeToast

    var body: some View {
        UndoToast(words: toast.text, text: text, undo: toast.undo != nil ? { score.undoToast() } : nil) { score.closeToast() }
    }
}

/// The score's toast: a light capsule with what was just done, a quiet «Отменить» where it can be undone, and ✕.
struct UndoToast: View {
    let words: String
    let text: UIText
    let undo: (() -> Void)?
    let close: () -> Void

    var body: some View {
        HStack(spacing: CGFloat.ds("s-3")) {
            Text(words)
                .dsType(.label)
                .foregroundStyle(Color.ds("ink"))
            if let undo {
                Button(text["merge.toastUndo"], action: undo)
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            }
            Button(action: close) { Icon(name: "close", size: CGFloat.ds("icon-s")).frame(width: 28, height: 28) }
                .buttonStyle(BarActionStyle())
                .accessibilityLabel(text["bar.close"])
        }
        .padding(.leading, CGFloat.ds("s-4"))
        .padding(.trailing, CGFloat.ds("s-2"))
        .padding(.vertical, CGFloat.ds("s-1"))
        .background(Color.ds("surface"), in: Capsule())
        .overlay(Capsule().strokeBorder(Color.ds("hairline")))
        .dsShadow("shadow-menu")
        .accessibilityElement(children: .contain)
    }
}

/// A marker tapped on the score (testing item 33): how the bar differs, heard as the version plays it, and the way into
/// the draft; nothing is taken from the score here.
struct MarkerPopover: View {
    @ObservedObject var score: ScoreController
    let text: UIText
    let pick: ScoreController.MarkerPick

    var body: some View {
        let bar = score.versions.bars.first { $0.bar == pick.bar }
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text.format("merge.markerTitle", String(pick.bar), score.versions.shown?.name ?? "")).overline()
            if let bar {
                Text(bar.marker).dsType(.label).foregroundStyle(Color.ds("ink"))
            }
            HStack(spacing: CGFloat.ds("s-2")) {
                Button { score.hearMarkerVersion() } label: {
                    Label { Text(text["merge.asInVersion"]) } icon: { Icon(name: "play", size: CGFloat.ds("icon-s")) }
                }
                .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                Button(text["merge.openDraft"]) { score.openDraft(at: pick.bar) }
                    .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
            }
        }
        .padding(CGFloat.ds("s-3"))
        .frame(width: 300, alignment: .leading)
        .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
        .dsShadow("shadow-menu")
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text.format("merge.markerTitle", String(pick.bar), score.versions.shown?.name ?? ""))
    }
}
