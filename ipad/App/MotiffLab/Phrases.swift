import Foundation

/// The phrases of a piece at the level shown (facade `phrases`, 0018 section 3): Short, Phrases or Sentences. They are
/// no longer drawn as brackets above the top staff (Andrei, 2026-11-13, #479 item 14); focus steps by
/// them and a selection is named by them.
struct PhraseLevelsShown: Equatable, Sendable {
    /// "short", "phrase" or "sentence".
    var level: String
    /// How many units each level has, by its id.
    var counts: [String: Int]
    var units: [PhraseUnit]
    /// The levels the player changed (boundaries added or taken away, in the Music Studio), which can be reset.
    var edited: Set<String> = []

    static let ids = ["short", "phrase", "sentence"]

    /// The phrases in a facade answer (`phrases`, `phrases.level`).
    init?(answer: String) {
        guard let object = facadeObject(answer), let level = object["level"] as? String,
              let units = object["units"] as? [[String: Any]] else { return nil }
        self.level = level
        var counts: [String: Int] = [:]
        for entry in object["levels"] as? [[String: Any]] ?? [] {
            if let id = entry["id"] as? String { counts[id] = (entry["count"] as? NSNumber)?.intValue ?? 0 }
        }
        self.counts = counts
        let edits = object["edits"] as? [String: Any] ?? [:]
        self.edited = Set(Self.ids.filter { id in
            let level = edits[id] as? [String: Any] ?? [:]
            return ["added", "removed"].contains { !((level[$0] as? [Any]) ?? []).isEmpty }
        })
        self.units = units.compactMap(PhraseUnit.init(answer:))
    }

    /// The unit with this number, from 1.
    func unit(_ number: Int) -> PhraseUnit? {
        units.first { $0.number == number }
    }
}

/// One unit of a level: its number, the bars and ticks it covers, the span a tap selects, and how its start was found.
struct PhraseUnit: Equatable, Sendable {
    var number: Int
    var name: String
    var firstBar: Int
    var lastBar: Int
    var onset: Double
    var end: Double
    var selectableOnset: Double
    var selectableEnd: Double
    /// "imported", "inferred", "userEdited", …: the provenance of its start.
    var provenance: String

    init?(answer: [String: Any]) {
        func number(_ value: Any?) -> Double? { (value as? NSNumber)?.doubleValue }
        guard let unit = number(answer["number"]), let onset = number(answer["onset"]), let end = number(answer["end"]) else { return nil }
        self.number = Int(unit)
        name = answer["name"] as? String ?? ""
        firstBar = Int(number(answer["firstBar"]) ?? 0)
        lastBar = Int(number(answer["lastBar"]) ?? 0)
        self.onset = onset
        self.end = end
        let selectable = answer["selectable"] as? [String: Any]
        selectableOnset = number(selectable?["onset"]) ?? onset
        selectableEnd = number(selectable?["end"]) ?? end
        provenance = (answer["start"] as? [String: Any])?["provenance"] as? String ?? "inferred"
    }
}

/// The passage selected on the score (facade `select`): where it starts and ends, its bars and its notes.
struct ScoreSelection: Equatable, Sendable {
    var onset: Double
    var end: Double
    var firstBar: Int
    var lastBar: Int
    var noteIds: [String]
    /// The beat of the first bar it starts on and of the last bar it ends in, as music-core counts them (#349); nil from
    /// an older answer.
    var firstBeat: Int?
    var lastBeat: Int?

    init(onset: Double, end: Double, firstBar: Int, lastBar: Int, noteIds: [String] = [], firstBeat: Int? = nil, lastBeat: Int? = nil) {
        self.onset = onset
        self.end = end
        self.firstBar = firstBar
        self.lastBar = lastBar
        self.noteIds = noteIds
        self.firstBeat = firstBeat
        self.lastBeat = lastBeat
    }

    /// The selection in a facade answer: {"selection": {onset, end, firstBar, lastBar, firstBeat, lastBeat, noteIds, …}}.
    init?(answer: String) {
        guard let selection = facadeObject(answer)?["selection"] as? [String: Any],
              let onset = (selection["onset"] as? NSNumber)?.doubleValue,
              let end = (selection["end"] as? NSNumber)?.doubleValue else { return nil }
        self.init(
            onset: onset,
            end: end,
            firstBar: (selection["firstBar"] as? NSNumber)?.intValue ?? 0,
            lastBar: (selection["lastBar"] as? NSNumber)?.intValue ?? 0,
            noteIds: selection["noteIds"] as? [String] ?? [],
            firstBeat: (selection["firstBeat"] as? NSNumber)?.intValue,
            lastBeat: (selection["lastBeat"] as? NSNumber)?.intValue
        )
    }

    /// "Bar 5, beat 2 → bar 8, beat 3", for a selection by beats (#349); nil without the beats.
    func beatSpan(_ text: UIText) -> String? {
        guard let firstBeat, let lastBeat else { return nil }
        return text.format("selection.beatSpan", String(firstBar), String(firstBeat), String(lastBar), String(lastBeat))
    }
}
