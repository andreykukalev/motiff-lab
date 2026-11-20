import SwiftUI

/// The selection on the score and what goes with it (ADR 0031 section 3, S1): the passage selected, its Undo and Redo
/// (#511 11), the toast after a clear or a replace, the hint to hold (#511 2.1), the place of the selection menu, and the
/// piece's phrases (the phrases spec, v1.2). It owns this state and the rules of its own that need nothing else: the
/// history's steps, kept for the piece beside the screen (SelectionHistories), how long the toast and the hint stay, and
/// since S3 selecting and reading the phrases through music-core (`select`, `loadPhrases`). What follows a new selection
/// (the page, the play range, Expand) and a clear, which leaves Expand first, are ScoreController's, the composition root,
/// which hands the menu's place in here as the page gives it (`placeMenu`) and takes the selection away (`answered(nil)`);
/// ScoreController's own observers see each change of it as a change of the controller. Until which step: ADR 0031, the notes of 2026-11-18 (S1, S3).
@MainActor
final class SelectionModel: ObservableObject {
    /// What a toast after a clear or a replace says, and which one it is.
    struct Toast: Equatable {
        var replaced: Bool
        var id = UUID()
    }

    /// How long the toast «Вернуть» stays (Andrei, 2026-11-14), and the hint to hold, in seconds.
    static let toastSeconds = 6.0
    static let holdHintSeconds = 2.0

    /// The piece's phrases, whose starts are marked on the score (the phrases spec, decision 1).
    @Published private(set) var phrases: PiecePhrases?
    /// The passage selected on the score: a bar tapped, or bars chosen by a long press and a drag.
    @Published private(set) var selection: ScoreSelection?
    /// The selection's Undo and Redo (#511 11, SelectionHistory), kept for the piece beside the screen (SelectionHistories).
    @Published private(set) var steps = SelectionHistory<SelectionSpan>()
    /// «Выделение снято · Вернуть» for a while after a clear or a replace (#511 11); nil while none shows.
    @Published private(set) var toast: Toast?
    /// «Задержите касание…» for a moment after a pointer that cannot scroll moved off the selection (#511 2.1, C).
    @Published private(set) var holdHint: UUID?
    /// Where the selection menu's top left corner goes in the score's view, as the web app places its menu; nil while
    /// nothing is selected or a drag is on.
    @Published private(set) var menuPlace: CGPoint?
    /// The piece as its selection's history knows it (SelectionHistories.key).
    private var historyKey: String?

    /// What the toast after a clear or a replace says: «Выделение заменено» for a replace by the place rule; otherwise
    /// «Выделение снято».
    var toastKey: String {
        toast?.replaced == true ? "selection.replacedToast" : "selection.clearedToast"
    }

    /// The selection as music-core answered it (a select, a clear), or none.
    func answered(_ selection: ScoreSelection?) {
        self.selection = selection
    }

    /// Selects through music-core in the piece (`piece`), which keeps the selection within what a selection may hold:
    /// `step` false for a selection the player did not make now (the resume point, Undo), `picked` for notes picked one by
    /// one (v0.6.2), which the selection then names, `replaces` for a new selection in place of the one there was.
    /// Returns music-core's refusal as the player reads it, nil once selected.
    func select(in piece: Int32, request: String, gesture: String?, step: Bool, picked: Bool, replaces: Bool) async -> String? {
        let answer = await PieceCore(piece: piece).select(request)
        guard var chosen = answer.value?.selection.map(ScoreSelection.init) else { return FacadeAnswer.problem(answer.text) }
        if picked { chosen.picked = chosen.noteIds }
        if step { record(from: selection, to: chosen, gesture: gesture, replaces: replaces) }
        answered(chosen)
        return nil
    }

    /// The piece's phrases, as music-core finds them and the player set them.
    func loadPhrases(of piece: Int32) async {
        let answer = await MusicCore.run { MusicCore.call(piece: piece, "phrases") }
        phrases = PiecePhrases(answer: answer)
    }

    /// Where the page places the selection menu, or where the player let it go; nil for none.
    func placeMenu(_ place: CGPoint?) {
        menuPlace = place
    }

    /// Another piece shown: its selection's history, as it was kept beside the screen.
    func open(_ piece: OpenPiece) {
        let key = SelectionHistories.key(piece)
        historyKey = key
        steps = SelectionHistories.history(of: key)
    }

    /// A change of the selection the player made: a step of the history, or a part of its gesture's step. A clear or a
    /// replace shows «Вернуть»; any other step puts it away, as it would no longer take back the clear.
    func record(from before: ScoreSelection?, to after: ScoreSelection?, gesture: String?, replaces: Bool = false) {
        guard let historyKey else { return }
        let step = steps.record(from: SelectionSpan(before), to: SelectionSpan(after), gesture: gesture, replaces: replaces)
        SelectionHistories.keep(steps, of: historyKey)
        switch step?.kind {
        case .clear?, .replace?:
            showToast(replaced: step?.kind == .replace)
        case nil where gesture == nil:
            break
        default:
            toast = nil
        }
    }

    /// The step Undo takes back, kept as taken; nil when there is none.
    func undo() -> SelectionHistory<SelectionSpan>.Step? {
        kept(steps.undo())
    }

    /// The step Redo does again, kept as done; nil when there is none.
    func redo() -> SelectionHistory<SelectionSpan>.Step? {
        kept(steps.redo())
    }

    /// The step of a gesture cancelled after its hold took the selection away, gone from the history; nil for none.
    func revert(_ gesture: String) -> SelectionHistory<SelectionSpan>.Step? {
        kept(steps.revert(gesture))
    }

    /// The hint to hold, for a moment.
    func showHoldHint() {
        let shown = UUID()
        holdHint = shown
        Task { [weak self] in
            try? await Task.sleep(for: .seconds(Self.holdHintSeconds))
            if self?.holdHint == shown { self?.holdHint = nil }
        }
    }

    /// The history after a step was taken back, done again or reverted: the toast goes, and the history is kept.
    private func kept(_ step: SelectionHistory<SelectionSpan>.Step?) -> SelectionHistory<SelectionSpan>.Step? {
        guard let step else { return nil }
        toast = nil
        if let historyKey { SelectionHistories.keep(steps, of: historyKey) }
        return step
    }

    private func showToast(replaced: Bool) {
        let shown = Toast(replaced: replaced)
        toast = shown
        Task { [weak self] in
            try? await Task.sleep(for: .seconds(Self.toastSeconds))
            if self?.toast == shown { self?.toast = nil }
        }
    }
}
