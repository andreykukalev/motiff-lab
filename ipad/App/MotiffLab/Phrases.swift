import Foundation

/// The phrases of a piece at the level shown (facade `phrases`, 0018 section 3): Short, Phrases or Sentences, drawn as
/// pencil brackets above the top staff (Web/score.js) that select their unit when tapped.
struct PhraseLevelsShown: Equatable, Sendable {
    /// "short", "phrase" or "sentence".
    var level: String
    /// How many units each level has, by its id.
    var counts: [String: Int]
    var units: [PhraseUnit]

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

    /// How its start was found, as the bracket's class, as the web app has it: the start of the piece, the player's own,
    /// inferred, or imported from the file.
    var kind: String {
        if number == 1 { return "start" }
        switch provenance {
        case "userEdited", "userConfirmed": return "yours"
        case "inferred", "detected": return "inferred"
        default: return "imported"
        }
    }
}

/// The passage selected on the score (facade `select`): where it starts and ends, and its bars.
struct ScoreSelection: Equatable, Sendable {
    var onset: Double
    var end: Double
    var firstBar: Int
    var lastBar: Int

    init(onset: Double, end: Double, firstBar: Int, lastBar: Int) {
        self.onset = onset
        self.end = end
        self.firstBar = firstBar
        self.lastBar = lastBar
    }

    /// The selection in a facade answer: {"selection": {onset, end, firstBar, lastBar, …}}.
    init?(answer: String) {
        guard let selection = facadeObject(answer)?["selection"] as? [String: Any],
              let onset = (selection["onset"] as? NSNumber)?.doubleValue,
              let end = (selection["end"] as? NSNumber)?.doubleValue else { return nil }
        self.init(
            onset: onset,
            end: end,
            firstBar: (selection["firstBar"] as? NSNumber)?.intValue ?? 0,
            lastBar: (selection["lastBar"] as? NSNumber)?.intValue ?? 0
        )
    }
}
