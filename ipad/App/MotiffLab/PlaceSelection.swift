import Foundation

/// Selection by the place of the touch (v0.6.2, #601, the selection study's sections C-F; the experiment
/// `experimental.placeSelection`, off by default): in Select the place a press lands decides what it does, never how long
/// it is held (the page's rule, Scripts/score/selection-places.ts, the same for the score and the roll). Off, the
/// selection works as in version 0.6.1. The selection is one object (Andrei, 2026-11-15): a span by beats, narrowed to a
/// hand (`ScoreSelection.hand`, music-core's filter) or to notes picked (`ScoreSelection.picked`), kept on the device,
/// never in the project file.
enum PlaceSelection {
    static let setting = "experimental.placeSelection"
}

extension ScoreController {
    /// Makes the notes picked the selection (a tap on a note head on the score, a box or a lasso on the roll): music-core
    /// takes the span out to the beats around them; one note or more, within the bars a selection spans. With `add` the
    /// notes join the ones picked now (a box drawn from inside); otherwise they replace the selection, a step of its history
    /// with «Выделение заменено · Вернуть» when `replaces` (the page's change "replace", placeChange), else an edit of the
    /// selection (an edge moved over picked notes narrows them). `gesture` names the page's gesture, as for a drawn passage.
    func selectNotes(_ ids: [String], add: Bool = false, gesture: String? = nil, replaces: Bool = false) {
        let notes = Self.pickedNotes(ids, adding: add ? selection?.picked : nil)
        guard !notes.isEmpty else { return }
        Task { await select(request: SelectionSpan.picking(notes), gesture: gesture, picked: true, replaces: replaces) }
    }

    /// The notes a pick selects: `ids` alone, or after the ones picked before when it adds to them, each once.
    nonisolated static func pickedNotes(_ ids: [String], adding before: [String]?) -> [String] {
        var seen = Set<String>()
        return ((before ?? []) + ids).filter { seen.insert($0).inserted }
    }

    /// Selects a passage by beats narrowed to a hand ("right", "left"; nil for both): a stroke within one staff on the
    /// score (the page's `strokeHand`).
    func selectSpan(_ start: Double, _ end: Double, hand: String?, gesture: String? = nil, replaces: Bool = false) {
        Task { await select(request: SelectionSpan(onset: start, end: end, hand: hand).request, gesture: gesture, replaces: replaces) }
    }
}

extension ScoreController {
    /// What the toast after a clear or a replace says: «Выделение заменено» for a replace by the place rule; otherwise, as
    /// in version 0.6.1, «Выделение снято».
    var selectionToastKey: String {
        placeSelection && selectionToast?.replaced == true ? "selection.replacedToast" : "selection.clearedToast"
    }
}
