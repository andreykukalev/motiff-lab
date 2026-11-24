import Foundation

/// Another engine's reading of a scan's bar to check (decision record 0035 section 6, v1.3 i-2), as music-core's
/// `scan.join` answered it: the engines that read it and their notes on the bar's staves to check. Chosen, it goes back to
/// music-core as it is (`corrections.variant`), which puts it in that bar only as the player's corrections.
struct ScanVariant: Codable, Equatable, Sendable {
    struct Note: Codable, Equatable, Sendable {
        /// Ticks from the bar's start, MIDI pitch, ticks long; the name music-core gave ("G5").
        var onset: Int
        var pitch: Int
        var length: Int
        var name: String
    }

    struct Staff: Codable, Equatable, Sendable {
        var part: Int
        var staff: Int
        var notes: [Note]
    }

    var engines: [String]
    var staves: [Staff]

    /// The variant as `scan.join` answered it; nil when it is not one.
    init?(_ object: [String: Any]) {
        guard let engines = object["engines"] as? [String], let staves = object["staves"] as? [[String: Any]] else { return nil }
        self.engines = engines
        self.staves = staves.compactMap { staff in
            guard let part = staff["part"] as? Int, let number = staff["staff"] as? Int else { return nil }
            let notes = (staff["notes"] as? [[String: Any]] ?? []).compactMap { note -> Note? in
                guard let onset = note["onset"] as? Int, let pitch = note["pitch"] as? Int, let length = note["length"] as? Int else { return nil }
                return Note(onset: onset, pitch: pitch, length: length, name: note["name"] as? String ?? "")
            }
            return Staff(part: part, staff: number, notes: notes)
        }
    }

    /// `corrections.variant`'s fields for the bar: the staves as music-core gave them.
    func fields(bar: Int) -> [String: Any] {
        ["bar": bar, "staves": staves.map { staff in
            ["part": staff.part, "staff": staff.staff, "notes": staff.notes.map { ["onset": $0.onset, "pitch": $0.pitch, "length": $0.length] }] as [String: Any]
        }]
    }

    /// «G5 A5 B5 C5», a staff's notes by name in order (a staff with none `rest`, «пауза»), the staves apart by « · ».
    func names(rest: String) -> String {
        staves.map { $0.notes.isEmpty ? rest : $0.notes.map(\.name).joined(separator: " ") }.joined(separator: " · ")
    }
}

/// A scan's bars to check with what the panel shows of them (0035 section 6): each bar's other readings and whether its
/// crop of the page is kept. Kept only on this iPad, beside its other device state, by the fingerprint of the notes they
/// were read into (music-core's `scan.join` and `check` "fingerprint"); never in the project file, never sent.
struct ScanReview: Codable, Equatable, Sendable {
    struct Bar: Codable, Equatable, Sendable {
        var bar: Int
        var variants: [ScanVariant]
        /// Whether `ScanReviewStore.crop` has the bar's crop of its page.
        var cropped: Bool
    }

    var fingerprint: String
    var bars: [Bar]

    func bar(_ number: Int) -> Bar? { bars.first { $0.bar == number } }
}

/// The variant of a scan's bar the player chose last (v1.3 i-2): one Undo (`corrections.undoDraft`) takes it back whole
/// only while nothing else changed the notes since, so any other edit, or other working notes, end it.
struct ScanVariantChoice: Equatable, Sendable {
    private(set) var bar: Int?
    private var chosenEngines: [String] = []
    /// The fingerprint of the notes it was chosen on.
    private var notes: String?

    mutating func chose(bar: Int, engines: [String], notes: String?) {
        (self.bar, chosenEngines, self.notes) = (bar, engines, notes)
    }

    /// The engines of the variant chosen in `bar`, while its Undo holds; nil otherwise.
    func engines(in bar: Int) -> [String]? { self.bar == bar ? chosenEngines : nil }

    /// Another edit of the notes: the choice can no longer be undone whole.
    mutating func edited() { self = ScanVariantChoice() }

    /// The working notes read again: the choice holds only for the notes it was made on.
    mutating func notesRead(_ fingerprint: String?) {
        if fingerprint != notes { self = ScanVariantChoice() }
    }
}

/// Where the scans' bars to check are kept on this iPad: a folder a fingerprint, with `review.json` and each bar's crop as
/// `bar-<n>.png`; the newest `kept` are kept.
enum ScanReviewStore {
    static let kept = 40

    static var root: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("ScanReview", isDirectory: true)
    }

    /// Writes the review and its crops (bar number to PNG), and forgets the oldest beyond `kept`.
    static func save(_ review: ScanReview, crops: [Int: Data], in root: URL = root) {
        let folder = root.appendingPathComponent(safe(review.fingerprint), isDirectory: true)
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        for (bar, png) in crops { try? png.write(to: folder.appendingPathComponent("bar-\(bar).png"), options: .atomic) }
        guard let data = try? JSONEncoder().encode(review) else { return }
        try? data.write(to: folder.appendingPathComponent("review.json"), options: .atomic)
        let folders = (try? FileManager.default.contentsOfDirectory(at: root, includingPropertiesForKeys: [.contentModificationDateKey])) ?? []
        let newest = folders.sorted { modified($0) > modified($1) }
        for old in newest.dropFirst(kept) { try? FileManager.default.removeItem(at: old) }
    }

    /// The review kept for notes of this fingerprint; nil for notes read elsewhere, before version 1.3 or forgotten.
    static func load(_ fingerprint: String, in root: URL = root) -> ScanReview? {
        let url = root.appendingPathComponent(safe(fingerprint), isDirectory: true).appendingPathComponent("review.json")
        guard let data = try? Data(contentsOf: url) else { return nil }
        return try? JSONDecoder().decode(ScanReview.self, from: data)
    }

    /// A bar's crop of its page, as PNG.
    static func crop(_ fingerprint: String, bar: Int, in root: URL = root) -> Data? {
        try? Data(contentsOf: root.appendingPathComponent(safe(fingerprint), isDirectory: true).appendingPathComponent("bar-\(bar).png"))
    }

    private static func safe(_ fingerprint: String) -> String {
        String(fingerprint.filter { $0.isASCII && ($0.isLetter || $0.isNumber) }.prefix(64))
    }

    private static func modified(_ url: URL) -> Date {
        (try? url.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast
    }
}
