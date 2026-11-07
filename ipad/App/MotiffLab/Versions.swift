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
        return VersionEntry(
            id: id, name: object["name"] as? String ?? "", takenBars: (object["takenBars"] as? NSNumber)?.intValue ?? 0,
            source: object["source"] as? String ?? "midi",
            mergedAt: VersionEntry.date(object["mergedAt"] as? String), mergeKept: object["mergeKept"] as? Bool ?? false
        )
    }
}

/// Sources (0022; testing items 16 and 32, the web app's `.perf-sheet`), from More or a marker's popover, in two levels.
/// The list: "My version", a line on what it holds beyond the base, a row for the base, the PDF kept inside the piece and
/// each other version with its role, and one Add a source (a MIDI or MusicXML file, a PDF, a recording). A version's row
/// opens its page, with "‹ Sources" back: a verdict, a strip of the piece's bars, the way into the comparison where the
/// player's own version is assembled (0024), what a merge kept by an older project file applied, and its name and Remove,
/// which the toast can undo. Over the right of the page in landscape and the lower half in portrait, as Check.
struct VersionsDrawer: View {
    @ObservedObject var score: ScoreController
    let text: UIText
    let close: () -> Void
    /// "‹ More" when opened from More: closes the drawer and opens More again (testing item 20); nil from elsewhere.
    var back: (label: String, action: () -> Void)?
    /// The piece's PDF view, when it keeps a PDF.
    var openPdf: (() -> Void)?
    @EnvironmentObject private var account: Account
    @ObservedObject private var scan = ScanServer.shared
    @ObservedObject private var settings = DeviceSettings.shared
    @State private var choosing = false
    @State private var choosingPdf = false
    @State private var adding = false
    @State private var readingPdf = false
    @State private var name = ""

    /// What a version can be: a MIDI file, or a score in MusicXML.
    static let versionFiles: [UTType] = [
        .midi,
        UTType(filenameExtension: "musicxml") ?? .xml,
        UTType(filenameExtension: "mxl") ?? .data,
        .xml,
    ]

    private var scanAddress: URL? { scan.address(hosted: ScanServer.hosted(account)) }

    /// The version whose page shows, once music-core has it shown.
    private var page: VersionEntry? {
        guard let id = score.versionPage, let shown = score.versions.shown, shown.id == id else { return nil }
        return shown
    }

    var body: some View {
        let versions = score.versions
        VStack(alignment: .leading, spacing: 0) {
            header
            ScrollView {
                VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
                    if let page {
                        VersionPage(score: score, text: text, version: page, name: $name)
                    } else {
                        sourceRows(versions)
                        status
                        Button {
                            adding.toggle()
                            readingPdf = false
                        } label: {
                            Label { Text(text["sources.add"]) } icon: { Icon(name: "plus") }
                        }
                        .buttonStyle(DSButtonStyle(kind: .primary))
                        .accessibilityAddTraits(adding ? .isSelected : [])
                        if adding {
                            addChoices
                        }
                    }
                }
                .padding(.leading, CGFloat.ds("s-6"))
                .padding(.trailing, CGFloat.ds("s-5"))
                .padding(.vertical, CGFloat.ds("s-4"))
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .background(Color.ds("surface"))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["sources.title"])
        // One importer for both choices: SwiftUI presents only one of two on the same view.
        .fileImporter(isPresented: $choosing, allowedContentTypes: choosingPdf ? [.pdf] : Self.versionFiles) { result in
            guard case .success(let url) = result else { return }
            let lent = url.startAccessingSecurityScopedResource()
            defer { if lent { url.stopAccessingSecurityScopedResource() } }
            guard let data = try? Data(contentsOf: url) else { return }
            let fileName = url.lastPathComponent
            let pdf = choosingPdf
            adding = false
            Task {
                if pdf {
                    await score.keepDocument(data)
                } else {
                    await score.addVersion(fileName: fileName, data: data)
                }
            }
        }
        .sheet(isPresented: $readingPdf) {
            ReadNotesPanel(
                address: scanAddress,
                hosted: scanAddress != nil && scanAddress == ScanServer.hosted(account),
                signedIn: account.player != nil,
                pro: account.allows(.scan),
                text: text,
                heading: text["sources.readPdf"],
                keeps: text["sources.readPdfKeeps"],
                settings: { readingPdf = false },
                read: {
                    guard let address = scanAddress else { return text["pdf.readNone"] }
                    let problem = await score.readDocumentVersion(address: address, account: account)
                    if problem == nil { readingPdf = false }
                    return problem
                }
            )
            .presentationDetents([.medium])
        }
        .onAppear {
            name = versions.shown?.name ?? ""
            score.refreshSources()
        }
        .onChange(of: versions.shown?.name) { _, shown in
            name = shown ?? ""
        }
    }

