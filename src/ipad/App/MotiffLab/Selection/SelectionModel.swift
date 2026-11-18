import SwiftUI

/// The selection on the score and what goes with it (ADR 0031 section 3, S1): the passage selected, its Undo and Redo
/// (#511 11), the toast after a clear or a replace, the hint to hold (#511 2.1), the place of the selection menu, and the
/// phrases at the level shown with whether they are hidden (#479 item 21). It owns this state and the rules of its own
/// that need nothing else: the history's steps, kept for the piece beside the screen (SelectionHistories), and how long
/// the toast and the hint stay. Selecting through music-core and what follows a new selection (the page, the play range,
/// Expand) are ScoreController's, the composition root, which hands the selection, the phrases and the menu's place in
/// here as music-core and the page answer them (`answered`, `show`, `placeMenu`); ScoreController's own observers see
/// each change of it as a change of the controller. Until which step: ADR 0031, the note of 2026-11-18 (S1).
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
    @Published var phrasesHidden = UserDefaults.standard.bool(forKey: SelectionModel.phrasesHiddenKey) {
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

    /// The phrases at the level shown, as music-core answered them.
    func show(_ phrases: PhraseLevelsShown?) {
        self.phrases = phrases
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
