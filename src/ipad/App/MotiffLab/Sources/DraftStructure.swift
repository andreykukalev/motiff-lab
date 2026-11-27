import Foundation

// What a place's card offers to change a draft's structure (v1.4, ADR 0034 section 8; drafts spec «Ноты + запись»), from
// music-core's read-only answers: «Найти на записи» (F2) and a place's length. The steps are `SourcesController`'s
// (SourcesDrafts.swift); nothing here changes the draft.

/// What «Найти на записи» in a place's card says (F2; spec «Ноты + запись»: the engine finds the stretch when it is tapped),
/// from what music-core found on the piece's recordings heard as notes (`drafts.findOnRecording`): the stretch it finds
/// most is put in, unless the place has it already, which is named; another recording's is not taken in its stead (c-3).
enum DraftRecordingOffer: Equatable, Sendable {
    /// Not searched yet: a tap searches.
    case ready
    /// The piece has no recording heard as notes: nothing is searched, the entry is not shown.
    case noRecording
    /// The search is on its way.
    case searching
    /// No recording has the place's music.
    case notFound
    /// music-core did not search: its words, when it gave them (r-1: the line said no recording had the music).
    case failed(String?)
    /// The stretch found most is no variant of the place yet: it is put in.
    case add(DraftRecordingAnswer.Found)
    /// The stretch found most is a variant of the place already: named, nothing added.
    case already(DraftRecordingAnswer.Found)

    /// The offer of music-core's answer, from the stretch it found most only (the first); with no answer, `refused`, its
    /// words, if any.
    init(_ answer: DraftRecordingAnswer?, refused: String? = nil) {
        self.init(found: answer?.found, refused: refused)
    }

    /// The offer of what music-core found (`found`, a place's or a new draft's, v1.5 d-11), as `init(_:refused:)`.
    init(found: [DraftRecordingAnswer.Found]?, refused: String? = nil) {
        guard let found else {
            self = .failed(refused)
            return
        }
        guard let best = found.first else {
            self = .notFound
            return
        }
        self = best.variant == nil ? .add(best) : .already(best)
    }

    /// The offer of what the card «Новый черновик» found (v1.5 d-11), as `init(found:refused:)`; with no recording searched,
    /// the one the passage is selected in left out, nothing to say (E-drafts-1).
    init(new answer: DraftNewRecordingAnswer?, refused: String? = nil) {
        self = answer?.recordings == 0 ? .noRecording : DraftRecordingOffer(found: answer?.found, refused: refused)
    }

    /// The card's offer once its search ends, unless the card left it meanwhile (`cancelled`: the sheet closed, another
    /// part chosen), whose end says nothing (E-drafts-3).
    mutating func settle(_ found: DraftRecordingOffer, cancelled: Bool) {
        if !cancelled { self = found }
    }

    /// The stretch «С ассистентом · Pro» on the card «Новый черновик» puts in (v1.5 d-11): one found, for a new draft
    /// (`into`), on a screen that can open the draft's view (`opensDraft`: one with the switch, E-drafts-2); else nil, and
    /// the button is not shown.
    func assisting(into: DraftChoice.Into?, opensDraft: Bool) -> DraftRecordingAnswer.Found? {
        into == .new && opensDraft ? adds : nil
    }

    /// The stretch to put in; nil where nothing is.
    var adds: DraftRecordingAnswer.Found? {
        if case .add(let found) = self { return found }
        return nil
    }

    /// The entry shows: not on a piece with no recording heard as notes, where there is nothing to search (v1.4 a-1,
    /// answer 9: hidden rather than off).
    var offered: Bool {
        self != .noRecording
    }

    /// The entry takes a tap: not while it searches, nor on a piece with no recording heard.
    var searchable: Bool {
        offered && self != .searching
    }

    /// The line under the entry: why nothing was put in, or that it searches; nil before a tap, once found and where the
    /// entry is not shown.
    func line(_ text: UIText) -> String? {
        switch self {
        case .ready, .add, .noRecording: nil
        case .searching: text["drafts.recordingSearching"]
        case .notFound: text["drafts.recordingNotFound"]
        case .failed(let refused): refused ?? text["drafts.recordingFailed"]
        case .already(let found): text.format("drafts.recordingAlready", found.label)
        }
    }

    /// The line of the card «Новый черновик» (v1.5 d-11; spec «Ноты + запись»: «место на записи, % совпадения, число
    /// приёмов»): where on the recording the passage is, how much of it, and how many приёмы the place would have; that it
    /// searches, or why nothing was found; nil before the search and on a piece with no recording heard.
    func newDraftLine(_ text: UIText) -> String? {
        switch self {
        case .ready, .noRecording: nil
        case .searching: text["drafts.recordingSearching"]
        case .notFound: text["drafts.newNotFound"]
        case .failed(let refused): refused ?? text["drafts.recordingFailed"]
        case .add(let found), .already(let found):
            text.format("drafts.newOnRecording", found.label, String(found.percent), text.count("drafts.techniqueCount", found.techniques ?? 0))
        }
    }
}

/// A place's length being chosen in its card (v1.4): the bars of its source its base is cut from now (`now`) and those
/// asked (`first`, `last`), kept within the bars its Notes have (`most`) and at least one bar long, as music-core takes a
/// resize; «Изменить длину» only once they differ from `now`.
struct DraftLengthChoice: Equatable, Sendable {
    let now: DraftLengthAnswer.Bars
    let most: DraftLengthAnswer.Bars
    private(set) var first: Int
    private(set) var last: Int

    /// The choice music-core's answer allows; nil for a place whose length cannot change, or whose bars are not known.
    init?(_ answer: DraftLengthAnswer?) {
        guard let answer, answer.resizable, let now = answer.bars, let most = answer.most else { return nil }
        self.now = now
        self.most = most
        first = now.first
        last = now.last
    }

    /// The bars the first bar may be: from the Notes' first to the last bar asked.
    var firstBars: ClosedRange<Int> { most.first...max(most.first, last) }

    /// The bars the last bar may be: from the first bar asked to the Notes' last.
    var lastBars: ClosedRange<Int> { min(first, most.last)...most.last }

    /// The first bar `by` bars on, kept within `firstBars`.
    mutating func step(first by: Int) {
        first = min(max(first + by, firstBars.lowerBound), firstBars.upperBound)
    }

    /// The last bar `by` bars on, kept within `lastBars`.
    mutating func step(last by: Int) {
        last = min(max(last + by, lastBars.lowerBound), lastBars.upperBound)
    }

    /// The bars asked; nil while they are the place's own, which music-core does not take as a resize.
    var asked: ClosedRange<Int>? {
        first == now.first && last == now.last ? nil : first...last
    }
}
