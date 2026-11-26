import Foundation

/// What the pen's colour bars and the eraser do to the highlights (finding 31, v0.10), as a `Recolouring` that the root
/// sends as music-core's `highlight.remove` and `highlight.add` (span and colour, as the selection menu's «Выделить цветом»
/// laid them): colouring a passage lays it in a colour, so the highlights of other colours under it give way there and the
/// same colour's that overlap or touch it become one with it; a passage that is all that colour already loses it, so a
/// second tap with the same colour takes it off; the eraser takes the colour from what it passed over. A highlight cut
/// keeps its colour in the parts left. Pure: the root (ScoreController) sends the calls and draws music-core's answer.
enum HighlightPaint {
    /// Colours `span` in `colour`; where it is all that colour already, the colour goes from it instead.
    static func colouring(_ span: Range<Double>, _ colour: String, over highlights: [HighlightMark]) -> Recolouring {
        guard !span.isEmpty else { return Recolouring() }
        let same = highlights.filter { $0.colour == colour }
        if covers(same, span) { return uncolouring([span], over: same) }
        var change = uncolouring([span], over: highlights.filter { $0.colour != colour })
        let joined = same.filter { $0.onset <= span.upperBound && $0.end >= span.lowerBound }
        change.taken += joined.map(value)
        let onset = min(span.lowerBound, joined.map(\.onset).min() ?? span.lowerBound)
        let end = max(span.upperBound, joined.map(\.end).max() ?? span.upperBound)
        change.laid.append(Recolouring.Highlight(onset: onset, end: end, colour: colour))
        return change
    }

    /// Takes the colour from `spans`: each highlight that overlaps one goes, and the parts of it outside them are laid again.
    static func uncolouring(_ spans: [Range<Double>], over highlights: [HighlightMark]) -> Recolouring {
        var change = Recolouring()
        for highlight in highlights where highlight.end > highlight.onset && spans.contains(where: { $0.overlaps(highlight.onset..<highlight.end) }) {
            change.taken.append(value(highlight))
            let left = spans.reduce([highlight.onset..<highlight.end]) { parts, span in parts.flatMap { minus($0, span) } }
            change.laid += left.map { Recolouring.Highlight(onset: $0.lowerBound, end: $0.upperBound, colour: highlight.colour) }
        }
        return change
    }

    /// The ids of the highlights a change takes, found by value among the ones there are (music-core gave each its id
    /// when it was laid, maybe since the change was worked out: its Undo and Redo, v1.4 i-5), each id once; one that is
    /// no longer there is left out.
    static func ids(of taken: [Recolouring.Highlight], over highlights: [HighlightMark]) -> [String] {
        var left = highlights
        return taken.compactMap { wanted in
            guard let index = left.firstIndex(where: { value($0) == wanted }) else { return nil }
            return left.remove(at: index).id
        }
    }

    /// A highlight by value, as a change keeps it.
    private static func value(_ highlight: HighlightMark) -> Recolouring.Highlight {
        Recolouring.Highlight(onset: highlight.onset, end: highlight.end, colour: highlight.colour, notes: highlight.notes)
    }

    /// What is left of `part` outside `span`: the part before it and the part after it, the empty ones left out.
    private static func minus(_ part: Range<Double>, _ span: Range<Double>) -> [Range<Double>] {
        let before = part.lowerBound..<max(part.lowerBound, min(part.upperBound, span.lowerBound))
        let after = min(part.upperBound, max(part.lowerBound, span.upperBound))..<part.upperBound
        return [before, after].filter { !$0.isEmpty }
    }

    /// Whether the highlights together cover the whole span.
    private static func covers(_ highlights: [HighlightMark], _ span: Range<Double>) -> Bool {
        var reached = span.lowerBound
        for highlight in highlights.sorted(by: { $0.onset < $1.onset }) where highlight.onset <= reached && highlight.end > reached {
            reached = highlight.end
            if reached >= span.upperBound { return true }
        }
        return false
    }
}
