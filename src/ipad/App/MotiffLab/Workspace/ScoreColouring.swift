import Foundation

/// The pen's colour bars on the score (finding 31, v0.10): what the page coloured (`coloured`, PenModel.colour) and what
/// the eraser took the colour from (PenModel.endRub) become music-core's highlights (HighlightPaint), each change sent
/// as `highlight.remove` and `highlight.add` and drawn once, at its last call, and handed back to the pen, which keeps it
/// in its history and sends it again, or reversed, for its Redo and Undo (`recolour`, v1.4 i-5), drawn by the step's ink
/// call after it when there is one. The selection never changes. In the pen's ink queue (PenModel), so each change meets the highlights as the one before left them.
extension ScoreController {
    func colouringChanged() {
        applyInput()
    }

    func colourBars(_ span: Range<Double>, in colour: String) async -> Recolouring {
        await paint(HighlightPaint.colouring(span, colour, over: marksModel.marks.highlights))
    }

    func uncolourBars(_ spans: [Range<Double>], draws: Bool) async -> Recolouring {
        await paint(HighlightPaint.uncolouring(spans, over: marksModel.marks.highlights), draws: draws)
    }

    func recolour(_ change: Recolouring, draws: Bool) async {
        _ = await paint(change, draws: draws)
    }

    /// Sends the change, its answer drawn at its last call unless `draws` is false, and answers what was sent: nothing
    /// without a piece open.
    private func paint(_ change: Recolouring, draws: Bool = true) async -> Recolouring {
        guard let piece, !change.isEmpty else { return Recolouring() }
        let removed = HighlightPaint.ids(of: change.taken, over: marksModel.marks.highlights)
        for (index, id) in removed.enumerated() {
            await changeMarks(piece.id, "highlight.remove", ["id": id], draws: draws && change.laid.isEmpty && index == removed.count - 1)
        }
        for (index, laid) in change.laid.enumerated() {
            var request: [String: Any] = ["span": ["onset": Int(laid.onset), "end": Int(laid.end)], "colour": laid.colour]
            if !laid.notes.isEmpty { request["notes"] = laid.notes }
            await changeMarks(piece.id, "highlight.add", request, draws: draws && index == change.laid.count - 1)
        }
        return change
    }
}