    private var header: some View {
        HStack(alignment: .top, spacing: CGFloat.ds("s-2")) {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                if let page {
                    BackChip(label: text["sources.title"], text: text) { score.closeVersionPage() }
                        .padding(.leading, -CGFloat.ds("s-2"))
                    Text(text[page.source == "score" ? "sources.roleNotes" : "sources.rolePlaying"]).overline()
                    Text(page.name)
                        .dsType(.heading)
                        .foregroundStyle(Color.ds("ink"))
                        .accessibilityAddTraits(.isHeader)
                } else {
                    if let back {
                        BackChip(label: back.label, text: text, action: back.action)
                            .padding(.leading, -CGFloat.ds("s-2"))
                    }
                    Text(text["sources.title"]).overline()
                    Text(text["sources.mine"])
                        .dsType(.heading)
                        .foregroundStyle(Color.ds("ink"))
                        .accessibilityAddTraits(.isHeader)
                }
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
    }

    @ViewBuilder private var status: some View {
        if let status = score.versionStatus {
            Text(status)
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    /// A row for the base, for the PDF kept inside the piece and for each other version, which opens its page.
    @ViewBuilder private func sourceRows(_ versions: VersionShelf) -> some View {
        let sources = score.sources
        if !sources.summary.isEmpty {
            Text(sources.summary)
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .fixedSize(horizontal: false, vertical: true)
        }
        sourceRow(icon: baseIcon(sources.base), title: text["sources.base"], detail: sources.baseName, opens: false)
        if let document = sources.document {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                sourceLine(icon: "pages", title: document.fileName, detail: text["sources.pdfInside"], opens: false)
                HStack(spacing: CGFloat.ds("s-2")) {
                    if let openPdf {
                        Button(text["sources.openPdf"], action: openPdf)
                            .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                    }
                    Button {
                        readingPdf = true
                        adding = false
                    } label: {
                        HStack(spacing: CGFloat.ds("s-1")) {
                            Text(text["sources.readPdf"])
                            if scanAddress != nil && scanAddress == ScanServer.hosted(account) { Tag(text: "Pro", kind: .saved) }
                        }
                    }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                }
            }
            .padding(CGFloat.ds("s-3"))
            .frame(maxWidth: .infinity, alignment: .leading)
            .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(Color.ds("hairline")))
        }
        ForEach(versions.entries) { entry in
            Button { score.openVersionPage(entry.id) } label: {
                sourceRow(icon: versionIcon(entry), title: entry.name, detail: versionDetail(entry), opens: true)
            }
            .buttonStyle(.plain)
        }
    }

    private func sourceRow(icon: String, title: String, detail: String, opens: Bool) -> some View {
        sourceLine(icon: icon, title: title, detail: detail, opens: opens)
            .padding(CGFloat.ds("s-3"))
            .frame(maxWidth: .infinity, alignment: .leading)
            .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(Color.ds("hairline")))
            .contentShape(Rectangle())
    }

    private func sourceLine(icon: String, title: String, detail: String, opens: Bool) -> some View {
        HStack(spacing: CGFloat.ds("s-3")) {
            Icon(name: icon).foregroundStyle(Color.ds("ink-2"))
            VStack(alignment: .leading, spacing: 0) {
                Text(title)
                    .font(DesignTokens.font(weight: 600, size: 15))
                    .foregroundStyle(Color.ds("ink"))
                Text(detail)
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            if opens {
                Icon(name: "fwd", size: CGFloat.ds("icon-s")).foregroundStyle(Color.ds("ink-3"))
            }
        }
        .accessibilityElement(children: .combine)
    }

    private func versionIcon(_ entry: VersionEntry) -> String {
        switch entry.source {
        case "recording": "mic"
        case "score": "note"
        default: "file"
        }
    }

    /// "Performance · 2 bars taken": what the version is to the notes, and what a merge applied took from it.
    private func versionDetail(_ entry: VersionEntry) -> String {
        let role = text[entry.source == "score" ? "sources.roleNotes" : "sources.rolePlaying"]
        guard entry.mergedAt != nil else { return role }
        return role + " · " + text.count("merge.takenShort", entry.takenBars)
    }

    private func baseIcon(_ base: String) -> String {
        switch base {
        case "recording": "mic"
        case "scan", "pdf": "pages"
        default: "file"
        }
    }

    /// Add a source: a MIDI or MusicXML file, a PDF kept inside the piece, or a recording.
    private var addChoices: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text["performance.how"])
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .fixedSize(horizontal: false, vertical: true)
            Button(text["performance.addMidi"]) {
                choosingPdf = false
                choosing = true
            }
            .buttonStyle(DSButtonStyle(kind: .secondary))
            Button(text[score.sources.document == nil ? "sources.addPdf" : "sources.replacePdf"]) {
                choosingPdf = true
                choosing = true
            }
            .buttonStyle(DSButtonStyle(kind: .secondary))
            Text(text["sources.pdfHow"])
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .fixedSize(horizontal: false, vertical: true)
            RecordingVersionSection(score: score, text: text)
        }
        .padding(CGFloat.ds("s-3"))
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
    }
}

