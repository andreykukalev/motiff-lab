import Foundation

/// The pen's colour bars on the score (finding 31, v0.10): what the page coloured (`coloured`, PenModel.colour) and what
/// the eraser took the colour from (PenModel.endRub) become music-core's highlights (HighlightPaint), each change sent
/// as `highlight.remove` and `highlight.add` and drawn once, at its last call. The selection never changes. In the pen's
/// ink queue (PenModel), so each change meets the highlights as the one before left them.
extension ScoreController {
    func colouringChanged() {
        applyInput()
    }

    func colourBars(_ span: Range<Double>, in colour: String) async {
        await paint(HighlightPaint.colouring(span, colour, over: marksModel.marks.highlights))
    }

    func uncolourBars(_ spans: [Range<Double>]) async {
        await paint(HighlightPaint.uncolouring(spans, over: marksModel.marks.highlights))
    }

    private func paint(_ change: HighlightPaint.Change) async {
        guard let piece, !change.isEmpty else { return }
        for (index, id) in change.removed.enumerated() {
            await changeMarks(piece.id, "highlight.remove", ["id": id], draws: change.laid.isEmpty && index == change.removed.count - 1)
        }
        for (index, laid) in change.laid.enumerated() {
            let span = ["onset": Int(laid.onset), "end": Int(laid.end)]
            await changeMarks(piece.id, "highlight.add", ["span": span, "colour": laid.colour], draws: index == change.laid.count - 1)
        }
    }
}
