import SwiftUI

/// The selection on the score and what goes with it (ADR 0031 section 3, S1): the passage selected, its Undo and Redo
/// (#511 11), the toast after a clear or a replace, the hint to hold (#511 2.1), the place of the selection menu, and the
/// piece's phrases (the phrases spec, v1.2) with their edits' Undo and Redo and its toast. It owns this state and the
/// rules of its own that need nothing else: the history's steps, kept for the piece beside the screen (SelectionHistories), how long the toast and the hint stay, and
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

    /// What the toast after an edit of the phrases says, and what it offers: Undo after an edit or a Redo, Redo after an
    /// Undo, nothing with a refusal's reason.
    struct PhraseToast: Equatable {
        enum Offer: Equatable { case undo, redo }

        var words: String
        var offer: Offer?
        var id = UUID()
    }

    /// The card after an edit of the phrases changed the phrase the selection was exactly (the phrases spec, «Player's
    /// boundaries» 4): that phrase's number before the edit, and the span its new boundaries give, which «Выделить» selects.
    struct PhraseOffer: Equatable {
        var number: Int
        var onset: Double
        var end: Double
        var id = UUID()
    }

    /// How long the toast «Вернуть» stays (Andrei, 2026-11-14), and the hint to hold, in seconds.
    static let toastSeconds = 6.0
    static let holdHintSeconds = 2.0

    /// The piece's phrases, whose starts are marked on the score (the phrases spec, decision 1).
    @Published private(set) var phrases: PiecePhrases?
    /// The edits of the phrases' boundaries, each one step of their own Undo and Redo (the phrases spec, v1.2 i-3a): from
    /// the edits before it (`previous`) to those it left (`edits`), while the screen is open.
    @Published private(set) var phraseSteps = SelectionHistory<PhraseLevelEdits>()
    /// The toast after an edit of the phrases, its Undo or Redo, or why it could not be; nil while none shows.
    @Published private(set) var phraseToast: PhraseToast?
    /// «Выделение было фразой 2. Выделить её по новым границам?» for a while after an edit of the phrases changed the phrase
    /// the selection was exactly; nil while none shows. Only the player's «Выделить» changes the selection.
    @Published private(set) var phraseOffer: PhraseOffer?
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

    /// The selection as music-core answered it (a select, a clear), or none; the card offering a phrase's new boundaries
    /// goes with the selection there was.
    func answered(_ selection: ScoreSelection?) {
        self.selection = selection
        phraseOffer = nil
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
        let answer = await MusicCore.run { PieceCore(piece: piece).send(.phrases) }
        phrases = PiecePhrases(answer: answer)
    }

    /// An edit of the phrases' boundaries (`phrases.edit`: a mark moved, which music-core snaps to the nearest note, a
    /// boundary removed or put back where it was found): the phrases as they are now, one step of their history, and the
    /// toast with `words` and Undo (none for an edit that left the edits as they were); refused, the toast says why. The
    /// selection stays as it is (the phrases spec: nothing about phrase marks changes it); when it was a phrase exactly and
    /// the edit changed that phrase, the card offering its new boundaries shows (`phraseOffer`). Returns music-core's
    /// refusal, nil once edited.
    func editPhrases(in piece: Int32, _ request: String, done words: String) async -> String? {
        phraseOffer = nil
        let answer = await MusicCore.run { PieceCore(piece: piece).send(.phrasesEdit, request) }
        guard let edited = PiecePhrases(answer: answer) else { return refusedPhrases(answer) }
        phrases = edited
        let step = edited.edits.flatMap { phraseSteps.record(from: edited.previous, to: $0) }
        showPhraseToast(PhraseToast(words: words, offer: step == nil ? nil : .undo))
        if let change = edited.change(selected: selection) {
            showPhraseOffer(PhraseOffer(number: change.before.number, onset: change.after.span[0], end: change.after.span[1]))
        }
        return nil
    }

    /// Undo (`redo` false) or Redo of an edit of the phrases: music-core puts back the edits the step went from (or to),
    /// once those it left are the edits in force; otherwise the step goes from the history and the toast says why (c-2's
    /// `restore` and `expected`). The selection stays as it is. Returns music-core's refusal, nil once done.
    func stepPhrases(in piece: Int32, redo: Bool, done words: String) async -> String? {
        guard let step = redo ? phraseSteps.redo() : phraseSteps.undo(), let from = redo ? step.before : step.after,
              let to = redo ? step.after : step.before else { return nil }
        phraseOffer = nil
        let request = "{\"restore\": \(to.json), \"expected\": \(from.json)}"
        let answer = await MusicCore.run { PieceCore(piece: piece).send(.phrasesEdit, request) }
        guard let restored = PiecePhrases(answer: answer) else {
            phraseSteps.dropLast(undone: !redo)
            return refusedPhrases(answer)
        }
        phrases = restored
        showPhraseToast(PhraseToast(words: words, offer: redo ? .undo : .redo))
        return nil
    }

    /// An edit of the phrases refused: its reason in the toast, and returned.
    private func refusedPhrases(_ answer: String) -> String {
        let problem = FacadeAnswer.problem(answer)
        showPhraseToast(PhraseToast(words: problem, offer: nil))
        return problem
    }

    /// Shows the toast after an edit of the phrases for a while, as the selection's toast stays.
    private func showPhraseToast(_ shown: PhraseToast) {
        phraseToast = shown
        Task { [weak self] in
            try? await Task.sleep(for: .seconds(Self.toastSeconds))
            if self?.phraseToast == shown { self?.phraseToast = nil }
        }
    }

    /// The toast after an edit of the phrases put away (its ✕).
    func closePhraseToast() {
        phraseToast = nil
    }

    /// Shows the card offering a changed phrase's new boundaries for a while, as the toast stays; unanswered, it goes with
    /// the selection unchanged.
    private func showPhraseOffer(_ shown: PhraseOffer) {
        phraseOffer = shown
        Task { [weak self] in
            try? await Task.sleep(for: .seconds(Self.toastSeconds))
            if self?.phraseOffer == shown { self?.phraseOffer = nil }
        }
    }

    /// The card offering a changed phrase's new boundaries put away («Оставить», another action): the selection stays.
    func closePhraseOffer() {
        phraseOffer = nil
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
        phraseSteps = SelectionHistory()
        phraseToast = nil
        phraseOffer = nil
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
