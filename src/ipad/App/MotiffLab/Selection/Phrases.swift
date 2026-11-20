import Foundation

/// The phrases of a piece (facade `phrases`, the phrases spec, v1.2): the unit of work. Each start is marked on the score
/// (ScorePhraseMarks.swift); focus steps by them, and a selection that is one exactly is named by it.
struct PiecePhrases: Equatable, Sendable {
    var units: [PhraseUnit]
    /// The sections of the form, A, B, A′…, each with its letter at its start on the score.
    var sections: [PhraseSection]

    /// The phrases music-core answers (`PhrasesAnswer`, `Answer.Phrases`).
    init(_ answer: PhrasesAnswer) {
        units = answer.units.compactMap(PhraseUnit.init)
        sections = (answer.sections ?? []).map(PhraseSection.init)
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

    /// The phrase a selection is exactly, its selectable span (the phrases spec, decision 1); nil for any other passage.
    func unit(selected selection: ScoreSelection?) -> PhraseUnit? {
        guard let selection else { return nil }
        return units.first { $0.selectableOnset == selection.onset && $0.selectableEnd == selection.end }
    }
}

/// One phrase: its number, the bars and ticks it covers, the span a tap selects, and how its start was found.
struct PhraseUnit: Equatable, Sendable {
    var number: Int
    var name: String
    var firstBar: Int
    var lastBar: Int
    var onset: Double
    var end: Double
    var selectableOnset: Double
    var selectableEnd: Double
    /// Whether its start is the player's own (moved, set or confirmed: "userEdited", "userConfirmed"), drawn in amber;
    /// else it was found ("imported", "inferred").
    var own: Bool
    /// The bar and beat of its start note, as music-core counts them.
    var startBar: Int
    var startBeat: Int
    /// «Почему здесь»: how its start was found, in the player's language, completing «начинается …».
    var why: String

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
        own = unit.start?.provenance.map { ["userEdited", "userConfirmed"].contains($0) } ?? false
        startBar = unit.start?.bar ?? firstBar
        startBeat = unit.start?.beat ?? 1
        why = unit.start?.why ?? ""
    }
}

/// A section of the form: its id and letter, where it starts, its bars, and the span a tap on its letter selects.
struct PhraseSection: Equatable, Sendable {
    var id: String
    var letter: String
    var onset: Double
    var firstBar: Int
    var lastBar: Int
    var selectableOnset: Double
    var selectableEnd: Double

    init(_ section: PhrasesAnswer.Section) {
        id = section.id
        letter = section.label
        onset = section.passage.onset
        firstBar = section.passage.firstBar
        lastBar = section.passage.lastBar
        selectableOnset = section.selectable?.onset ?? section.passage.onset
        selectableEnd = section.selectable?.end ?? section.passage.end
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
