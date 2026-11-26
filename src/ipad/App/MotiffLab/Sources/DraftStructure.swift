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
    /// The piece has no recording heard as notes: nothing is searched, the entry is off.
    case noRecording
    /// The search is on its way.
    case searching
    /// No recording has the place's music, or music-core did not answer.
    case notFound
    /// The stretch found most is no variant of the place yet: it is put in.
    case add(DraftRecordingAnswer.Found)
    /// The stretch found most is a variant of the place already: named, nothing added.
    case already(DraftRecordingAnswer.Found)

    /// The offer of music-core's answer, from the stretch it found most only (the first).
    init(_ answer: DraftRecordingAnswer?) {
        guard let best = answer?.found.first else {
            self = .notFound
            return
        }
        self = best.variant == nil ? .add(best) : .already(best)
    }

    /// The stretch to put in; nil where nothing is.
    var adds: DraftRecordingAnswer.Found? {
        if case .add(let found) = self { return found }
        return nil
    }

    /// The entry takes a tap: not while it searches, nor on a piece with no recording heard.
    var searchable: Bool {
        self != .noRecording && self != .searching
    }

    /// The line under the entry: why nothing was put in, or that it searches; nil before a tap and once found.
    func line(_ text: UIText) -> String? {
        switch self {
        case .ready, .add: nil
        case .noRecording: text["drafts.recordingNone"]
        case .searching: text["drafts.recordingSearching"]
        case .notFound: text["drafts.recordingNotFound"]
        case .already(let found): text.format("drafts.recordingAlready", found.label)
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

    /// The first bar `by` bars on, kept from the Notes' first to the last bar asked.
    mutating func step(first by: Int) {
        first = min(max(first + by, most.first), last)
    }

    /// The last bar `by` bars on, kept from the first bar asked to the Notes' last.
    mutating func step(last by: Int) {
        last = min(max(last + by, first), most.last)
    }

    /// The bars asked; nil while they are the place's own, which music-core does not take as a resize.
    var asked: ClosedRange<Int>? {
        first == now.first && last == now.last ? nil : first...last
    }
}
