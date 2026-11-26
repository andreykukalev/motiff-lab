import Foundation

/// A change of the highlights by the pen's colour bars or its eraser (finding 31), by value: the highlights it takes away
/// and the ones it lays, each by its span, colour and notes, never by id, since music-core gives a highlight laid again a
/// new id. `Marks/HighlightPaint` works it out and finds the ids of what it takes; the pen keeps it in the step of its
/// history it belongs to (InkStep, v1.4 i-5), so its Undo lays back what was taken (`reversed`) and its Redo does it again.
struct Recolouring: Equatable, Sendable {
    /// A highlight by value: its span in ticks, its ink («amber», «carmine», «forest» or «ochre») and the notes it names.
    struct Highlight: Equatable, Sendable {
        var onset: Double
        var end: Double
        var colour: String
        var notes: [String] = []
    }

    /// The highlights taken away, then the ones laid.
    var taken: [Highlight] = []
    var laid: [Highlight] = []

    var isEmpty: Bool { taken.isEmpty && laid.isEmpty }

    /// The change that takes this one back: what it laid goes, what it took comes back as it was.
    var reversed: Recolouring { Recolouring(taken: laid, laid: taken) }
}
