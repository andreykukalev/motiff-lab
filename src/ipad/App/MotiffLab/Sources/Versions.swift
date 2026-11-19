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
        self.init(id: id, name: head.name ?? "", takenBars: head.takenBars ?? 0)
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

/// A marker tapped on the score (testing item 33): how the bar differs, heard as the version plays it, and the way to the
/// version in Sources; nothing is taken from the score here.
struct MarkerPopover: View {
    @ObservedObject var score: ScoreController
    @ObservedObject var sourcesModel: SourcesModel
    let text: UIText
    let pick: SourcesModel.MarkerPick

    var body: some View {
        let bar = sourcesModel.versions.bars.first { $0.bar == pick.bar }
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text.format("merge.markerTitle", String(pick.bar), sourcesModel.versions.shown?.name ?? "")).overline()
            if let bar {
                Text(bar.marker).dsType(.label).foregroundStyle(Color.ds("ink"))
            }
            HStack(spacing: CGFloat.ds("s-2")) {
                Button { score.hearMarkerVersion() } label: {
                    Label { Text(text["merge.asInVersion"]) } icon: { Icon(name: "play", size: CGFloat.ds("icon-s")) }
                }
                .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                Button(text["sources.title"]) { score.openSourcesFromMarker() }
                    .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
            }
        }
        .padding(CGFloat.ds("s-3"))
        .frame(width: 300, alignment: .leading)
        .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
        .dsShadow("shadow-menu")
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text.format("merge.markerTitle", String(pick.bar), sourcesModel.versions.shown?.name ?? ""))
    }
}
