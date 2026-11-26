import Foundation

// What a place's card offers to change a draft's structure (v1.4, ADR 0034 section 8; drafts spec «Ноты + запись»), from
// music-core's read-only answers: «Найти на записи» (F2) and the place's length. The steps are `SourcesController`'s
// (SourcesDrafts.swift); nothing here changes the draft.

/// What «Найти на записи» in a place's card offers (F2), from what music-core found on the piece's recordings heard as
/// notes (`drafts.findOnRecording`): the entry adds a stretch only where one is found that the place does not have yet;
/// otherwise it is off and its line says why.
enum DraftRecordingOffer: Equatable, Sendable {
    /// The piece has no recording heard as notes: nothing is searched.
    case noRecording
    /// The search is on its way.
    case searching
    /// No recording has the place's music, or music-core did not answer.
    case notFound
    /// A stretch found that is no variant of the place yet: the entry puts it in.
    case add(DraftRecordingAnswer.Found)
    /// Every stretch found is a variant of the place already: named, not added twice.
    case already(DraftRecordingAnswer.Found)

    /// The offer of music-core's answer: the stretch found most of that the place does not have yet, else the first one
    /// found, which it has.
    init(_ answer: DraftRecordingAnswer?) {
        let found = answer?.found ?? []
        if let new = found.first(where: { $0.variant == nil }) {
            self = .add(new)
        } else if let first = found.first {
            self = .already(first)
        } else {
            self = .notFound
        }
    }

    /// The stretch the entry puts in; nil while it is off.
    var adds: DraftRecordingAnswer.Found? {
        if case .add(let found) = self { return found }
        return nil
    }

    /// The line under the entry: the stretch and how much of it is the place's music, or why the entry is off.
    func line(_ text: UIText) -> String {
        switch self {
        case .noRecording: text["drafts.recordingNone"]
        case .searching: text["drafts.recordingSearching"]
        case .notFound: text["drafts.recordingNotFound"]
        case .add(let found): text.format("drafts.sameMusic", found.label, String(found.percent))
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
        self.init(now: now, most: most)
    }

    /// The place's own bars `now`, within `most`, none stepped yet.
    init(now: DraftLengthAnswer.Bars, most: DraftLengthAnswer.Bars) {
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
