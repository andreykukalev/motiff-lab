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
        return VersionEntry(
            id: id, name: object["name"] as? String ?? "", takenBars: (object["takenBars"] as? NSNumber)?.intValue ?? 0,
            source: object["source"] as? String ?? "midi"
        )
    }
}

/// Sources (0022; testing item 16, the web app's `.perf-sheet`), from More or a version's label on the score: "My version",
/// a line on what it holds beyond the base, a row for the base, the PDF kept inside the piece and each other version with its
/// role, one Add a source (a MIDI or MusicXML file, a PDF, a recording), and after a version is added its four next steps:
/// Compare, Hear it alone, Take everything sure and Adjust it myself. For the version shown, the bars it has otherwise with
/// Show, Not a difference and Take into the score, the bars taken to restore, and its name. Over the right of the page in
/// landscape and the lower half in portrait, as Check.
struct VersionsDrawer: View {
    @ObservedObject var score: ScoreController
    let text: UIText
    let close: () -> Void
    /// "‹ More" when opened from More: closes the drawer and opens More again (testing item 20); nil from elsewhere.
    var back: (label: String, action: () -> Void)?
    /// The piece's PDF view, when it keeps a PDF.
    var openPdf: (() -> Void)?
    /// Hear it alone: the phrase with the first bar the version has otherwise, in focus, as in the version.
    var hearAlone: () -> Void = {}
    /// Adjust it myself: the note editor on the roll at the first bar the version has otherwise.
    var adjust: () -> Void = {}
    @EnvironmentObject private var account: Account
    @ObservedObject private var scan = ScanServer.shared
    @State private var choosing = false
    @State private var choosingPdf = false
    @State private var adding = false
    @State private var readingPdf = false
    @State private var name = ""
    @State private var removing = false

    /// What a version can be: a MIDI file, or a score in MusicXML.
    static let versionFiles: [UTType] = [
        .midi,
        UTType(filenameExtension: "musicxml") ?? .xml,
        UTType(filenameExtension: "mxl") ?? .data,
        .xml,
    ]

    private var scanAddress: URL? { scan.address(hosted: ScanServer.hosted(account)) }

    var body: some View {
        let versions = score.versions
        VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .top, spacing: CGFloat.ds("s-2")) {
                VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
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
                    sourceRows(versions)
                    if let status = score.versionStatus {
                        Text(status)
                            .dsType(.caption)
                            .foregroundStyle(Color.ds("ink-2"))
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    if let shown = versions.shown, score.addedVersion == shown.id {
                        nextSteps(versions)
                    }
                    if let taken = score.takenAll, taken.version == versions.shownId {
                        HStack(spacing: CGFloat.ds("s-2")) {
                            Text(text.count("sources.takenAll", taken.bars.count))
                                .dsType(.caption)
                                .foregroundStyle(Color.ds("ink-2"))
                                .frame(maxWidth: .infinity, alignment: .leading)
                            Button(text["sources.giveBack"]) { score.giveBackTakenAll() }
                                .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                        }
                    }
                    if let shown = versions.shown {
                        comparison(versions)
                        nameAndRemove(shown)
                    }
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
            removing = false
        }
    }

    /// A row for the base, for the PDF kept inside the piece and for each other version, the one shown in plum.
    @ViewBuilder private func sourceRows(_ versions: VersionShelf) -> some View {
        let sources = score.sources
        if !sources.summary.isEmpty {
            Text(sources.summary)
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .fixedSize(horizontal: false, vertical: true)
        }
        sourceRow(icon: baseIcon(sources.base), title: text["sources.base"], detail: sources.baseName, on: false)
        if let document = sources.document {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                sourceLine(icon: "pages", title: document.fileName, detail: text["sources.pdfInside"])
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
            let shown = entry.id == versions.shownId
            Button { score.showVersion(entry.id) } label: {
                sourceRow(icon: versionIcon(entry), title: entry.name, detail: versionDetail(entry, shown: shown, versions), on: shown)
            }
            .buttonStyle(.plain)
            .accessibilityAddTraits(shown ? .isSelected : [])
        }
    }

    private func sourceRow(icon: String, title: String, detail: String, on: Bool) -> some View {
        sourceLine(icon: icon, title: title, detail: detail)
            .padding(CGFloat.ds("s-3"))
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(on ? Color.ds("plum-tint") : Color.clear, in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
            .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(on ? Color.ds("plum") : Color.ds("hairline")))
            .contentShape(Rectangle())
    }

    private func sourceLine(icon: String, title: String, detail: String) -> some View {
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

    /// "Performance · 92 % lined up": what the version is to the notes, and for the one shown how much of it lines up.
    private func versionDetail(_ entry: VersionEntry, shown: Bool, _ versions: VersionShelf) -> String {
        let role = text[entry.source == "score" ? "sources.roleNotes" : "sources.rolePlaying"]
        guard shown, let percent = versions.alignedPercent else { return role }
        return role + " · " + text.format("sources.aligned", String(percent))
    }

    private func baseIcon(_ base: String) -> String {
        switch base {
        case "recording": "mic"
        case "scan", "pdf": "pages"
        default: "file"
        }
    }

    /// What next, once a version is added: compare, hear it alone, take what is sure, or adjust the notes by hand.
    private func nextSteps(_ versions: VersionShelf) -> some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text["sources.next"]).overline()
            step("layers", text["sources.compare"]) { score.compareVersion() }
            step("play", text["sources.hearAlone"], action: hearAlone)
            step("check", text["sources.takeAll"], detail: text.count("sources.sureBars", versions.sureCount)) { score.takeAllSure() }
                .disabled(versions.sureCount == 0)
            step("roll", text["sources.adjust"], action: adjust)
        }
        .padding(CGFloat.ds("s-3"))
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["sources.next"])
    }

    private func step(_ icon: String, _ title: String, detail: String? = nil, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            HStack(spacing: CGFloat.ds("s-2")) {
                Icon(name: icon)
                Text(title)
                Spacer(minLength: CGFloat.ds("s-2"))
                if let detail {
                    Text(detail).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .buttonStyle(DSButtonStyle(kind: .secondary))
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
            if score.addedVersion != versions.shownId && versions.sureCount > 0 {
                Button(text["sources.takeAll"] + " · " + text.count("sources.sureBars", versions.sureCount)) { score.takeAllSure() }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            }
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
