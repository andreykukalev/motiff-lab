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

    /// The answer of `sources.layers`, or of one of the sheet's operations; nil for an error.
    init?(answer: String) {
        guard let object = facadeObject(answer), object["error"] == nil, object["views"] != nil else { return nil }
        func int(_ value: Any?) -> Int? { (value as? NSNumber)?.intValue }
        working = object["working"] as? String
        originals = (object["originals"] as? [[String: Any]] ?? []).compactMap { entry in
            guard let id = entry["id"] as? String else { return nil }
            let fileName = entry["fileName"] as? String ?? ""
            return Original(
                id: id, kind: entry["kind"] as? String ?? "", shown: entry["shown"] as? String ?? fileName, fileName: fileName,
                state: entry["state"] as? String ?? "nowhere", removedAt: VersionEntry.date(entry["removedAt"] as? String),
                kept: entry["kept"] as? String ?? "file", pages: int(entry["pages"]), durationMs: int(entry["durationMs"]),
                hidden: entry["hidden"] as? Bool ?? false, notes: entry["notes"] as? [String] ?? [], pageMarks: int(entry["pageMarks"]) ?? 0
            )
        }
        notes = (object["notes"] as? [[String: Any]] ?? []).compactMap { entry in
            guard let id = entry["id"] as? String else { return nil }
            return Notes(
                id: id, role: entry["role"] as? String ?? "other", name: entry["name"] as? String, op: entry["op"] as? String ?? "",
                engine: entry["engine"] as? String, at: VersionEntry.date(entry["at"] as? String), original: entry["original"] as? String,
                state: entry["state"] as? String, edits: int(entry["edits"]) ?? 0, taken: int(entry["taken"]) ?? 0,
                hidden: entry["hidden"] as? Bool ?? false
            )
        }
        views = (object["views"] as? [[String: Any]] ?? []).compactMap { entry in
            guard let id = entry["id"] as? String else { return nil }
            return ViewEntry(
                id: id, kind: entry["kind"] as? String ?? "notes", group: entry["group"] as? String,
                working: entry["working"] as? Bool ?? false, hidden: entry["hidden"] as? Bool ?? false, op: entry["op"] as? String ?? ""
            )
        }
        hiddenCount = int(object["hidden"]) ?? 0
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