/// A version's page in Sources: how it matches the notes, the way into the comparison (0024), what a merge kept by an older
/// project file applied, the version's name and Remove.
struct VersionPage: View {
    @ObservedObject var score: ScoreController
    let text: UIText
    let version: VersionEntry
    @Binding var name: String

    var body: some View {
        let versions = score.versions
        let differing = versions.differingAll
        let sure = differing.filter { $0.confidence >= 80 }.count
        let source = score.assembly.source(version.id)
        if let at = version.mergedAt {
            HStack(spacing: CGFloat.ds("s-2")) {
                Icon(name: "check", size: CGFloat.ds("icon-s"))
                Text(text.format("merge.appliedAt", at.formatted(date: .omitted, time: .shortened), text.count("merge.bars", version.takenBars)))
                    .dsType(.label)
            }
            .foregroundStyle(Color.ds("ink"))
            .padding(CGFloat.ds("s-3"))
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Color.ds("plum-tint"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        }
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            Text(text["merge.overview"]).overline()
            Text(source?.summary ?? verdict(differing.count, sure))
                .dsType(.label)
                .foregroundStyle(Color.ds("ink"))
                .fixedSize(horizontal: false, vertical: true)
            if let detail = source?.detail {
                Text(detail).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            }
            BarStrip(score: score, text: text, bars: versions.bars)
            // The comparison, where the player's own version is assembled from the sources (0024): nothing in the notes
            // changes until «Сделать нотами».
            Button(text["assembly.compare"]) { score.compare(version.id) }
                .buttonStyle(DSButtonStyle(kind: .primary))
                .disabled(source?.reads == false)
            if let source, source.corrections > 0 {
                Button(text.format("assembly.accept", String(source.corrections))) {
                    score.compare(version.id)
                    score.assembly.acceptMatched(version.id)
                }
                .buttonStyle(DSButtonStyle(kind: .secondary))
                Text(text["assembly.acceptHint"]).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true)
            }
            Text(text["assembly.sourcesSafe"]).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true)
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
                .textFieldStyle(.roundedBorder)
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

    /// "fur-elise-cover plays 2 bars differently, 1 of them sure."
    private func verdict(_ differing: Int, _ sure: Int) -> String {
        differing == 0
            ? text.format("merge.verdictSame", version.name)
            : text.format("merge.verdict", version.name, text.count("merge.bars", differing), text.count("merge.sure", sure))
    }
}

/// The bars of the piece in a strip: grey where the version agrees, plum where it differs, the sure ones darker; a tap
/// goes to the bar.
struct BarStrip: View {
    @ObservedObject var score: ScoreController
    let text: UIText
    let bars: [VersionBar]

    var body: some View {
        HStack(spacing: 1) {
            ForEach(bars) { bar in
                Button {
                    score.showVersionBar(bar.bar)
                } label: {
                    Rectangle()
                        .fill(fill(bar))
                        .frame(maxWidth: .infinity, minHeight: 18, maxHeight: 18)
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
        if !bar.aligned { return Color.ds("surface-2") }
        if !bar.differs { return Color.ds("hairline") }
        return bar.confidence >= 80 ? Color.ds("plum") : Color.ds("plum-tint")
    }
}

/// What was just done with a version, with Undo: a version removed.
struct MergeToastView: View {
    @ObservedObject var score: ScoreController
    let text: UIText
    let toast: ScoreController.MergeToast

    var body: some View {
        HStack(spacing: CGFloat.ds("s-3")) {
            Text(toast.text)
                .dsType(.label)
                .foregroundStyle(Color.ds("ink"))
            if toast.undo != nil {
                Button(text["merge.toastUndo"]) { score.undoToast() }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            }
            Button { score.closeToast() } label: { Icon(name: "close", size: CGFloat.ds("icon-s")).frame(width: 28, height: 28) }
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
/// the comparison (0024); nothing is taken from the score here.
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
                Button(text["assembly.compare"]) { score.compare() }
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
