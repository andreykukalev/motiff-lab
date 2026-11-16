import UIKit
import WebKit

/// Selection by the place of the touch (v0.6.2, #601, the selection study's sections C-F): in Select the place a press
/// lands decides what it does, never how long it is held (the page's rule, Scripts/score/selection-places.ts, the same for
/// the score and the roll). Since v0.6.3 (#608, the lead's decision 1, Andrei's spec «как на макете») it is how Select
/// works on the iPad, with no switch (the web app's experiment `experimental.placeSelection` is not read here). The
/// selection is one object (Andrei, 2026-11-15): a span by beats, narrowed to a hand (`ScoreSelection.hand`, music-core's
/// filter) or to notes picked (`ScoreSelection.picked`), kept on the device, never in the project file.
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

    /// A span the roll's place rule drew (v0.6.4, `span` from Web/roll.js): selected exactly as drawn on the grid, notes in
    /// it or none; an edit of the selection keeps its hand, a new one takes both (the roll has no staves), and a replace says
    /// «Выделение заменено · Вернуть».
    func selectRollSpan(_ span: RollSpan) {
        selectSpan(span.start, span.end, hand: span.how == "edit" ? selection?.hand : nil, gesture: span.gesture, replaces: span.how == "replace")
    }

    /// Selects a passage exactly as the page drew it on the sixteenth grid (v0.6.4, selection-grid.ts; SelectionSpan.request),
    /// narrowed to a hand ("right", "left"; nil for both): a stroke within one staff on the score (the page's `strokeHand`).
    func selectSpan(_ start: Double, _ end: Double, hand: String?, gesture: String? = nil, replaces: Bool = false) {
        Task { await select(request: SelectionSpan(onset: start, end: end, hand: hand).request, gesture: gesture, replaces: replaces) }
    }
}

extension ScoreController {
    /// What the toast after a clear or a replace says: «Выделение заменено» for a replace by the place rule; otherwise
    /// «Выделение снято».
    var selectionToastKey: String {
        selectionToast?.replaced == true ? "selection.replacedToast" : "selection.clearedToast"
    }
}

// MARK: - The score page's place rule (v0.6.2 S2, Scripts/score/selection-place-layer.ts)

extension ScoreController {
    /// How far round the selection a double tap zooms to, in points.
    static var zoomMargin: CGFloat { CGFloat.ds("s-6") }

    /// The tuplet blocks the page read ({start, end} in ticks), the ones with both ends.
    nonisolated static func tuplets(_ groups: [[String: Any]]) -> [[String: Double]] {
        groups.compactMap { group in
            guard let start = (group["start"] as? NSNumber)?.doubleValue, let end = (group["end"] as? NSNumber)?.doubleValue, end > start else { return nil }
            return ["start": start, "end": end]
        }
    }

    /// A passage the place rule chose (an edge moved, extended, drawn, a bar tapped or held): by beats, narrowed to the hand
    /// the stroke stayed on; a new one in place of the selection is a replace (the toast «Выделение заменено · Вернуть»).
    func chosenByPlace(_ chosen: [String: Any], _ start: Double, _ end: Double) {
        selectSpan(start, end, hand: chosen["hand"] as? String, gesture: chosen["gesture"] as? String, replaces: Self.replaces(chosen))
    }

    /// Whether the page says a gesture's selection replaces the one there was (placeChange "replace").
    nonisolated static func replaces(_ message: [String: Any]) -> Bool {
        message["how"] as? String == "replace"
    }

    /// What a word of guidance says when music-core refused a selection the place rule asked for: a passage longer than a
    /// selection spans ("A selection spans at most N bars."), or one with no notes in it (a hand resting); nil for another.
    nonisolated static func refusal(_ message: String?) -> String? {
        guard let message else { return nil }
        if message.contains("spans at most") { return "selection.tooLong" }
        if message.contains("no notes") { return "selection.noNotes" }
        return nil
    }

    /// music-core refused a selection the place rule asked for (#601 H 11, the lead's decision 7): the band the page kept
    /// showing goes back to the selection there is, never clipped; a passage too long or with no notes says so softly, and
    /// true. For another refusal, false: the problem shows as before.
    func softlyRefused(_ message: String?) -> Bool {
        Task { await showSelection() }
        guard let key = Self.refusal(message) else { return false }
        showNotice(key)
        return true
    }

    /// A double tap inside the selection (#601 H 9): the page zooms to the selection's band (`box`, in the document's
    /// points); the next double tap goes back to the zoom and the place it came from.
    func zoomToSelection(_ box: [String: Any]?) {
        let scroll = webView.scrollView
        if let back = zoomedFrom {
            zoomedFrom = nil
            scroll.setZoomScale(back.scale, animated: true)
            scroll.setContentOffset(back.offset, animated: true)
            return
        }
        guard let rect = Self.zoomRect(box, margin: Self.zoomMargin) else { return }
        zoomedFrom = (scroll.zoomScale, scroll.contentOffset)
        scroll.zoom(to: rect, animated: true)
    }

    /// What a double tap zooms to: the band's box with `margin` round it; nil for no box.
    nonisolated static func zoomRect(_ box: [String: Any]?, margin: CGFloat) -> CGRect? {
        func number(_ key: String) -> CGFloat? { (box?[key] as? NSNumber).map { CGFloat($0.doubleValue) } }
        guard let left = number("left"), let top = number("top"), let right = number("right"), let bottom = number("bottom"), right > left, bottom > top else { return nil }
        return CGRect(x: left, y: top, width: right - left, height: bottom - top).insetBy(dx: -margin, dy: -margin)
    }
}

extension RollController {
    /// The roll's part of the one selection (v0.6.4): a span drawn there is the score's selection, exactly as drawn, and the
    /// tuplets the score's page read are the roll's blocks too, so both views stop on the same grid.
    func sharePlaces(with score: ScoreController) {
        spanPicked = { [weak score] span in score?.selectRollSpan(span) }
        score.tupletsRead = { [weak self] spans in self?.tuplets(spans) }
    }
}
