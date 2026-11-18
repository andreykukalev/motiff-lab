import Foundation

/// The piece's sources by the layers of decision record 0030 (version 0.6, «Новые источники»; permanent on the iPad since
/// v0.6.5), as music-core answers `sources.layers` for this iPad: the originals, live and deleted, with
/// where their bytes are, the Notes made from them with their provenance and the state of their original, and the views
/// of the switch at the top of the score in its order. Everything here is music-core's answer, never worked out by the app.
struct SourceLayers: Equatable, Sendable {
    struct Original: Identifiable, Equatable, Sendable {
        var id: String
        /// "pdf", "images", "audio", "midi" or "musicxml".
        var kind: String
        /// The player's name for it, else its file's.
        var shown: String
        var fileName: String
        /// "here", "onAnotherDevice", "nowhere" or "removed" (a tombstone).
        var state: String
        var removedAt: Date?
        /// "file", "device" or "link".
        var kept: String
        var pages: Int?
        var durationMs: Int?
        /// Hidden from the switch by the player.
        var hidden: Bool
        /// The Notes made from it, in the sheet's order.
        var notes: [String]
        /// How many marks its pages have (strokes and bookmarks).
        var pageMarks: Int

        var isRemoved: Bool { state == "removed" }
        var isHere: Bool { state == "here" }
        /// A PDF or photos: printed pages, the kind a scan server reads.
        var isPages: Bool { kind == "pdf" || kind == "images" }
        var isRecording: Bool { kind == "audio" }
    }

    struct Notes: Identifiable, Equatable, Sendable {
        var id: String
        /// "working", "other" or "version".
        var role: String
        var name: String?
        /// The provenance's operation: "pdfRecognition", "audioRecognition", "import", "assembly", … or a later build's own.
        var op: String
        var engine: String?
        var at: Date?
        /// The original they were made from, in this piece; nil when none.
        var original: String?
        /// Their original's state ("here", "onAnotherDevice", "nowhere", "fileChanged", "removed"); nil without one.
        var state: String?
        /// How many corrections they carry.
        var edits: Int
        /// For a version: how many bars were taken from it into the working notes.
        var taken: Int
        var hidden: Bool

        var isWorking: Bool { role == "working" }
        /// Other Notes can become the working ones directly («Сделать рабочими»); a version only through a merge.
        var canMakeWorking: Bool { role == "other" }
    }

    /// An original with the Notes music-core lists as made from it: the sheet shows them together (Andrei, v0.8 finding
    /// 19: a PDF and the notes read from it were at the two ends of the list).
    struct Group: Identifiable, Equatable, Sendable {
        var original: Original
        var notes: [Notes]
        var id: String { original.id }
    }

    /// A view of the switch: an original as it is, Notes, or a recording's sound.
    struct ViewEntry: Identifiable, Equatable, Sendable {
        var id: String
        /// "original", "notes" or "performance".
        var kind: String
        /// The original whose group it is in, the working notes' too (PDF before Notes, #582 item 2); nil for Notes of no original here.
        var group: String?
        var working: Bool
        var hidden: Bool
        /// What made it, for its pictogram: the Notes' operation, "pdf" or "recording".
        var op: String
    }

    /// The working notes' id; nil for a piece that is only its PDF.
    var working: String?
    var originals: [Original] = []
    var notes: [Notes] = []
    var views: [ViewEntry] = []
    /// How many views the player hid from the switch.
    var hiddenCount = 0

    init() {}

    /// The sources music-core answers (`SourceLayersAnswer`), their times read as dates.
    init(_ answer: SourceLayersAnswer) {
        working = answer.working
        originals = answer.originals.map {
            Original(
                id: $0.id, kind: $0.kind, shown: $0.shown, fileName: $0.fileName, state: $0.state, removedAt: VersionEntry.date($0.removedAt),
                kept: $0.kept, pages: $0.pages, durationMs: $0.durationMs, hidden: $0.hidden, notes: $0.notes, pageMarks: $0.pageMarks
            )
        }
        notes = answer.notes.map {
            Notes(
                id: $0.id, role: $0.role, name: $0.name, op: $0.op, engine: $0.engine, at: VersionEntry.date($0.at), original: $0.original,
                state: $0.state, edits: $0.edits, taken: $0.taken, hidden: $0.hidden
            )
        }
        views = answer.views.map { ViewEntry(id: $0.id, kind: $0.kind, group: $0.group, working: $0.working, hidden: $0.hidden, op: $0.op) }
        hiddenCount = answer.hidden
    }

    /// The sources in the answer's text of one of the sheet's operations; nil for a refusal or an answer that cannot be
    /// read.
    init?(answer: String) {
        guard let read = FacadeAnswer.decode(SourceLayersAnswer.self, answer, of: "sources.layers") else { return nil }
        self.init(read)
    }

    /// The views the switch shows: every one the player did not hide.
    var shownViews: [ViewEntry] { views.filter { !$0.hidden } }

    func original(_ id: String?) -> Original? {
        guard let id else { return nil }
        return originals.first { $0.id == id }
    }

    func notes(_ id: String?) -> Notes? {
        guard let id else { return nil }
        return notes.first { $0.id == id }
    }

    /// The originals the sheet counts: deleted ones are listed, never counted.
    var liveCount: Int { originals.filter { !$0.isRemoved }.count }

