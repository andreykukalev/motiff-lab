import Foundation

/// A section of the form as music-core letters it (`Answer.FormSection`: the `dna` map's form lane and the `phrases`
/// answer's sections): A, B, A′…, where it is, which sections it shares its music with, and what a tap on it selects.
/// One type for Piece DNA, the Contents' structure and the letters on the score.
struct FormSection: Sendable, Identifiable, Equatable {
    var id: String
    var label: String
    var onset: Double
    var end: Double
    var firstBar: Int
    var lastBar: Int
    var sharesWith: [String]
    /// What a tap on the section selects: the whole section, or its first bars when it runs longer than a selection holds.
    var selectableOnset: Double
    var selectableEnd: Double
}

extension FormSection {
    /// A section of the `phrases` answer.
    init(_ section: PhrasesAnswer.Section) {
        self.init(
            id: section.id, label: section.label, onset: section.passage.onset, end: section.passage.end,
            firstBar: section.passage.firstBar, lastBar: section.passage.lastBar, sharesWith: section.sharesWith ?? [],
            selectableOnset: section.selectable?.onset ?? section.passage.onset, selectableEnd: section.selectable?.end ?? section.passage.end
        )
    }
}
