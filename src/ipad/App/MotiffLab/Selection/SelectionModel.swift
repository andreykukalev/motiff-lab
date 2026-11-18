import SwiftUI

/// The selection on the score and what goes with it (ADR 0031 section 3, S1): the passage selected, its Undo and Redo
/// (#511 11), the toast after a clear or a replace, the hint to hold (#511 2.1), the place of the selection menu, and the
/// phrases at the level shown with whether they are hidden (#479 item 21). It owns this state and the rules of its own
/// that need nothing else: the history's steps, kept for the piece beside the screen (SelectionHistories), how long the
/// toast and the hint stay, and since S3 selecting and changing the level through music-core (`select`, `showLevel`,
/// `resetLevel`, `loadPhrases`). What follows a new selection (the page, the play range, Expand) and a clear, which leaves
/// Expand first, are ScoreController's, the composition root, which hands the menu's place in here as the page gives it
/// (`placeMenu`) and takes the selection away (`answered(nil)`); ScoreController's own observers see each change of it as
/// a change of the controller. Until which step: ADR 0031, the notes of 2026-11-18 (S1, S3).
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
    static let phrasesHiddenKey = "motiff.phrases.hidden"

    /// The phrases at the level shown (0018 section 3), no longer drawn on the score (#479 item 14).
    @Published private(set) var phrases: PhraseLevelsShown?
    /// «Не показывать» in the Phrases menu (#479 item 21): the phrases are not named, in the Contents nor for the
    /// selection, until a level is chosen again. Kept on this device for every piece; the piece's level stays as it was.
    @Published private(set) var phrasesHidden = UserDefaults.standard.bool(forKey: SelectionModel.phrasesHiddenKey) {
        didSet { UserDefaults.standard.set(phrasesHidden, forKey: Self.phrasesHiddenKey) }
    }
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

    /// The selection as music-core answered it (a select, a clear, the selection moved to another level), or none.
    func answered(_ selection: ScoreSelection?) {
        self.selection = selection
    }

    /// «Не показывать» chosen, or a level chosen again: kept on this device.
    func hidePhrases(_ hidden: Bool) {
        phrasesHidden = hidden
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

    /// Another level shown in the piece (`piece`): the selection moves to the unit that is, encloses or starts it at the
    /// new level. `remember` false for the level a piece opens in, shown without becoming the piece's own.
    func showLevel(_ level: String, in piece: Int32, remember: Bool = true) async {
        let request = remember ? "{\"level\": \"\(level)\"}" : "{\"level\": \"\(level)\", \"remember\": false}"
        let answer = await PieceCore(piece: piece).phraseLevel(request).value
        if let shown = answer.map({ PhraseLevelsShown($0.phrases) }) { phrases = shown }
        if selection != nil, let moved = answer?.selection.selection.map(ScoreSelection.init) { answered(moved) }
    }

    /// «Вернуть найденные»: the level forgets the player's own boundaries and has the units found in the file again.
    func resetLevel(_ level: String, in piece: Int32) async {
        let request = "{\"reset\": true, \"level\": \"\(level)\"}"
        let answer = await MusicCore.run { MusicCore.call(piece: piece, "phrases.edit", request) }
        if let shown = PhraseLevelsShown(answer: answer) { phrases = shown }
    }

    /// The piece's phrases at the level it opens in, as music-core finds them.
    func loadPhrases(of piece: Int32) async {
        let answer = await MusicCore.run { MusicCore.call(piece: piece, "phrases") }
        phrases = PhraseLevelsShown(answer: answer)
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