    /// The originals in the sheet's order: as music-core lists them, the tombstones after the live ones.
    var listedOriginals: [Original] { originals.filter { !$0.isRemoved } + originals.filter(\.isRemoved) }

    /// The Notes in the sheet's order: the working notes first.
    var listedNotes: [Notes] { notes.filter(\.isWorking) + notes.filter { !$0.isWorking } }

    /// The sheet's groups: each original with its Notes in music-core's order, the group of the working notes first, then
    /// the others as `listedOriginals` has them (the tombstones last).
    var groups: [Group] {
        let all = listedOriginals.map { original in Group(original: original, notes: original.notes.compactMap { notes($0) }) }
        let working = { (group: Group) in group.notes.contains(where: \.isWorking) }
        return all.filter(working) + all.filter { !working($0) }
    }

    /// The Notes made from no original of this piece (a version from a file), after the groups, the working notes first.
    var looseNotes: [Notes] {
        let grouped = Set(originals.flatMap(\.notes))
        return listedNotes.filter { !grouped.contains($0.id) }
    }

    // MARK: - Words and pictograms

    /// The pictogram of what made Notes (sources sheet, new icons): a PDF read, a recording heard, an assembly, a file.
    static func pictogram(op: String) -> String {
        switch op {
        case "pdfRecognition": "fromPdf"
        case "audioRecognition": "fromRec"
        case "assembly": "assemble"
        case "pdf": "pages"
        case "recording": "mic"
        default: "fromFile"
        }
    }

    /// An original's pictogram: pages for a PDF or photos, the microphone for a recording, a file for MIDI or MusicXML.
    static func pictogram(kind: String) -> String {
        switch kind {
        case "pdf", "images": "pages"
        case "audio": "mic"
        default: "file"
        }
    }

    /// What VoiceOver says for a Notes' pictogram.
    static func operationName(_ op: String, _ text: UIText) -> String {
        switch op {
        case "pdfRecognition": text["sources.sheet.opPdf"]
        case "audioRecognition": text["sources.sheet.opRec"]
        case "assembly": text["sources.sheet.opAssembly"]
        default: text["sources.sheet.opImport"]
        }
    }

    /// The Notes' title: the file they were made from, else their name, else what made them.
    func title(of notes: Notes, _ text: UIText) -> String {
        if let original = original(notes.original) { return original.shown }
        if let name = notes.name, !name.isEmpty { return name }
        return Self.operationName(notes.op, text)
    }

    /// A view's word in the switch: «PDF», «Ноты» or «Исполнение».
    static func word(_ view: ViewEntry, _ text: UIText) -> String {
        switch view.kind {
        case "original": text["pdf.viewPdfShort"]
        case "performance": text["sources.rolePlaying"]
        default: text["sources.roleNotes"]
        }
    }

    /// A view's pictogram in the switch's menu.
    static func pictogram(_ view: ViewEntry) -> String {
        switch view.kind {
        case "original": "pages"
        case "performance": "mic"
        default: pictogram(op: view.op)
        }
    }

    /// The short date the sheet writes: «13.11» in Russian, «13 Nov» in English.
    static func shortDate(_ date: Date, _ text: UIText) -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: text.language == "ru" ? "ru_RU" : "en_GB")
        formatter.setLocalizedDateFormatFromTemplate(text.language == "ru" ? "ddMM" : "dMMM")
        return formatter.string(from: date)
    }

    /// A recording's length as m:ss.
    static func duration(_ milliseconds: Int) -> String {
        let seconds = max(0, milliseconds / 1000)
        return String(format: "%d:%02d", seconds / 60, seconds % 60)
    }

    /// The caption under an original's name: «PDF · 4 страницы», «Запись · 2:41».
    static func caption(of original: Original, _ text: UIText) -> String {
        switch original.kind {
        case "pdf":
            return original.pages.map { text.count("sources.sheet.pdfPages", $0) } ?? text["pdf.viewPdfShort"]
        case "images":
            return original.pages.map { text.count("sources.sheet.photoPages", $0) } ?? text["sources.sheet.photos"]
        case "audio":
            return original.durationMs.map { text.format("sources.sheet.recordingLength", duration($0)) } ?? text["sources.sheet.recording"]
        case "midi":
            return "MIDI"
        default:
            return "MusicXML"
        }
    }

    /// The caption under Notes' name: «homr 0.7.0 · 13.11 · 14 правок», or «14.11 · 3 переноса».
    static func caption(of notes: Notes, _ text: UIText) -> String {
        var parts: [String] = []
        if let engine = notes.engine, !engine.isEmpty { parts.append(engine) }
        if let at = notes.at { parts.append(shortDate(at, text)) }
        if notes.edits > 0 { parts.append(text.count("sources.sheet.edits", notes.edits)) }
        if notes.taken > 0 { parts.append(text.count("sources.sheet.carries", notes.taken)) }
        return parts.joined(separator: " · ")
    }

    /// A count's text with more than the number filled in ({0} the number, {1}… the values), in the language's plural form.
    static func counted(_ key: String, _ number: Int, _ values: String..., text: UIText) -> String {
        var result = text["\(key).\(UIText.pluralForm(text.language, number))"].replacingOccurrences(of: "{0}", with: String(number))
        for (index, value) in values.enumerated() {
            result = result.replacingOccurrences(of: "{\(index + 1)}", with: value)
        }
        return result
    }
}
