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

    /// The phrases music-core answers (`PhrasesAnswer`, `Answer.Phrases`).
    init(_ answer: PhrasesAnswer) {
        level = answer.level
        var counts: [String: Int] = [:]
        for entry in answer.levels ?? [] {
            if let id = entry.id { counts[id] = entry.count ?? 0 }
        }
        self.counts = counts
        edited = Set(Self.ids.filter { id in
            let level = answer.edits?[id]
            return !(level?.added ?? []).isEmpty || !(level?.removed ?? []).isEmpty
        })
        units = answer.units.compactMap(PhraseUnit.init)
    }

    /// The phrases in a facade answer's text (`phrases`, `phrases.edit`), for the calls not yet made through `PieceCore`; nil
    /// for a refusal or an answer that cannot be read.
    init?(answer: String) {
        guard let answer = FacadeAnswer.decode(PhrasesAnswer.self, answer, of: "phrases") else { return nil }
        self.init(answer)
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

    /// A unit music-core answers; nil without its number, onset or end.
    init?(_ unit: PhrasesAnswer.Unit) {
        guard let number = unit.number, let onset = unit.onset, let end = unit.end else { return nil }
        self.number = number
        name = unit.name ?? ""
        firstBar = unit.firstBar ?? 0
        lastBar = unit.lastBar ?? 0
        self.onset = onset
        self.end = end
        selectableOnset = unit.selectable?.onset ?? onset
        selectableEnd = unit.selectable?.end ?? end
        provenance = unit.start?.provenance ?? "inferred"
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
    /// a selection made without them (music-core always writes them).
    var firstBeat: Int?
    var lastBeat: Int?
    /// The one selection (v0.6.2, #601, PlaceSelection.swift): the hand it is narrowed to ("right", "left", music-core's
    /// filter), nil for both; and the notes picked (a tap on a note, a box on the roll), nil for a passage.
    var hand: String?
    var picked: [String]?

    init(onset: Double, end: Double, firstBar: Int, lastBar: Int, noteIds: [String] = [], firstBeat: Int? = nil, lastBeat: Int? = nil, hand: String? = nil, picked: [String]? = nil) {
        self.onset = onset
        self.end = end
        self.firstBar = firstBar
        self.lastBar = lastBar
        self.noteIds = noteIds
        self.firstBeat = firstBeat
        self.lastBeat = lastBeat
        self.hand = hand
        self.picked = picked
    }

    /// The selection music-core answers (`SelectionAnswer`, `Answer.Selection`), narrowed to the hand its filter names.
    init(_ answer: SelectionAnswer.Selection) {
        self.init(
            onset: answer.onset, end: answer.end, firstBar: answer.firstBar, lastBar: answer.lastBar, noteIds: answer.noteIds,
            firstBeat: answer.firstBeat, lastBeat: answer.lastBeat, hand: answer.filter.hand
        )
    }

    /// "Bar 5, beat 2 → bar 8, beat 3", for a selection by beats (#349); nil without the beats.
    func beatSpan(_ text: UIText) -> String? {
        guard let firstBeat, let lastBeat else { return nil }
        return text.format("selection.beatSpan", String(firstBar), String(firstBeat), String(lastBar), String(lastBeat))
    }
}
