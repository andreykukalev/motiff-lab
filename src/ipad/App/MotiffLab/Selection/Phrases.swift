import Foundation

/// The phrases of a piece (facade `phrases`, the phrases spec, v1.2): the unit of work. Each start is marked on the score
/// (ScorePhraseMarks.swift); focus steps by them, and a selection that is one exactly is named by it.
struct PiecePhrases: Equatable, Sendable {
    var units: [PhraseUnit]
    /// The sections of the form, A, B, A′…, each with its letter at its start on the score.
    var sections: [FormSection]
    /// The player's edits of the boundaries now, and before the edit that answered them: one step of their Undo.
    var edits: PhraseLevelEdits?
    var previous: PhraseLevelEdits?
    /// How many boundaries are the player's own: the reset's warning «Ваших границ: N» (the phrases spec, «Player's
    /// boundaries» 2).
    var own: Int
    /// The phrases the edit that answered them changed, as they were and the phrase now that covers most of each.
    var changed: [PhrasesAnswer.Change]
    /// The phrase anchors ideas and resume points are attached to (#318), each with the number of the phrase it is now, and
    /// how many match no phrase since the notes or their analysis changed (the Insights line «Границы фраз изменились»).
    var anchors: [PhrasesAnswer.Anchor]
    var unmatched: Int
    /// The number of the step of the edit that answered them, which its Undo and Redo send back (nil for none), and how
    /// many anchors `phrases.reattach` attached again.
    var step: Int?
    var reattached: Int?

    /// The phrases music-core answers (`PhrasesAnswer`, `Answer.Phrases`).
    init(_ answer: PhrasesAnswer) {
        units = answer.units.compactMap(PhraseUnit.init)
        sections = (answer.sections ?? []).map(FormSection.init)
        edits = answer.edits
        previous = answer.previous
        own = answer.own ?? 0
        changed = answer.changed ?? []
        anchors = answer.anchors
        unmatched = answer.unmatched
        step = answer.step
        reattached = answer.reattached
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

    /// The phrase the anchor `id` is now (#318: its number from "anchors"); nil for no anchor, or one that matches no phrase.
    func unit(anchor id: String?) -> PhraseUnit? {
        anchors.first { $0.id == id }?.phrase.flatMap { unit($0) }
    }

    /// The phrase that holds this tick.
    func unit(at tick: Double) -> PhraseUnit? {
        units.first { $0.onset <= tick && tick < $0.end }
    }

    /// The phrase a selection lies inside without being it exactly, which «Вся фраза» selects (the phrases spec, decision 5,
    /// from option C); nil for a phrase selected exactly or a passage across a boundary.
    func unit(enclosing selection: ScoreSelection?) -> PhraseUnit? {
        guard let selection, unit(selected: selection) == nil, let holding = unit(at: selection.onset), selection.end <= holding.end else {
            return nil
        }
        return holding
    }

    /// The change of the phrase a selection was exactly before the edit that answered these phrases, whose new boundaries the
    /// card «Выделить её по новым границам?» offers (the phrases spec, «Player's boundaries» 4); nil when the selection was
    /// no phrase, or its phrase did not change.
    func change(selected selection: ScoreSelection?) -> PhrasesAnswer.Change? {
        guard let selection else { return nil }
        return changed.first { $0.before.span == [selection.onset, selection.end] }
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
    /// For a found start the player moved: where it was found, its tick (the faint comma there), bar and beat
    /// («Вернуть найденную · 4.3»); nil for any other start.
    var found: PhrasesAnswer.Found?
    /// Its motifs in time order, from music-core (decision 2): small commas on the card in focus, a tap selects one.
    var motifs: [PhrasesAnswer.Motif]

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
        found = unit.start?.found
        motifs = unit.motifs ?? []
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
