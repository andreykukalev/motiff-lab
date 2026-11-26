import Foundation

/// The numbers a draft's visit shows its view under (`ShownDraftScreen`), each closed in music-core once it is no longer
/// shown (r-1): after each step the view opens again under a new number (`drafts.open`), which takes the place of the one
/// before; once the visit has left, a number still opening comes too late and is closed at once, never shown (it stayed
/// open in music-core). Two openings on their way close each other in turn, so none is left open.
@MainActor
final class DraftVisitPieces {
    /// The number shown now; nil once the visit has left.
    private(set) var current: Int32?
    private let close: @MainActor (Int32) -> Void

    /// `close` closes a number in music-core, after the screen's own leaving has read it (`ShownView.close`).
    init(showing first: Int32, close: @escaping @MainActor (Int32) -> Void = ScoreScreenContent.ShownView.close) {
        current = first
        self.close = close
    }

    /// The view opened again under `next`: shown in place of the number before, which is closed; nil once the visit has
    /// left, `next` closed with it.
    func shown(_ next: OpenPiece) -> OpenPiece? {
        guard let before = current else {
            close(next.id)
            return nil
        }
        current = next.id
        if before != next.id { close(before) }
        return next
    }

    /// The visit's screen has gone: the number shown is closed, and any opening still on its way will be.
    func leave() {
        guard let shown = current else { return }
        current = nil
        close(shown)
    }
}
